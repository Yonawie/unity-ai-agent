using UnityAgent.Editor.Tools.Assets;
using UnityAgent.Editor.Tools.CameraTools;
using UnityAgent.Editor.Tools.Capture;
using UnityAgent.Editor.Tools.Console;
using UnityAgent.Editor.Tools.EditorTools;
using UnityAgent.Editor.Tools.GameObjects;
using UnityAgent.Editor.Tools.InputTools;
using UnityAgent.Editor.Tools.Materials;
using UnityAgent.Editor.Tools.Prefabs;
using UnityAgent.Editor.Tools.Scene;
using UnityAgent.Editor.Tools.Scripts;
using UnityAgent.Editor.Tools.SelectionTools;
using UnityAgent.Editor.Tools.UI;

namespace UnityAgent.Editor.Tools
{
    public static class ToolBootstrap
    {
        public static ToolRegistry CreateDefaultRegistry()
        {
            var registry = new ToolRegistry();

            // Scene
            registry.Register(new GetCurrentSceneTool());
            registry.Register(new GetSceneHierarchyTool());
            registry.Register(new SaveSceneTool());
            registry.Register(new CreateSceneTool());

            // GameObjects
            registry.Register(new CreateGameObjectTool());
            registry.Register(new DeleteGameObjectTool());
            registry.Register(new RenameGameObjectTool());
            registry.Register(new DuplicateGameObjectTool());
            registry.Register(new SetTransformTool());
            registry.Register(new SetParentTool());
            registry.Register(new GetComponentsTool());
            registry.Register(new AddComponentTool());
            registry.Register(new RemoveComponentTool());

            // Selection / tags / layers
            registry.Register(new GetSelectionTool());
            registry.Register(new SetSelectionTool());
            registry.Register(new FocusObjectTool());
            registry.Register(new SetTagTool());
            registry.Register(new SetLayerTool());

            // Prefabs
            registry.Register(new CreatePrefabTool());
            registry.Register(new InstantiatePrefabTool());
            registry.Register(new UnpackPrefabTool());
            registry.Register(new GetPrefabInfoTool());

            // Materials
            registry.Register(new CreateMaterialTool());
            registry.Register(new AssignMaterialTool());
            registry.Register(new SetMaterialColorTool());

            // UI (UGUI)
            registry.Register(new CreateCanvasTool());
            registry.Register(new CreateUiTextTool());
            registry.Register(new CreateUiButtonTool());
            registry.Register(new CreateUiPanelTool());
            registry.Register(new SetRectTransformTool());

            // Camera / lights
            registry.Register(new SetupMainCameraTool());
            registry.Register(new CreateThirdPersonCameraTool());
            registry.Register(new CreateLightTool());

            // Capture
            registry.Register(new CaptureSceneViewTool());
            registry.Register(new CaptureGameViewTool());
            registry.Register(new ListCapturesTool());

            // Scripts
            registry.Register(new ListScriptsTool());
            registry.Register(new ReadScriptTool());
            registry.Register(new CreateScriptTool());
            registry.Register(new PreviewScriptPatchTool());
            registry.Register(new PatchScriptTool());

            // Input helpers
            registry.Register(new DetectInputSetupTool());
            registry.Register(new CreateWasdControllerScriptTool());

            // Console
            registry.Register(new ReadConsoleTool());
            registry.Register(new ClearConsoleTool());

            // Assets / Project
            registry.Register(new FindAssetsTool());
            registry.Register(new CreateFolderTool());

            // Editor
            registry.Register(new EnterPlayModeTool());
            registry.Register(new ExitPlayModeTool());

            return registry;
        }
    }
}
