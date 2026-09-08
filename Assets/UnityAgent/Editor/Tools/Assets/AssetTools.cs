using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityAgent.Editor.Agent;

namespace UnityAgent.Editor.Tools.Assets
{
    public sealed class FindAssetsTool : IAgentTool
    {
        public string Name => "find_assets";
        public string Description => "Finds assets using AssetDatabase filter (e.g. t:MonoScript Player, t:Prefab).";
        public string ParameterSchema => "{\"filter\":string,\"folders\":[string],\"maxResults\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var filter = ToolArgs.Str(arguments, "filter", "");
            var maxResults = ToolArgs.Int(arguments, "maxResults", 50);
            string[] folders = null;
            if (arguments != null && arguments.TryGetValue("folders", out var f) && f is List<object> list)
            {
                folders = list.Select(x => x?.ToString()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
            }

            var guids = folders == null || folders.Length == 0
                ? AssetDatabase.FindAssets(filter)
                : AssetDatabase.FindAssets(filter, folders);

            var results = new List<object>();
            foreach (var guid in guids)
            {
                if (results.Count >= maxResults) break;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                results.Add(new Dictionary<string, object>
                {
                    ["guid"] = guid,
                    ["path"] = path,
                    ["type"] = AssetDatabase.GetMainAssetTypeAtPath(path)?.Name
                });
            }

            return ToolResult.Ok($"Found {results.Count} assets.", new Dictionary<string, object> { ["assets"] = results });
        }
    }

    public sealed class CreateFolderTool : IAgentTool
    {
        public string Name => "create_folder";
        public string Description => "Creates a folder under Assets.";
        public string ParameterSchema => "{\"path\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = ToolArgs.Str(arguments, "path")?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return ToolResult.Fail("path must start with Assets/");

            if (AssetDatabase.IsValidFolder(path) || Directory.Exists(path))
                return ToolResult.Ok("Folder already exists.", new Dictionary<string, object> { ["path"] = path });

            var parts = path.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var guid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(guid))
                        return ToolResult.Fail($"Failed to create folder: {next}");
                }
                current = next;
            }

            AssetDatabase.Refresh();
            return ToolResult.Ok($"Folder created: {path}", new Dictionary<string, object> { ["path"] = path });
        }
    }
}
