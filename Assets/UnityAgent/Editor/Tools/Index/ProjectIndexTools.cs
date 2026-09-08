using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Attributes;

namespace UnityAgent.Editor.Tools.Index
{
    [AgentTool]
    public sealed class BuildProjectIndexTool : IAgentTool
    {
        public string Name => "build_project_index";
        public string Description => "Builds a compact project index (scripts, prefabs, scenes, materials, audio, animations) and caches it for later get_project_index calls.";
        public string ParameterSchema => "{\"maxPerType\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var max = ToolArgs.Int(arguments, "maxPerType", 80);
            var index = ProjectIndexCache.Build(max);
            ProjectIndexCache.Save(index);
            return ToolResult.Ok("Project index built.", index);
        }
    }

    [AgentTool]
    public sealed class GetProjectIndexTool : IAgentTool
    {
        public string Name => "get_project_index";
        public string Description => "Returns the cached project index, rebuilding if missing.";
        public string ParameterSchema => "{\"rebuild\":boolean,\"maxPerType\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var rebuild = ToolArgs.Bool(arguments, "rebuild", false);
            var max = ToolArgs.Int(arguments, "maxPerType", 80);
            var index = (!rebuild ? ProjectIndexCache.Load() : null) ?? ProjectIndexCache.Build(max);
            if (rebuild) ProjectIndexCache.Save(index);
            return ToolResult.Ok("Project index ready.", index);
        }
    }

    public static class ProjectIndexCache
    {
        static string PathFile => System.IO.Path.Combine("Library", "UnityAgent", "project-index.json");

        public static Dictionary<string, object> Build(int maxPerType)
        {
            return new Dictionary<string, object>
            {
                ["builtUtc"] = DateTime.UtcNow.ToString("O"),
                ["unityVersion"] = Application.unityVersion,
                ["scripts"] = Collect("t:MonoScript", ".cs", maxPerType),
                ["prefabs"] = Collect("t:Prefab", ".prefab", maxPerType),
                ["scenes"] = Collect("t:SceneAsset", ".unity", maxPerType),
                ["materials"] = Collect("t:Material", ".mat", maxPerType),
                ["audio"] = Collect("t:AudioClip", null, maxPerType),
                ["animations"] = Collect("t:AnimationClip", ".anim", maxPerType),
                ["animators"] = Collect("t:AnimatorController", ".controller", maxPerType)
            };
        }

        static List<object> Collect(string filter, string extension, int max)
        {
            var guids = AssetDatabase.FindAssets(filter);
            var list = new List<object>();
            foreach (var guid in guids)
            {
                if (list.Count >= max) break;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Packages/", StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(extension) &&
                    !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["name"] = Path.GetFileNameWithoutExtension(path)
                });
            }
            return list;
        }

        public static void Save(Dictionary<string, object> index)
        {
            try
            {
                var dir = Path.GetDirectoryName(PathFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(PathFile, Util.AgentJson.Serialize(index));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityAgent] Failed to save project index: " + ex.Message);
            }
        }

        public static Dictionary<string, object> Load()
        {
            try
            {
                if (!File.Exists(PathFile)) return null;
                return Util.AgentJson.ParseObject(File.ReadAllText(PathFile));
            }
            catch
            {
                return null;
            }
        }
    }
}
