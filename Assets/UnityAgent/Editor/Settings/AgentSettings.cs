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
        public string Provider = "LMStudio";
        public string BaseUrl = "http://127.0.0.1:1234/v1";
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
        public bool EnableStreaming = false;
        public bool EnableVision = true;
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

        /// <summary>Force LM Studio defaults and save (overwrites Provider/BaseUrl).</summary>
        public static void ApplyLmStudioDefaults(bool keepModel = true)
        {
            var model = keepModel ? Current.Model : "";
            _cached ??= new AgentSettingsData();
            _cached.Provider = "LMStudio";
            _cached.BaseUrl = "http://127.0.0.1:1234/v1";
            _cached.EnableStreaming = false;
            if (!keepModel) _cached.Model = "";
            else _cached.Model = model ?? "";
            Save();
        }

        static AgentSettingsData Load()
        {
            AgentSettingsData data = null;
            try
            {
                var projectPath = Path.Combine("ProjectSettings", "UnityAgentSettings.json");
                if (File.Exists(projectPath))
                {
                    var obj = AgentJson.ParseObject(File.ReadAllText(projectPath));
                    if (obj != null) data = FromDict(obj);
                }
            }
            catch { /* fallback */ }

            if (data == null)
            {
                var json = EditorPrefs.GetString(PrefsKey, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var obj = AgentJson.ParseObject(json);
                        if (obj != null) data = FromDict(obj);
                    }
                    catch { /* ignore */ }
                }
            }

            data ??= new AgentSettingsData();
            MigrateToLmStudioIfNeeded(data);
            return data;
        }

        static void MigrateToLmStudioIfNeeded(AgentSettingsData data)
        {
            if (data == null) return;
            var provider = data.Provider ?? "";
            var url = data.BaseUrl ?? "";

            // User pointed at LM Studio port but left Ollama provider.
            var looksLikeLmStudioPort = url.Contains(":1234");
            var isOllama = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
            if (isOllama && looksLikeLmStudioPort)
            {
                data.Provider = "LMStudio";
                if (!url.Contains("/v1"))
                    data.BaseUrl = url.TrimEnd('/') + "/v1";
                data.EnableStreaming = false;
                try { SaveWith(data); } catch { /* ignore */ }
            }
        }

        static void SaveWith(AgentSettingsData d)
        {
            _cached = d;
            Save();
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
            ["RequireScriptDiffApproval"] = d.RequireScriptDiffApproval,
            ["EnableStreaming"] = d.EnableStreaming,
            ["EnableVision"] = d.EnableVision
        };

        static AgentSettingsData FromDict(System.Collections.Generic.Dictionary<string, object> o) => new AgentSettingsData
        {
            Provider = AgentJson.GetString(o, "Provider", "LMStudio"),
            BaseUrl = AgentJson.GetString(o, "BaseUrl", "http://127.0.0.1:1234/v1"),
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
            RequireScriptDiffApproval = AgentJson.GetBool(o, "RequireScriptDiffApproval", false),
            EnableStreaming = AgentJson.GetBool(o, "EnableStreaming", false),
            EnableVision = AgentJson.GetBool(o, "EnableVision", true)
        };
    }
}
