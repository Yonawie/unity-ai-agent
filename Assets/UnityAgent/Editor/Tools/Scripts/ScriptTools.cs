using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;

namespace UnityAgent.Editor.Tools.Scripts
{
    public sealed class ListScriptsTool : IAgentTool
    {
        public string Name => "list_scripts";
        public string Description => "Lists C# scripts under Assets (optional filter).";
        public string ParameterSchema => "{\"filter\":string,\"folder\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var folder = ToolArgs.Str(arguments, "folder", "Assets");
            var filter = ToolArgs.Str(arguments, "filter", "");
            var guids = AssetDatabase.FindAssets("t:MonoScript", new[] { folder });
            var list = new List<object>();
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    path.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    Path.GetFileNameWithoutExtension(path).IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                list.Add(new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["name"] = Path.GetFileNameWithoutExtension(path)
                });
            }

            return ToolResult.Ok($"Found {list.Count} scripts.", new Dictionary<string, object> { ["scripts"] = list });
        }
    }

    public sealed class ReadScriptTool : IAgentTool
    {
        public string Name => "read_script";
        public string Description => "Reads a C# script file content from Assets.";
        public string ParameterSchema => "{\"path\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = NormalizePath(ToolArgs.Str(arguments, "path"));
            if (!IsValidScriptPath(path))
                return ToolResult.Fail("path must be an Assets/*.cs file.");
            if (!File.Exists(path))
                return ToolResult.Fail($"File not found: {path}");

            var content = File.ReadAllText(path, Encoding.UTF8);
            var hash = Hash(content);
            return ToolResult.Ok("Script read.", new Dictionary<string, object>
            {
                ["path"] = path,
                ["content"] = content,
                ["hash"] = hash,
                ["lineCount"] = content.Split('\n').Length
            });
        }

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            path = path.Replace('\\', '/').Trim();
            if (path.StartsWith("./")) path = path.Substring(2);
            return path;
        }

        public static bool IsValidScriptPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            path = path.Replace('\\', '/');
            return path.StartsWith("Assets/", StringComparison.Ordinal) &&
                   path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                   !path.Contains("..");
        }

        public static string Hash(string content)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(content ?? string.Empty));
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    public sealed class CreateScriptTool : IAgentTool
    {
        public string Name => "create_script";
        public string Description => "Creates a new C# MonoBehaviour script under Assets. Prefer providing full class content.";
        public string ParameterSchema => "{\"path\":string,\"className\":string,\"content\":string}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var className = ToolArgs.Str(arguments, "className");
            var path = ReadScriptTool.NormalizePath(ToolArgs.Str(arguments, "path"));
            var content = ToolArgs.Str(arguments, "content");

            if (string.IsNullOrWhiteSpace(className) && !string.IsNullOrWhiteSpace(path))
                className = Path.GetFileNameWithoutExtension(path);

            if (string.IsNullOrWhiteSpace(className))
                return ToolResult.Fail("className or path is required.");

            if (string.IsNullOrWhiteSpace(path))
                path = $"Assets/{className}.cs";

            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                path += ".cs";

            if (!ReadScriptTool.IsValidScriptPath(path))
                return ToolResult.Fail("path must be under Assets/ and end with .cs");

            if (File.Exists(path))
                return ToolResult.Fail($"Script already exists: {path}. Use patch_script instead.");

            if (string.IsNullOrWhiteSpace(content))
            {
                content =
$@"using UnityEngine;

public class {className} : MonoBehaviour
{{
    void Update()
    {{
    }}
}}
";
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, content, Encoding.UTF8);
            AssetDatabase.ImportAsset(path);
            AssetDatabase.Refresh();

            return ToolResult.Ok($"Script created: {path}", new Dictionary<string, object>
            {
                ["path"] = path,
                ["className"] = className,
                ["hash"] = ReadScriptTool.Hash(content),
                ["needsCompile"] = true
            });
        }
    }

    public sealed class PatchScriptTool : IAgentTool
    {
        public string Name => "patch_script";
        public string Description => "Applies a targeted patch to a script. Provide expectedHash from read_script, and either full content replacement via 'content', or search/replace via oldString/newString. May show a diff approval dialog when enabled in settings.";
        public string ParameterSchema => "{\"path\":string,\"expectedHash\":string,\"content\":string,\"oldString\":string,\"newString\":string,\"replaceAll\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments) => ScriptPatchApplier.Apply(arguments);
    }
}
