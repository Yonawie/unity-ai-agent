using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Attributes;
using UnityAgent.Editor.Tools.Capture;
using UnityAgent.Editor.Tools.Console;

namespace UnityAgent.Editor.Tools.Testing
{
    [AgentTool]
    public sealed class RunPlayModeSmokeTool : IAgentTool
    {
        public string Name => "run_play_mode_smoke";
        public string Description => "Enters Play Mode briefly, waits, captures Game view, exits Play Mode, and returns console errors + screenshot path. Editor-blocking wait uses EditorApplication.update polling via async helper.";
        public string ParameterSchema => "{\"seconds\":number,\"capture\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (EditorApplication.isCompiling)
                return ToolResult.Fail("Cannot run smoke test while compiling.");
            if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying)
                return ToolResult.Fail("Play Mode transition already in progress.");

            var seconds = Mathf.Clamp(ToolArgs.Float(arguments, "seconds", 2f), 0.5f, 15f);
            var capture = ToolArgs.Bool(arguments, "capture", true);

            // Kick async workflow; return immediate ack with tracking id.
            var id = Guid.NewGuid().ToString("N");
            PlayModeSmokeRunner.Start(id, seconds, capture);
            return ToolResult.Ok("Play Mode smoke started. Call get_play_mode_smoke_result next.", new Dictionary<string, object>
            {
                ["smokeId"] = id,
                ["seconds"] = seconds,
                ["status"] = "started"
            });
        }
    }

    [AgentTool]
    public sealed class GetPlayModeSmokeResultTool : IAgentTool
    {
        public string Name => "get_play_mode_smoke_result";
        public string Description => "Gets status/result of the latest Play Mode smoke test.";
        public string ParameterSchema => "{\"smokeId\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var id = ToolArgs.Str(arguments, "smokeId");
            var result = PlayModeSmokeRunner.Get(id);
            if (result == null)
                return ToolResult.Fail("No smoke result found. Start with run_play_mode_smoke.");
            return ToolResult.Ok(result.Status, result.ToDict());
        }
    }

    public static class PlayModeSmokeRunner
    {
        public sealed class SmokeState
        {
            public string Id;
            public string Status; // started|playing|capturing|done|failed
            public string Error;
            public string CapturePath;
            public int ErrorCount;
            public float Seconds;
            public double StartTime;

            public Dictionary<string, object> ToDict() => new Dictionary<string, object>
            {
                ["smokeId"] = Id,
                ["status"] = Status,
                ["error"] = Error,
                ["capturePath"] = CapturePath,
                ["errorCount"] = ErrorCount,
                ["seconds"] = Seconds
            };
        }

        static SmokeState _current;
        static bool _hooked;

        public static void Start(string id, float seconds, bool capture)
        {
            _current = new SmokeState
            {
                Id = id,
                Status = "started",
                Seconds = seconds,
                StartTime = EditorApplication.timeSinceStartup
            };
            EnsureHook();
            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
            _current.Status = "playing";
            _pendingCapture = capture;
            _waitUntil = EditorApplication.timeSinceStartup + seconds;
        }

        public static SmokeState Get(string id)
        {
            if (_current == null) return null;
            if (!string.IsNullOrEmpty(id) && _current.Id != id) return _current; // still return latest
            return _current;
        }

        static bool _pendingCapture;
        static double _waitUntil;

        static void EnsureHook()
        {
            if (_hooked) return;
            _hooked = true;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (_current == null) return;
            if (_current.Status == "done" || _current.Status == "failed") return;

            if (!EditorApplication.isPlaying)
            {
                if (_current.Status == "playing" || _current.Status == "capturing")
                {
                    // Exited early
                    FinishConsole();
                    _current.Status = string.IsNullOrEmpty(_current.Error) ? "done" : "failed";
                }
                return;
            }

            if (EditorApplication.timeSinceStartup < _waitUntil)
                return;

            try
            {
                if (_pendingCapture && _current.Status == "playing")
                {
                    _current.Status = "capturing";
                    var capture = new CaptureGameViewTool().Execute(new Dictionary<string, object>
                    {
                        ["fileName"] = $"smoke_{_current.Id}.png"
                    });
                    if (capture.Success && capture.Data is Dictionary<string, object> data &&
                        data.TryGetValue("path", out var p))
                        _current.CapturePath = Convert.ToString(p);
                    _pendingCapture = false;
                }

                FinishConsole();
                EditorApplication.isPlaying = false;
                _current.Status = _current.ErrorCount > 0 ? "failed" : "done";
            }
            catch (Exception ex)
            {
                _current.Status = "failed";
                _current.Error = ex.Message;
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
            }
        }

        static void FinishConsole()
        {
            var console = new ReadConsoleTool().Execute(new Dictionary<string, object>
            {
                ["includeWarnings"] = false,
                ["includeLogs"] = false,
                ["maxEntries"] = 30
            });
            if (console.Data is Dictionary<string, object> d)
                _current.ErrorCount = Util.AgentJson.GetInt(d, "errorCount");
        }
    }
}
