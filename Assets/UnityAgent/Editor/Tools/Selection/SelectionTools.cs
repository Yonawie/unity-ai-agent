using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.SelectionTools
{
    public sealed class GetSelectionTool : IAgentTool
    {
        public string Name => "get_selection";
        public string Description => "Returns currently selected GameObjects with stable ids.";
        public string ParameterSchema => "{}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var selected = Selection.gameObjects;
            var list = new List<object>();
            foreach (var go in selected)
                list.Add(ObjectIdUtil.Describe(go));

            return ToolResult.Ok($"Selected: {list.Count}", new Dictionary<string, object>
            {
                ["count"] = list.Count,
                ["objects"] = list
            });
        }
    }

    public sealed class SetSelectionTool : IAgentTool
    {
        public string Name => "set_selection";
        public string Description => "Selects a GameObject by id or name/path.";
        public string ParameterSchema => "{\"id\":string,\"name\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
            return ToolResult.Ok($"Selected '{go.name}'.", ObjectIdUtil.Describe(go));
        }
    }

    public sealed class FocusObjectTool : IAgentTool
    {
        public string Name => "focus_object";
        public string Description => "Frames/focuses a GameObject in the Scene view.";
        public string ParameterSchema => "{\"id\":string,\"name\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            Selection.activeGameObject = go;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
                SceneView.lastActiveSceneView.Repaint();
            }

            return ToolResult.Ok($"Focused '{go.name}'.", ObjectIdUtil.Describe(go));
        }
    }

    public sealed class SetTagTool : IAgentTool
    {
        public string Name => "set_tag";
        public string Description => "Sets a GameObject tag. Tag must already exist in Tag Manager.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"tag\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var tag = ToolArgs.Str(arguments, "tag");
            if (string.IsNullOrWhiteSpace(tag))
                return ToolResult.Fail("tag is required.");

            try
            {
                Undo.RecordObject(go, "UnityAgent Set Tag");
                go.tag = tag;
            }
            catch (UnityException ex)
            {
                return ToolResult.Fail($"Failed to set tag '{tag}': {ex.Message}");
            }

            return ToolResult.Ok($"Tag set to '{tag}'.", ObjectIdUtil.Describe(go));
        }
    }

    public sealed class SetLayerTool : IAgentTool
    {
        public string Name => "set_layer";
        public string Description => "Sets GameObject layer by name or index.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"layer\":string,\"layerIndex\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var layerIndex = ToolArgs.Int(arguments, "layerIndex", -1);
            var layerName = ToolArgs.Str(arguments, "layer");
            if (layerIndex < 0 && !string.IsNullOrEmpty(layerName))
                layerIndex = LayerMask.NameToLayer(layerName);

            if (layerIndex < 0 || layerIndex > 31)
                return ToolResult.Fail("Provide valid layer name or layerIndex (0-31).");

            Undo.RecordObject(go, "UnityAgent Set Layer");
            go.layer = layerIndex;
            return ToolResult.Ok($"Layer set to {layerIndex} ({LayerMask.LayerToName(layerIndex)}).", ObjectIdUtil.Describe(go));
        }
    }
}
