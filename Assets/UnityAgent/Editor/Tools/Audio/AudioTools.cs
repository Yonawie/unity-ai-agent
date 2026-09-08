using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Attributes;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.Audio
{
    [AgentTool]
    public sealed class AddAudioSourceTool : IAgentTool
    {
        public string Name => "add_audio_source";
        public string Description => "Adds an AudioSource to a GameObject with optional clip, loop, playOnAwake, volume.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"clipPath\":string,\"loop\":boolean,\"playOnAwake\":boolean,\"volume\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var src = go.GetComponent<AudioSource>();
            if (src == null)
                src = Undo.AddComponent<AudioSource>(go);

            Undo.RecordObject(src, "UnityAgent Configure AudioSource");
            src.loop = ToolArgs.Bool(arguments, "loop", false);
            src.playOnAwake = ToolArgs.Bool(arguments, "playOnAwake", false);
            src.volume = Mathf.Clamp01(ToolArgs.Float(arguments, "volume", 1f));

            var clipPath = ToolArgs.Str(arguments, "clipPath");
            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
                if (clip == null)
                    return ToolResult.Fail($"AudioClip not found: {clipPath}");
                src.clip = clip;
            }

            return ToolResult.Ok("AudioSource ready.", new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(go),
                ["clipPath"] = clipPath,
                ["volume"] = src.volume,
                ["loop"] = src.loop
            });
        }
    }

    [AgentTool]
    public sealed class ListAudioClipsTool : IAgentTool
    {
        public string Name => "list_audio_clips";
        public string Description => "Lists AudioClip assets in the project.";
        public string ParameterSchema => "{\"filter\":string,\"maxResults\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var filter = ToolArgs.Str(arguments, "filter", "");
            var max = ToolArgs.Int(arguments, "maxResults", 50);
            var guids = AssetDatabase.FindAssets("t:AudioClip");
            var list = new List<object>();
            foreach (var guid in guids)
            {
                if (list.Count >= max) break;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(filter) &&
                    path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["name"] = Path.GetFileNameWithoutExtension(path)
                });
            }
            return ToolResult.Ok($"Audio clips: {list.Count}", new Dictionary<string, object> { ["clips"] = list });
        }
    }

    [AgentTool]
    public sealed class AssignAudioClipTool : IAgentTool
    {
        public string Name => "assign_audio_clip";
        public string Description => "Assigns an AudioClip asset to an existing AudioSource.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"clipPath\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);
            var src = go.GetComponent<AudioSource>();
            if (src == null)
                return ToolResult.Fail("No AudioSource on object. Use add_audio_source first.");

            var clipPath = ToolArgs.Str(arguments, "clipPath");
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (clip == null)
                return ToolResult.Fail($"AudioClip not found: {clipPath}");

            Undo.RecordObject(src, "UnityAgent Assign AudioClip");
            src.clip = clip;
            return ToolResult.Ok("AudioClip assigned.", new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(go),
                ["clipPath"] = clipPath
            });
        }
    }
}
