using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityAgent.Editor.Logging
{
    public static class AgentLogger
    {
        static readonly object Gate = new object();
        static string LogDirectory => Path.Combine("Logs", "UnityAgent");
        static string LogPath => Path.Combine(LogDirectory, $"agent-{DateTime.UtcNow:yyyyMMdd}.log");

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        public static void Tool(string tool, string arguments, string result, long durationMs)
        {
            Write("TOOL", $"tool={tool} durationMs={durationMs} args={Sanitize(arguments)} result={Sanitize(result)}");
        }

        public static void Llm(string phase, string payload)
        {
            Write("LLM", $"{phase}: {Sanitize(payload)}");
        }

        static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    if (!Directory.Exists(LogDirectory))
                        Directory.CreateDirectory(LogDirectory);
                    var line = $"{DateTime.UtcNow:O} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(LogPath, line, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityAgent] Logger failed: {ex.Message}");
            }
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sanitized = value
                .Replace("api_key", "***", StringComparison.OrdinalIgnoreCase)
                .Replace("apikey", "***", StringComparison.OrdinalIgnoreCase)
                .Replace("authorization", "***", StringComparison.OrdinalIgnoreCase);
            if (sanitized.Length > 4000)
                sanitized = sanitized.Substring(0, 4000) + "…";
            return sanitized;
        }
    }
}
