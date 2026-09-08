using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Diff;
using UnityAgent.Editor.Safety;
using UnityAgent.Editor.Settings;

namespace UnityAgent.Editor.Tools.Scripts
{
    public sealed class PreviewScriptPatchTool : IAgentTool
    {
        public string Name => "preview_script_patch";
        public string Description => "Previews a script patch as unified diff without writing the file. Use before patch_script when review is needed.";
        public string ParameterSchema => "{\"path\":string,\"expectedHash\":string,\"content\":string,\"oldString\":string,\"newString\":string,\"replaceAll\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!TryBuildPatchedContent(arguments, out var path, out var current, out var next, out var error))
                return ToolResult.Fail(error);

            var diff = TextDiff.Unified(current, next, path);
            DiffReview.Set(path, TextDiff.Brief(current, next), diff);
            return ToolResult.Ok("Patch preview ready.", new Dictionary<string, object>
            {
                ["path"] = path,
                ["brief"] = TextDiff.Brief(current, next),
                ["diff"] = diff,
                ["currentHash"] = ReadScriptTool.Hash(current),
                ["nextHash"] = ReadScriptTool.Hash(next)
            });
        }

        public static bool TryBuildPatchedContent(
            Dictionary<string, object> arguments,
            out string path,
            out string current,
            out string next,
            out string error)
        {
            path = ReadScriptTool.NormalizePath(ToolArgs.Str(arguments, "path"));
            current = null;
            next = null;
            error = null;

            if (!ReadScriptTool.IsValidScriptPath(path))
            {
                error = "path must be an Assets/*.cs file.";
                return false;
            }
            if (!File.Exists(path))
            {
                error = $"File not found: {path}";
                return false;
            }

            current = File.ReadAllText(path, Encoding.UTF8);
            var currentHash = ReadScriptTool.Hash(current);
            var expectedHash = ToolArgs.Str(arguments, "expectedHash");
            if (!string.IsNullOrEmpty(expectedHash) &&
                !string.Equals(expectedHash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                error = "File changed since last read (hash mismatch). Call read_script again before patching.";
                return false;
            }

            next = ToolArgs.Str(arguments, "content");
            if (string.IsNullOrEmpty(next))
            {
                var oldString = ToolArgs.Str(arguments, "oldString");
                var replacement = ToolArgs.Str(arguments, "newString", "");
                if (string.IsNullOrEmpty(oldString))
                {
                    error = "Provide content or oldString/newString.";
                    return false;
                }
                if (!current.Contains(oldString))
                {
                    error = "oldString not found in file. Re-read the script and retry.";
                    return false;
                }

                next = ToolArgs.Bool(arguments, "replaceAll", false)
                    ? current.Replace(oldString, replacement)
                    : ReplaceFirst(current, oldString, replacement);
            }

            return true;
        }

        public static string ReplaceFirst(string text, string oldValue, string newValue)
        {
            var index = text.IndexOf(oldValue, StringComparison.Ordinal);
            if (index < 0) return text;
            return text.Substring(0, index) + newValue + text.Substring(index + oldValue.Length);
        }
    }

    // Keep existing tools in ScriptTools.cs; patch tool is upgraded below via partial replacement file.
    public static class ScriptPatchApplier
    {
        public static ToolResult Apply(Dictionary<string, object> arguments)
        {
            if (!PreviewScriptPatchTool.TryBuildPatchedContent(arguments, out var path, out var current, out var next, out var error))
                return ToolResult.Fail(error);

            if (AgentSettings.Current.RequireScriptDiffApproval)
            {
                var brief = TextDiff.Brief(current, next);
                var diff = TextDiff.Unified(current, next, path, maxLines: 120);
                DiffReview.Set(path, brief, diff);
                var approved = EditorUtility.DisplayDialog(
                    "AI Agent — Approve Script Patch",
                    $"{path}\n{brief}\n\nApply this patch?\n\n(Full diff is shown in the AI Agent Diff panel / Console)",
                    "Apply",
                    "Deny");
                Debug.Log($"[UnityAgent] Script diff preview for {path}:\n{diff}");
                if (!approved)
                    return ToolResult.Fail("User denied script patch.");
            }
            else
            {
                DiffReview.Set(path, TextDiff.Brief(current, next), TextDiff.Unified(current, next, path, maxLines: 120));
            }

            var backup = ChangeTracker.BackupFile(path);
            File.WriteAllText(path, next, Encoding.UTF8);
            AssetDatabase.ImportAsset(path);
            AssetDatabase.Refresh();

            return ToolResult.Ok($"Script patched: {path}", new Dictionary<string, object>
            {
                ["path"] = path,
                ["hash"] = ReadScriptTool.Hash(next),
                ["backupPath"] = backup,
                ["brief"] = TextDiff.Brief(current, next),
                ["needsCompile"] = true
            });
        }
    }
}
