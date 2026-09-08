using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.UI
{
    public sealed class CreateCanvasTool : IAgentTool
    {
        public string Name => "create_canvas";
        public string Description => "Creates a UGUI Canvas (Screen Space Overlay) with CanvasScaler and GraphicRaycaster. Ensures EventSystem exists.";
        public string ParameterSchema => "{\"name\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var name = ToolArgs.Str(arguments, "name", "Canvas");
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "UnityAgent Create Canvas");

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            go.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();
            Selection.activeGameObject = go;

            return ToolResult.Ok($"Canvas '{name}' created.", ObjectIdUtil.Describe(go));
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(es, "UnityAgent EventSystem");
            es.AddComponent<EventSystem>();
            // Input System UI module if present, else StandaloneInputModule
            var inputSystemModule = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputSystemModule != null)
                es.AddComponent(inputSystemModule);
            else
                es.AddComponent<StandaloneInputModule>();
        }
    }

    public sealed class CreateUiTextTool : IAgentTool
    {
        public string Name => "create_ui_text";
        public string Description => "Creates a UGUI Text under a Canvas (or creates Canvas if missing).";
        public string ParameterSchema => "{\"name\":string,\"text\":string,\"parentId\":string,\"fontSize\":number,\"color\":[r,g,b,a]}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var parent = ResolveUiParent(arguments, out var createdCanvas);
            var name = ToolArgs.Str(arguments, "name", "Text");
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "UnityAgent Create UI Text");
            go.transform.SetParent(parent.transform, false);

            var text = go.AddComponent<Text>();
            text.text = ToolArgs.Str(arguments, "text", "New Text");
            text.fontSize = ToolArgs.Int(arguments, "fontSize", 36);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = ReadColor(arguments, Color.white);
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf")
                        ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(400, 80);
            rt.anchoredPosition = Vector2.zero;

            Selection.activeGameObject = go;
            var data = ObjectIdUtil.Describe(go);
            data["createdCanvas"] = createdCanvas;
            data["parentId"] = ObjectIdUtil.GetId(parent);
            return ToolResult.Ok($"UI Text '{name}' created.", data);
        }

        public static GameObject ResolveUiParent(Dictionary<string, object> arguments, out bool createdCanvas)
        {
            createdCanvas = false;
            var parentId = ToolArgs.Str(arguments, "parentId");
            if (!string.IsNullOrEmpty(parentId) && ObjectIdUtil.TryResolve(parentId, out var parent))
                return parent;

            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
            if (canvas != null) return canvas.gameObject;

            var result = new CreateCanvasTool().Execute(new Dictionary<string, object> { ["name"] = "Canvas" });
            createdCanvas = true;
            if (result.Data is Dictionary<string, object> d && ObjectIdUtil.TryResolve(AgentJsonGetId(d), out var go))
                return go;

            // Fallback direct create
            CreateCanvasTool.EnsureEventSystem();
            var c = new GameObject("Canvas");
            c.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            c.AddComponent<CanvasScaler>();
            c.AddComponent<GraphicRaycaster>();
            return c;
        }

        static string AgentJsonGetId(Dictionary<string, object> d) =>
            d != null && d.TryGetValue("id", out var id) ? Convert.ToString(id) : null;

        public static Color ReadColor(Dictionary<string, object> arguments, Color fallback)
        {
            if (arguments == null || !arguments.ContainsKey("color")) return fallback;
            var v = ToolArgs.Vec3(arguments, "color", new Vector3(fallback.r, fallback.g, fallback.b));
            var a = fallback.a;
            if (arguments["color"] is List<object> list && list.Count >= 4)
                a = Convert.ToSingle(list[3]);
            return new Color(v.x, v.y, v.z, a);
        }
    }

    public sealed class CreateUiButtonTool : IAgentTool
    {
        public string Name => "create_ui_button";
        public string Description => "Creates a UGUI Button with child Text label under a Canvas.";
        public string ParameterSchema => "{\"name\":string,\"label\":string,\"parentId\":string,\"fontSize\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var parent = CreateUiTextTool.ResolveUiParent(arguments, out var createdCanvas);
            var name = ToolArgs.Str(arguments, "name", "Button");
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "UnityAgent Create UI Button");
            go.transform.SetParent(parent.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(220, 60);
            rt.anchoredPosition = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = new Color(0.18f, 0.45f, 0.85f, 1f);
            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var labelGo = new GameObject("Label");
            Undo.RegisterCreatedObjectUndo(labelGo, "UnityAgent Button Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;

            var text = labelGo.AddComponent<Text>();
            text.text = ToolArgs.Str(arguments, "label", "Button");
            text.fontSize = ToolArgs.Int(arguments, "fontSize", 28);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf")
                        ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            Selection.activeGameObject = go;
            var data = ObjectIdUtil.Describe(go);
            data["createdCanvas"] = createdCanvas;
            data["label"] = text.text;
            return ToolResult.Ok($"UI Button '{name}' created.", data);
        }
    }

    public sealed class CreateUiPanelTool : IAgentTool
    {
        public string Name => "create_ui_panel";
        public string Description => "Creates a simple UGUI panel (Image) under a Canvas.";
        public string ParameterSchema => "{\"name\":string,\"parentId\":string,\"color\":[r,g,b,a],\"width\":number,\"height\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var parent = CreateUiTextTool.ResolveUiParent(arguments, out var createdCanvas);
            var name = ToolArgs.Str(arguments, "name", "Panel");
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "UnityAgent Create UI Panel");
            go.transform.SetParent(parent.transform, false);

            var rt = go.AddComponent<RectTransform>();
            var w = ToolArgs.Float(arguments, "width", 600f);
            var h = ToolArgs.Float(arguments, "height", 400f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = CreateUiTextTool.ReadColor(arguments, new Color(0f, 0f, 0f, 0.55f));

            Selection.activeGameObject = go;
            var data = ObjectIdUtil.Describe(go);
            data["createdCanvas"] = createdCanvas;
            return ToolResult.Ok($"UI Panel '{name}' created.", data);
        }
    }

    public sealed class SetRectTransformTool : IAgentTool
    {
        public string Name => "set_rect_transform";
        public string Description => "Sets RectTransform anchoredPosition and/or sizeDelta for a UI element.";
        public string ParameterSchema => "{\"id\":string,\"anchoredPosition\":[x,y],\"sizeDelta\":[x,y]}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var rt = go.GetComponent<RectTransform>();
            if (rt == null)
                return ToolResult.Fail("Object has no RectTransform.");

            Undo.RecordObject(rt, "UnityAgent Set RectTransform");
            if (arguments.ContainsKey("anchoredPosition") && arguments["anchoredPosition"] is List<object> ap && ap.Count >= 2)
                rt.anchoredPosition = new Vector2(Convert.ToSingle(ap[0]), Convert.ToSingle(ap[1]));
            if (arguments.ContainsKey("sizeDelta") && arguments["sizeDelta"] is List<object> sd && sd.Count >= 2)
                rt.sizeDelta = new Vector2(Convert.ToSingle(sd[0]), Convert.ToSingle(sd[1]));

            return ToolResult.Ok("RectTransform updated.", ObjectIdUtil.Describe(go));
        }
    }
}
