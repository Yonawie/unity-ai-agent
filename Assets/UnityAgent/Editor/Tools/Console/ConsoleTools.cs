using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;

namespace UnityAgent.Editor.Tools.Console
{
    public sealed class ReadConsoleTool : IAgentTool
    {
        public string Name => "read_console";
        public string Description => "Reads Unity Console entries (errors/warnings/logs). Prefer errors after compilation.";
        public string ParameterSchema => "{\"includeWarnings\":boolean,\"includeLogs\":boolean,\"maxEntries\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var includeWarnings = ToolArgs.Bool(arguments, "includeWarnings", true);
            var includeLogs = ToolArgs.Bool(arguments, "includeLogs", false);
            var maxEntries = ToolArgs.Int(arguments, "maxEntries", 50);

            var entries = ConsoleReader.GetEntries(includeWarnings, includeLogs, maxEntries);
            var errors = entries.Where(e => e.Type == "Error" || e.Type == "Assert" || e.Type == "Exception").ToList();
            return ToolResult.Ok($"Console entries: {entries.Count} (errors: {errors.Count}).", new Dictionary<string, object>
            {
                ["entries"] = entries.Select(e => e.ToDict()).ToList(),
                ["errorCount"] = errors.Count,
                ["hasErrors"] = errors.Count > 0
            });
        }
    }

    public sealed class ClearConsoleTool : IAgentTool
    {
        public string Name => "clear_console";
        public string Description => "Clears the Unity Console.";
        public string ParameterSchema => "{}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            ConsoleReader.Clear();
            return ToolResult.Ok("Console cleared.");
        }
    }

    public static class ConsoleReader
    {
        public sealed class Entry
        {
            public string Type;
            public string Message;
            public string File;
            public int Line;

            public Dictionary<string, object> ToDict() => new Dictionary<string, object>
            {
                ["type"] = Type,
                ["message"] = Message,
                ["file"] = File,
                ["line"] = Line
            };
        }

        public static List<Entry> GetEntries(bool includeWarnings, bool includeLogs, int maxEntries)
        {
            var result = new List<Entry>();
            try
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor.dll");
                var logEntryType = Type.GetType("UnityEditor.LogEntry,UnityEditor.dll");
                if (logEntriesType == null || logEntryType == null)
                    return result;

                var getCount = logEntriesType.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public);
                var getEntry = logEntriesType.GetMethod("GetEntryInternal", BindingFlags.Static | BindingFlags.Public);
                var startGetting = logEntriesType.GetMethod("StartGettingEntries", BindingFlags.Static | BindingFlags.Public);
                var endGetting = logEntriesType.GetMethod("EndGettingEntries", BindingFlags.Static | BindingFlags.Public);

                startGetting?.Invoke(null, null);
                var count = (int)(getCount?.Invoke(null, null) ?? 0);
                var entry = Activator.CreateInstance(logEntryType);

                for (var i = 0; i < count && result.Count < maxEntries; i++)
                {
                    getEntry?.Invoke(null, new[] { i, entry });
                    var mode = (int)(logEntryType.GetField("mode")?.GetValue(entry) ?? 0);
                    var message = logEntryType.GetField("message")?.GetValue(entry)?.ToString() ?? string.Empty;
                    var file = logEntryType.GetField("file")?.GetValue(entry)?.ToString() ?? string.Empty;
                    var line = (int)(logEntryType.GetField("line")?.GetValue(entry) ?? 0);

                    var type = Classify(mode, message);
                    if (type == "Warning" && !includeWarnings) continue;
                    if (type == "Log" && !includeLogs) continue;

                    result.Add(new Entry
                    {
                        Type = type,
                        Message = Truncate(message, 2000),
                        File = file,
                        Line = line
                    });
                }

                endGetting?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                result.Add(new Entry
                {
                    Type = "Error",
                    Message = $"Failed to read console via reflection: {ex.Message}",
                    File = "",
                    Line = 0
                });
            }

            return result;
        }

        public static void Clear()
        {
            try
            {
                var logEntriesType = Type.GetType("UnityEditor.LogEntries,UnityEditor.dll");
                var clear = logEntriesType?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
                clear?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] Clear console failed: {ex.Message}");
            }
        }

        static string Classify(int mode, string message)
        {
            // Unity LogEntry mode flags vary by version; message heuristics as backup.
            if (!string.IsNullOrEmpty(message))
            {
                if (message.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Error";
                if (message.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Warning";
            }

            // Common flags: Error=1, Assert=2, Log=4, Fatal=16, DontPreprocessCondition=32, AssetImportError=64, AssetImportWarning=128, ScriptingError=256, ScriptingWarning=512, ScriptingLog=1024, ScriptCompileError=2048, ScriptCompileWarning=4096
            if ((mode & (1 | 2 | 16 | 64 | 256 | 2048)) != 0) return "Error";
            if ((mode & (128 | 512 | 4096)) != 0) return "Warning";
            return "Log";
        }

        static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
            return value.Substring(0, max) + "…";
        }
    }
}
