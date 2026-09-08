using System.Collections.Generic;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Settings;
using UnityAgent.Editor.Tools;
using UnityEditor;

namespace UnityAgent.Editor.Safety
{
    public sealed class ValidationResult
    {
        public bool Allowed;
        public string Reason;

        public static ValidationResult Ok() => new ValidationResult { Allowed = true };
        public static ValidationResult Deny(string reason) => new ValidationResult { Allowed = false, Reason = reason };
    }

    public sealed class ActionValidator
    {
        public ValidationResult Validate(IAgentTool tool, Dictionary<string, object> args)
        {
            if (tool == null)
                return ValidationResult.Deny("Tool is null.");

            if (args == null)
                return ValidationResult.Deny("Arguments are null.");

            // Soft schema checks for common dangerous patterns.
            if (tool.Name.Contains("delete") && args.ContainsKey("path"))
            {
                var path = ConvertPath(args["path"]);
                if (!IsInsideAssets(path) && !IsInsideProject(path))
                    return ValidationResult.Deny("Path is outside the Unity project.");
            }

            if (tool.RiskLevel == RiskLevel.High &&
                !AgentSettings.Current.AllowAssetDeletion &&
                tool.Name.IndexOf("delete", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ValidationResult.Deny("High-risk delete operations are disabled in settings.");
            }

            return ValidationResult.Ok();
        }

        static string ConvertPath(object value) => value?.ToString()?.Replace('\\', '/') ?? string.Empty;

        static bool IsInsideAssets(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            path = path.Replace('\\', '/');
            return path.StartsWith("Assets/") || path == "Assets";
        }

        static bool IsInsideProject(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var project = System.IO.Path.GetFullPath(".").Replace('\\', '/');
            var full = System.IO.Path.GetFullPath(path).Replace('\\', '/');
            return full.StartsWith(project, System.StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class PermissionManager
    {
        public bool CanExecute(IAgentTool tool, out string error)
        {
            error = null;
            var s = AgentSettings.Current;

            switch (tool.Name)
            {
                case "create_game_object":
                case "delete_game_object":
                case "rename_game_object":
                case "duplicate_game_object":
                case "set_transform":
                case "set_parent":
                case "add_component":
                case "remove_component":
                case "save_scene":
                case "create_scene":
                    if (!s.AllowSceneModification)
                    {
                        error = "Scene modification is disabled in AI Agent settings.";
                        return false;
                    }
                    break;
                case "create_script":
                case "patch_script":
                    if (!s.AllowScriptModification)
                    {
                        error = "Script modification is disabled in AI Agent settings.";
                        return false;
                    }
                    break;
                case "create_folder":
                case "find_assets":
                    if (tool.Name == "create_folder" && !s.AllowAssetCreation)
                    {
                        error = "Asset creation is disabled in AI Agent settings.";
                        return false;
                    }
                    break;
                case "enter_play_mode":
                case "exit_play_mode":
                    if (!s.AllowPlayMode)
                    {
                        error = "Play Mode control is disabled in AI Agent settings.";
                        return false;
                    }
                    break;
            }

            return true;
        }

        public bool ConfirmIfNeeded(IAgentTool tool, Dictionary<string, object> args, out string error)
        {
            error = null;
            if (tool.RiskLevel != RiskLevel.High)
            {
                if (tool.RiskLevel == RiskLevel.Medium && !AgentSettings.Current.AutoApproveMediumRisk)
                {
                    var ok = EditorUtility.DisplayDialog(
                        "AI Agent — Confirm Action",
                        $"Allow medium-risk tool '{tool.Name}'?\n\n{tool.Description}",
                        "Allow",
                        "Deny");
                    if (!ok)
                    {
                        error = "User denied medium-risk action.";
                        return false;
                    }
                }
                return true;
            }

            var message = $"Allow HIGH-risk tool '{tool.Name}'?\n\n{tool.Description}\n\nArgs: {Util.AgentJson.Serialize(args)}";
            var approved = EditorUtility.DisplayDialog("AI Agent — High Risk Action", message, "Allow", "Deny");
            if (!approved)
            {
                error = "User denied high-risk action.";
                return false;
            }
            return true;
        }
    }
}
