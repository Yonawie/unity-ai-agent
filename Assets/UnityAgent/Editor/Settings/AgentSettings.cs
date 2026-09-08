using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Settings
{
    [Serializable]
    public class AgentSettingsData
    {
        public string Provider = "Ollama";
        public string BaseUrl = "http://localhost:11434";
        public string Model = "";
        public float Temperature = 0.2f;
        public int MaxContext = 16000;
        public int MaxAgentSteps = 30;
        public int MaxFixAttempts = 5;
        public int RequestTimeoutSeconds = 120;
        public string DefaultMode = "Agent";

        public bool AllowSceneModification = true;
        public bool AllowScriptModification = true;
        public bool AllowAssetCreation = true;
        public bool AllowAssetDeletion = false;
        public bool AllowProjectSettingsModification = false;
        public bool AllowPlayMode = true;
        public bool AutoApproveMediumRisk = true;
        public bool RequireScriptDiffApproval = false;
    }

    public static class AgentSettings
    {
        const string PrefsKey = "UnityAgent.Settings.Json";
        static AgentSettingsData _cached;

        public static AgentSettingsData Current
        {
            get
            {
                if (_cached != null) return _cached;
                _cached = Load();
                return _cached;
            }
        }

        public static AgentMode DefaultMode
        {
            get
            {
                return Enum.TryParse(Current.DefaultMode, true, out AgentMode mode)
                    ? mode
                    : AgentMode.Agent;
            }
        }

        public static void Save()
        {
            if (_cached == null) _cached = new AgentSettingsData();
            EditorPrefs.SetString(PrefsKey, AgentJson.Serialize(ToDict(_cached)));
            try
            {
                var dir = Path.Combine("ProjectSettings");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "UnityAgentSettings.json");
                File.WriteAllText(path, AgentJson.Serialize(ToDict(_cached)));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] Failed to write ProjectSettings/UnityAgentSettings.json: {ex.Message}");
            }
        }

        public static void Reload()
        {
            _cached = Load();
        }

        static AgentSettingsData Load()
        {
            try
            {
                var projectPath = Path.Combine("ProjectSettings", "UnityAgentSettings.json");
                if (File.Exists(projectPath))
                {
                    var obj = AgentJson.ParseObject(File.ReadAllText(projectPath));
                    if (obj != null) return FromDict(obj);
                }
            }
            catch { /* fallback */ }

            var json = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var obj = AgentJson.ParseObject(json);
                    if (obj != null) return FromDict(obj);
                }
                catch { /* ignore */ }
            }

            return new AgentSettingsData();
        }

        static System.Collections.Generic.Dictionary<string, object> ToDict(AgentSettingsData d) => new System.Collections.Generic.Dictionary<string, object>
        {
            ["Provider"] = d.Provider,
            ["BaseUrl"] = d.BaseUrl,
            ["Model"] = d.Model,
            ["Temperature"] = d.Temperature,
            ["MaxContext"] = d.MaxContext,
            ["MaxAgentSteps"] = d.MaxAgentSteps,
            ["MaxFixAttempts"] = d.MaxFixAttempts,
            ["RequestTimeoutSeconds"] = d.RequestTimeoutSeconds,
            ["DefaultMode"] = d.DefaultMode,
            ["AllowSceneModification"] = d.AllowSceneModification,
            ["AllowScriptModification"] = d.AllowScriptModification,
            ["AllowAssetCreation"] = d.AllowAssetCreation,
            ["AllowAssetDeletion"] = d.AllowAssetDeletion,
            ["AllowProjectSettingsModification"] = d.AllowProjectSettingsModification,
            ["AllowPlayMode"] = d.AllowPlayMode,
            ["AutoApproveMediumRisk"] = d.AutoApproveMediumRisk,
            ["RequireScriptDiffApproval"] = d.RequireScriptDiffApproval
        };

        static AgentSettingsData FromDict(System.Collections.Generic.Dictionary<string, object> o) => new AgentSettingsData
        {
            Provider = AgentJson.GetString(o, "Provider", "Ollama"),
            BaseUrl = AgentJson.GetString(o, "BaseUrl", "http://localhost:11434"),
            Model = AgentJson.GetString(o, "Model", ""),
            Temperature = AgentJson.GetFloat(o, "Temperature", 0.2f),
            MaxContext = AgentJson.GetInt(o, "MaxContext", 16000),
            MaxAgentSteps = AgentJson.GetInt(o, "MaxAgentSteps", 30),
            MaxFixAttempts = AgentJson.GetInt(o, "MaxFixAttempts", 5),
            RequestTimeoutSeconds = AgentJson.GetInt(o, "RequestTimeoutSeconds", 120),
            DefaultMode = AgentJson.GetString(o, "DefaultMode", "Agent"),
            AllowSceneModification = AgentJson.GetBool(o, "AllowSceneModification", true),
            AllowScriptModification = AgentJson.GetBool(o, "AllowScriptModification", true),
            AllowAssetCreation = AgentJson.GetBool(o, "AllowAssetCreation", true),
            AllowAssetDeletion = AgentJson.GetBool(o, "AllowAssetDeletion", false),
            AllowProjectSettingsModification = AgentJson.GetBool(o, "AllowProjectSettingsModification", false),
            AllowPlayMode = AgentJson.GetBool(o, "AllowPlayMode", true),
            AutoApproveMediumRisk = AgentJson.GetBool(o, "AutoApproveMediumRisk", true),
            RequireScriptDiffApproval = AgentJson.GetBool(o, "RequireScriptDiffApproval", false)
        };
    }
}
