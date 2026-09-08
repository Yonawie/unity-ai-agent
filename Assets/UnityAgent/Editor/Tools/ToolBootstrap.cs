using UnityAgent.Editor.Tools.Assets;
using UnityAgent.Editor.Tools.Console;
using UnityAgent.Editor.Tools.EditorTools;
using UnityAgent.Editor.Tools.GameObjects;
using UnityAgent.Editor.Tools.Scene;
using UnityAgent.Editor.Tools.Scripts;

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

            // Scripts
            registry.Register(new ListScriptsTool());
            registry.Register(new ReadScriptTool());
            registry.Register(new CreateScriptTool());
            registry.Register(new PatchScriptTool());

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
