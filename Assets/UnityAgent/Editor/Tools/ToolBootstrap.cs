using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityAgent.Editor.Attributes;
using UnityAgent.Editor.Logging;
using UnityAgent.Editor.Tools.AnimationTools;
using UnityAgent.Editor.Tools.Assets;
using UnityAgent.Editor.Tools.Audio;
using UnityAgent.Editor.Tools.CameraTools;
using UnityAgent.Editor.Tools.Capture;
using UnityAgent.Editor.Tools.Console;
using UnityAgent.Editor.Tools.EditorTools;
using UnityAgent.Editor.Tools.GameObjects;
using UnityAgent.Editor.Tools.Index;
using UnityAgent.Editor.Tools.InputTools;
using UnityAgent.Editor.Tools.Materials;
using UnityAgent.Editor.Tools.Prefabs;
using UnityAgent.Editor.Tools.Scene;
using UnityAgent.Editor.Tools.Scripts;
using UnityAgent.Editor.Tools.SelectionTools;
using UnityAgent.Editor.Tools.Testing;
using UnityAgent.Editor.Tools.UI;
using UnityAgent.Editor.Tools.Vision;

namespace UnityAgent.Editor.Tools
{
    public static class ToolBootstrap
    {
        public static ToolRegistry CreateDefaultRegistry()
        {
            var registry = new ToolRegistry();

            // Explicit core set (order stable for prompts)
            RegisterMany(registry,
                new GetCurrentSceneTool(), new GetSceneHierarchyTool(), new SaveSceneTool(), new CreateSceneTool(),
                new CreateGameObjectTool(), new DeleteGameObjectTool(), new RenameGameObjectTool(), new DuplicateGameObjectTool(),
                new SetTransformTool(), new SetParentTool(), new GetComponentsTool(), new AddComponentTool(), new RemoveComponentTool(),
                new GetSelectionTool(), new SetSelectionTool(), new FocusObjectTool(), new SetTagTool(), new SetLayerTool(),
                new CreatePrefabTool(), new InstantiatePrefabTool(), new UnpackPrefabTool(), new GetPrefabInfoTool(),
                new CreateMaterialTool(), new AssignMaterialTool(), new SetMaterialColorTool(),
                new CreateCanvasTool(), new CreateUiTextTool(), new CreateUiButtonTool(), new CreateUiPanelTool(), new SetRectTransformTool(),
                new SetupMainCameraTool(), new CreateThirdPersonCameraTool(), new CreateLightTool(),
                new CaptureSceneViewTool(), new CaptureGameViewTool(), new ListCapturesTool(), new AnalyzeCaptureTool(),
                new ListScriptsTool(), new ReadScriptTool(), new CreateScriptTool(), new PreviewScriptPatchTool(), new PatchScriptTool(),
                new DetectInputSetupTool(), new CreateWasdControllerScriptTool(),
                new CreateAnimatorControllerTool(), new AddAnimatorTool(), new ListAnimationClipsTool(),
                new AddAudioSourceTool(), new AssignAudioClipTool(), new ListAudioClipsTool(),
                new BuildProjectIndexTool(), new GetProjectIndexTool(),
                new RunPlayModeSmokeTool(), new GetPlayModeSmokeResultTool(),
                new ReadConsoleTool(), new ClearConsoleTool(),
                new FindAssetsTool(), new CreateFolderTool(),
                new EnterPlayModeTool(), new ExitPlayModeTool()
            );

            AutoDiscover(registry);
            return registry;
        }

        static void RegisterMany(ToolRegistry registry, params IAgentTool[] tools)
        {
            foreach (var t in tools) registry.Register(t);
        }

        /// <summary>
        /// Discovers additional IAgentTool types from loaded assemblies:
        /// - any type with [AgentTool]
        /// - any concrete IAgentTool in UnityAgent.* namespaces
        /// </summary>
        public static void AutoDiscover(ToolRegistry registry)
        {
            var existing = new HashSet<string>(registry.All.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract || type.IsInterface) continue;
                    if (!typeof(IAgentTool).IsAssignableFrom(type)) continue;

                    var attr = type.GetCustomAttribute<AgentToolAttribute>();
                    var inUnityAgent = type.Namespace != null &&
                                       type.Namespace.StartsWith("UnityAgent", StringComparison.Ordinal);
                    if (attr == null && !inUnityAgent) continue;

                    try
                    {
                        if (Activator.CreateInstance(type) is not IAgentTool tool) continue;
                        if (existing.Contains(tool.Name)) continue;
                        registry.Register(tool);
                        existing.Add(tool.Name);
                        AgentLogger.Info($"Auto-discovered tool: {tool.Name} ({type.FullName})");
                    }
                    catch (Exception ex)
                    {
                        AgentLogger.Warn($"Failed to auto-register {type.FullName}: {ex.Message}");
                    }
                }
            }
        }
    }
}
