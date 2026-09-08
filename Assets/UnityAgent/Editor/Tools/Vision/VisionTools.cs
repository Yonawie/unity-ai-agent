using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Attributes;
using UnityAgent.Editor.Tools.Capture;

namespace UnityAgent.Editor.Tools.Vision
{
    /// <summary>
    /// Queues image paths that the next LLM request should include (multimodal).
    /// </summary>
    public static class VisionQueue
    {
        static readonly List<string> Paths = new List<string>();

        public static void Enqueue(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = path.Replace('\\', '/');
            if (!File.Exists(path)) return;
            if (!Paths.Contains(path)) Paths.Add(path);
        }

        public static List<string> Drain()
        {
            var copy = new List<string>(Paths);
            Paths.Clear();
            return copy;
        }

        public static IReadOnlyList<string> Peek() => Paths;
    }

    [AgentTool]
    public sealed class AnalyzeCaptureTool : IAgentTool
    {
        public string Name => "analyze_capture";
        public string Description => "Queues a capture image for multimodal vision on the next LLM step. Provide path, or set captureScene/captureGame true to take a fresh screenshot first. Requires a vision-capable model.";
        public string ParameterSchema => "{\"path\":string,\"captureScene\":boolean,\"captureGame\":boolean,\"prompt\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = ToolArgs.Str(arguments, "path");
            if (ToolArgs.Bool(arguments, "captureScene", false))
            {
                var cap = new CaptureSceneViewTool().Execute(new Dictionary<string, object>());
                if (!cap.Success) return cap;
                if (cap.Data is Dictionary<string, object> d) path = Convert.ToString(d["path"]);
            }
            else if (ToolArgs.Bool(arguments, "captureGame", false))
            {
                var cap = new CaptureGameViewTool().Execute(new Dictionary<string, object>());
                if (!cap.Success) return cap;
                if (cap.Data is Dictionary<string, object> d) path = Convert.ToString(d["path"]);
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return ToolResult.Fail("Capture path not found. Provide path or captureScene/captureGame.");

            VisionQueue.Enqueue(path);
            var prompt = ToolArgs.Str(arguments, "prompt", "Describe this Unity editor capture and note any obvious issues.");
            return ToolResult.Ok("Capture queued for vision on the next reasoning step.", new Dictionary<string, object>
            {
                ["path"] = path,
                ["prompt"] = prompt,
                ["queuedCount"] = VisionQueue.Peek().Count,
                ["bytes"] = new FileInfo(path).Length
            });
        }
    }
}
