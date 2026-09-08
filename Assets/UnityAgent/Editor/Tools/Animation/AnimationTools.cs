using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Attributes;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.AnimationTools
{
    [AgentTool]
    public sealed class CreateAnimatorControllerTool : IAgentTool
    {
        public string Name => "create_animator_controller";
        public string Description => "Creates an AnimatorController asset with an optional default empty state/clip.";
        public string ParameterSchema => "{\"path\":string,\"defaultState\":string,\"createIdleClip\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = ToolArgs.Str(arguments, "path", "Assets/Animations/NewAnimator.controller");
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/"))
                return ToolResult.Fail("path must start with Assets/");
            if (!path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                path += ".controller";

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(path))
                return ToolResult.Fail($"AnimatorController already exists: {path}");

            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var stateName = ToolArgs.Str(arguments, "defaultState", "Idle");
            string clipPath = null;

            if (ToolArgs.Bool(arguments, "createIdleClip", true))
            {
                clipPath = Path.ChangeExtension(path, null) + "_Idle.anim";
                var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(clipPath) };
                // Tiny placeholder curve so the clip is valid.
                clip.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Constant(0, 0.1f, 0f));
                AssetDatabase.CreateAsset(clip, clipPath);
                var root = controller.layers[0].stateMachine;
                var state = root.AddState(stateName);
                state.motion = clip;
                root.defaultState = state;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return ToolResult.Ok($"AnimatorController created: {path}", new Dictionary<string, object>
            {
                ["path"] = path,
                ["idleClipPath"] = clipPath,
                ["defaultState"] = stateName
            });
        }
    }

    [AgentTool]
    public sealed class AddAnimatorTool : IAgentTool
    {
        public string Name => "add_animator";
        public string Description => "Adds an Animator component to a GameObject and optionally assigns a controller asset.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"controllerPath\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var animator = go.GetComponent<Animator>();
            if (animator == null)
                animator = Undo.AddComponent<Animator>(go);

            var controllerPath = ToolArgs.Str(arguments, "controllerPath");
            if (!string.IsNullOrEmpty(controllerPath))
            {
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
                if (controller == null)
                    return ToolResult.Fail($"Controller not found: {controllerPath}");
                Undo.RecordObject(animator, "UnityAgent Assign AnimatorController");
                animator.runtimeAnimatorController = controller;
            }

            return ToolResult.Ok("Animator ready.", new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(go),
                ["controllerPath"] = controllerPath,
                ["hasController"] = animator.runtimeAnimatorController != null
            });
        }
    }

    [AgentTool]
    public sealed class ListAnimationClipsTool : IAgentTool
    {
        public string Name => "list_animation_clips";
        public string Description => "Lists AnimationClip assets in the project.";
        public string ParameterSchema => "{\"filter\":string,\"maxResults\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var filter = ToolArgs.Str(arguments, "filter", "");
            var max = ToolArgs.Int(arguments, "maxResults", 50);
            var guids = AssetDatabase.FindAssets("t:AnimationClip");
            var list = new List<object>();
            foreach (var guid in guids)
            {
                if (list.Count >= max) break;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(filter) &&
                    path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(new Dictionary<string, object> { ["path"] = path, ["name"] = Path.GetFileNameWithoutExtension(path) });
            }
            return ToolResult.Ok($"Clips: {list.Count}", new Dictionary<string, object> { ["clips"] = list });
        }
    }
}
