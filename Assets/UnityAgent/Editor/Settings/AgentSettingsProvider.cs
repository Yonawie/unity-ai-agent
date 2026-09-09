using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;

namespace UnityAgent.Editor.Settings
{
    public class AgentSettingsProvider : SettingsProvider
    {
        static readonly string[] Providers =
        {
            "LMStudio",
            "Ollama",
            "OpenAICompatible",
            "OpenAI",
            "Claude",
            "Gemini"
        };

        public AgentSettingsProvider()
            : base("Project/AI Agent", SettingsScope.Project)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new AgentSettingsProvider
            {
                keywords = new HashSet<string>(new[] { "AI", "Agent", "LMStudio", "Ollama", "LLM", "UnityAgent" })
            };
        }

        public override void OnGUI(string searchContext)
        {
            var s = AgentSettings.Current;
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("LLM Provider", EditorStyles.boldLabel);

            var providerIndex = Mathf.Max(0, Array.FindIndex(Providers,
                p => p.Equals(s.Provider, StringComparison.OrdinalIgnoreCase)));
            if (string.IsNullOrWhiteSpace(s.Provider))
                providerIndex = 0;
            else if (Array.FindIndex(Providers, p => p.Equals(s.Provider, StringComparison.OrdinalIgnoreCase)) < 0)
            {
                // Keep custom provider as free text below popup
                providerIndex = -1;
            }

            var newIndex = EditorGUILayout.Popup("Provider", Math.Max(providerIndex, 0), Providers);
            if (providerIndex >= 0 || newIndex != 0)
                s.Provider = Providers[newIndex];

            s.Provider = EditorGUILayout.TextField("Provider (exact)", s.Provider);
            s.BaseUrl = EditorGUILayout.TextField("Base URL", s.BaseUrl);
            s.Model = EditorGUILayout.TextField("Model", s.Model);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Use LM Studio Defaults"))
            {
                AgentSettings.ApplyLmStudioDefaults();
                GUI.FocusControl(null);
            }
            if (GUILayout.Button("Use Ollama Defaults"))
            {
                s.Provider = "Ollama";
                s.BaseUrl = "http://localhost:11434";
                AgentSettings.Save();
            }
            EditorGUILayout.EndHorizontal();

            s.Temperature = EditorGUILayout.Slider("Temperature", s.Temperature, 0f, 2f);
            s.MaxContext = EditorGUILayout.IntField("Max Context", s.MaxContext);
            s.MaxAgentSteps = EditorGUILayout.IntField("Max Agent Steps", s.MaxAgentSteps);
            s.MaxFixAttempts = EditorGUILayout.IntField("Max Fix Attempts", s.MaxFixAttempts);
            s.RequestTimeoutSeconds = EditorGUILayout.IntField("Request Timeout (sec)", s.RequestTimeoutSeconds);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Default Mode", EditorStyles.boldLabel);
            var mode = AgentSettings.DefaultMode;
            mode = (AgentMode)EditorGUILayout.EnumPopup("Mode", mode);
            s.DefaultMode = mode.ToString();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Permissions", EditorStyles.boldLabel);
            s.AllowSceneModification = EditorGUILayout.Toggle("Allow Scene Modification", s.AllowSceneModification);
            s.AllowScriptModification = EditorGUILayout.Toggle("Allow Script Modification", s.AllowScriptModification);
            s.AllowAssetCreation = EditorGUILayout.Toggle("Allow Asset Creation", s.AllowAssetCreation);
            s.AllowAssetDeletion = EditorGUILayout.Toggle("Allow Asset Deletion", s.AllowAssetDeletion);
            s.AllowProjectSettingsModification = EditorGUILayout.Toggle("Allow Project Settings Modification", s.AllowProjectSettingsModification);
            s.AllowPlayMode = EditorGUILayout.Toggle("Allow Play Mode", s.AllowPlayMode);
            s.AutoApproveMediumRisk = EditorGUILayout.Toggle("Auto-approve Medium Risk", s.AutoApproveMediumRisk);
            s.RequireScriptDiffApproval = EditorGUILayout.Toggle("Require Script Diff Approval", s.RequireScriptDiffApproval);
            s.EnableStreaming = EditorGUILayout.Toggle("Enable LLM Streaming", s.EnableStreaming);
            s.EnableVision = EditorGUILayout.Toggle("Enable Vision (image captures)", s.EnableVision);

            if (EditorGUI.EndChangeCheck())
                AgentSettings.Save();
        }
    }
}
