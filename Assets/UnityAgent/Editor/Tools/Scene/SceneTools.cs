using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.Scene
{
    public sealed class GetCurrentSceneTool : IAgentTool
    {
        public string Name => "get_current_scene";
        public string Description => "Returns information about the currently active scene.";
        public string ParameterSchema => "{}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var scene = SceneManager.GetActiveScene();
            return ToolResult.Ok("Current scene loaded.", new Dictionary<string, object>
            {
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["isDirty"] = scene.isDirty,
                ["rootCount"] = scene.rootCount,
                ["isLoaded"] = scene.isLoaded
            });
        }
    }

    public sealed class GetSceneHierarchyTool : IAgentTool
    {
        public string Name => "get_scene_hierarchy";
        public string Description => "Returns a compact hierarchy of GameObjects in the active scene with stable ids.";
        public string ParameterSchema => "{\"includeInactive\":boolean,\"maxDepth\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var includeInactive = ToolArgs.Bool(arguments, "includeInactive", true);
            var maxDepth = ToolArgs.Int(arguments, "maxDepth", 8);
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var list = new List<object>();
            foreach (var root in roots)
                list.Add(Describe(root.transform, 0, maxDepth, includeInactive));

            return ToolResult.Ok($"Hierarchy nodes: {list.Count} roots.", new Dictionary<string, object>
            {
                ["scene"] = scene.name,
                ["roots"] = list
            });
        }

        static Dictionary<string, object> Describe(Transform t, int depth, int maxDepth, bool includeInactive)
        {
            var children = new List<object>();
            if (depth < maxDepth)
            {
                for (var i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (!includeInactive && !child.gameObject.activeSelf) continue;
                    children.Add(Describe(child, depth + 1, maxDepth, includeInactive));
                }
            }

            var comps = new List<object>();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                comps.Add(c.GetType().Name);
            }

            return new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(t.gameObject),
                ["name"] = t.name,
                ["path"] = ObjectIdUtil.GetHierarchyPath(t.gameObject),
                ["active"] = t.gameObject.activeSelf,
                ["components"] = comps,
                ["children"] = children
            };
        }
    }

    public sealed class SaveSceneTool : IAgentTool
    {
        public string Name => "save_scene";
        public string Description => "Saves the currently active scene.";
        public string ParameterSchema => "{}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var scene = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.path))
                return ToolResult.Fail("Scene has no path. Use create_scene or save as first.");

            var ok = EditorSceneManager.SaveScene(scene);
            return ok
                ? ToolResult.Ok($"Scene saved: {scene.path}", new Dictionary<string, object> { ["path"] = scene.path })
                : ToolResult.Fail("Failed to save scene.");
        }
    }

    public sealed class CreateSceneTool : IAgentTool
    {
        public string Name => "create_scene";
        public string Description => "Creates and opens a new empty scene at the given Assets path.";
        public string ParameterSchema => "{\"path\":string,\"saveCurrent\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = ToolArgs.Str(arguments, "path");
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/"))
                return ToolResult.Fail("path must be under Assets/, e.g. Assets/Scenes/NewScene.unity");

            if (!path.EndsWith(".unity"))
                path += ".unity";

            if (ToolArgs.Bool(arguments, "saveCurrent", true))
                EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var ok = EditorSceneManager.SaveScene(scene, path);
            AssetDatabase.Refresh();
            return ok
                ? ToolResult.Ok($"Scene created: {path}", new Dictionary<string, object> { ["path"] = path })
                : ToolResult.Fail("Failed to create scene.");
        }
    }
}
