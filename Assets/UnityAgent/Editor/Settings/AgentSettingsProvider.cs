using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Settings;

namespace UnityAgent.Editor.Settings
{
    public class AgentSettingsProvider : SettingsProvider
    {
        public AgentSettingsProvider()
            : base("Project/AI Agent", SettingsScope.Project)
        {
        }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new AgentSettingsProvider
            {
                keywords = new HashSet<string>(new[] { "AI", "Agent", "Ollama", "LLM", "UnityAgent" })
            };
        }

        public override void OnGUI(string searchContext)
        {
            var s = AgentSettings.Current;
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("LLM Provider", EditorStyles.boldLabel);
            s.Provider = EditorGUILayout.TextField("Provider", s.Provider);
            s.BaseUrl = EditorGUILayout.TextField("Base URL", s.BaseUrl);
            s.Model = EditorGUILayout.TextField("Model", s.Model);
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

            if (EditorGUI.EndChangeCheck())
                AgentSettings.Save();
        }
    }
}
