using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;

namespace UnityAgent.Editor.Tools.Capture
{
    public sealed class CaptureSceneViewTool : IAgentTool
    {
        public string Name => "capture_scene_view";
        public string Description => "Captures the active Scene view into Library/UnityAgent/Captures and returns the image path + size. Use to visually verify scene setup.";
        public string ParameterSchema => "{\"width\":number,\"height\":number,\"fileName\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null)
                return ToolResult.Fail("No active Scene view. Open a Scene view tab first.");

            var width = Mathf.Clamp(ToolArgs.Int(arguments, "width", 1280), 256, 3840);
            var height = Mathf.Clamp(ToolArgs.Int(arguments, "height", 720), 256, 2160);
            var fileName = ToolArgs.Str(arguments, "fileName", $"scene_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png");
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName += ".png";

            var dir = Path.Combine("Library", "UnityAgent", "Captures");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName).Replace('\\', '/');

            try
            {
                view.Repaint();
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                // Render SceneView camera into temporary RT
                var cam = view.camera;
                if (cam == null)
                    return ToolResult.Fail("Scene view camera is null.");

                var rt = RenderTexture.GetTemporary(width, height, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);

                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);

                return ToolResult.Ok($"Scene view captured: {path}", new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["width"] = width,
                    ["height"] = height,
                    ["source"] = "SceneView",
                    ["note"] = "Image saved on disk. Describe expected visuals in final report; multimodal vision can be enabled later."
                });
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Scene capture failed: {ex.Message}");
            }
        }
    }

    public sealed class CaptureGameViewTool : IAgentTool
    {
        public string Name => "capture_game_view";
        public string Description => "Captures the Game view (via Main Camera render) into Library/UnityAgent/Captures.";
        public string ParameterSchema => "{\"width\":number,\"height\":number,\"fileName\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var cam = Camera.main;
            if (cam == null)
                return ToolResult.Fail("No Main Camera found.");

            var width = Mathf.Clamp(ToolArgs.Int(arguments, "width", 1280), 256, 3840);
            var height = Mathf.Clamp(ToolArgs.Int(arguments, "height", 720), 256, 2160);
            var fileName = ToolArgs.Str(arguments, "fileName", $"game_{DateTime.UtcNow:yyyyMMdd_HHmmss}.png");
            if (!fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                fileName += ".png";

            var dir = Path.Combine("Library", "UnityAgent", "Captures");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, fileName).Replace('\\', '/');

            try
            {
                var rt = RenderTexture.GetTemporary(width, height, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = prev;

                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);

                File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);

                return ToolResult.Ok($"Game view captured: {path}", new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["width"] = width,
                    ["height"] = height,
                    ["source"] = "GameView/MainCamera"
                });
            }
            catch (Exception ex)
            {
                return ToolResult.Fail($"Game capture failed: {ex.Message}");
            }
        }
    }

    public sealed class ListCapturesTool : IAgentTool
    {
        public string Name => "list_captures";
        public string Description => "Lists recent screenshot captures under Library/UnityAgent/Captures.";
        public string ParameterSchema => "{\"maxResults\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var dir = Path.Combine("Library", "UnityAgent", "Captures");
            if (!Directory.Exists(dir))
                return ToolResult.Ok("No captures yet.", new Dictionary<string, object> { ["captures"] = new List<object>() });

            var max = ToolArgs.Int(arguments, "maxResults", 20);
            var files = Directory.GetFiles(dir, "*.png");
            Array.Sort(files, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            var list = new List<object>();
            for (var i = 0; i < files.Length && i < max; i++)
            {
                var fi = new FileInfo(files[i]);
                list.Add(new Dictionary<string, object>
                {
                    ["path"] = files[i].Replace('\\', '/'),
                    ["bytes"] = fi.Length,
                    ["modifiedUtc"] = fi.LastWriteTimeUtc.ToString("O")
                });
            }

            return ToolResult.Ok($"Captures: {list.Count}", new Dictionary<string, object> { ["captures"] = list });
        }
    }
}
