using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityAgent.Editor.Tools;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.LLM
{
    public static class ToolCallParser
    {
        public sealed class ParsedLlmTurn
        {
            public bool HasToolCall;
            public ToolCall ToolCall;
            public bool IsFinal;
            public string FinalMessage;
            public string AssistantText;
            public List<Dictionary<string, object>> PlanSteps;
            public string Goal;
            public string Raw;
        }

        public static ParsedLlmTurn Parse(string content)
        {
            var result = new ParsedLlmTurn
            {
                Raw = content,
                AssistantText = content
            };

            if (string.IsNullOrWhiteSpace(content))
            {
                result.IsFinal = true;
                result.FinalMessage = "Empty model response.";
                return result;
            }

            var json = ExtractJson(content);
            if (json == null)
            {
                // Plain text response — treat as final answer.
                result.IsFinal = true;
                result.FinalMessage = content.Trim();
                return result;
            }

            try
            {
                var obj = AgentJson.ParseObject(json);
                if (obj == null)
                {
                    result.IsFinal = true;
                    result.FinalMessage = content.Trim();
                    return result;
                }

                result.Goal = AgentJson.GetString(obj, "goal");
                var plan = AgentJson.GetArray(obj, "plan");
                if (plan != null)
                {
                    result.PlanSteps = new List<Dictionary<string, object>>();
                    foreach (var step in plan)
                    {
                        if (step is Dictionary<string, object> d)
                            result.PlanSteps.Add(d);
                        else if (step is string s)
                            result.PlanSteps.Add(new Dictionary<string, object> { ["title"] = s });
                    }
                }

                var type = AgentJson.GetString(obj, "type") ?? AgentJson.GetString(obj, "action");
                if (string.Equals(type, "final", StringComparison.OrdinalIgnoreCase) ||
                    obj.ContainsKey("final") ||
                    AgentJson.GetBool(obj, "done"))
                {
                    result.IsFinal = true;
                    result.FinalMessage = AgentJson.GetString(obj, "message")
                                          ?? AgentJson.GetString(obj, "final")
                                          ?? AgentJson.GetString(obj, "content")
                                          ?? content.Trim();
                    return result;
                }

                // tool call shapes:
                // { "tool": "name", "arguments": {...} }
                // { "type":"tool_call", "tool":"...", "arguments":{...} }
                // { "tool_call": { "name":"...", "arguments":{...} } }
                string toolName = null;
                Dictionary<string, object> args = null;

                if (obj.ContainsKey("tool"))
                {
                    toolName = AgentJson.GetString(obj, "tool");
                    args = AgentJson.GetObject(obj, "arguments") ?? AgentJson.GetObject(obj, "args") ?? new Dictionary<string, object>();
                }
                else if (obj.ContainsKey("tool_call"))
                {
                    var tc = AgentJson.GetObject(obj, "tool_call");
                    toolName = AgentJson.GetString(tc, "name") ?? AgentJson.GetString(tc, "tool");
                    args = AgentJson.GetObject(tc, "arguments") ?? AgentJson.GetObject(tc, "args") ?? new Dictionary<string, object>();
                }
                else if (string.Equals(type, "tool_call", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(type, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    toolName = AgentJson.GetString(obj, "name") ?? AgentJson.GetString(obj, "tool");
                    args = AgentJson.GetObject(obj, "arguments") ?? AgentJson.GetObject(obj, "args") ?? new Dictionary<string, object>();
                }

                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    result.HasToolCall = true;
                    result.ToolCall = new ToolCall
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Tool = toolName,
                        Arguments = args ?? new Dictionary<string, object>()
                    };
                    result.AssistantText = AgentJson.GetString(obj, "thought") ?? AgentJson.GetString(obj, "message") ?? $"Calling {toolName}";
                    return result;
                }

                // If JSON has message but no tool — final.
                result.IsFinal = true;
                result.FinalMessage = AgentJson.GetString(obj, "message") ?? content.Trim();
                return result;
            }
            catch (Exception)
            {
                result.IsFinal = true;
                result.FinalMessage = content.Trim();
                return result;
            }
        }

        static string ExtractJson(string content)
        {
            var trimmed = content.Trim();
            if (trimmed.StartsWith("```"))
            {
                var match = Regex.Match(trimmed, "```(?:json)?\\s*([\\s\\S]*?)```", RegexOptions.IgnoreCase);
                if (match.Success)
                    trimmed = match.Groups[1].Value.Trim();
            }

            if (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
                return trimmed;

            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start >= 0 && end > start)
                return trimmed.Substring(start, end - start + 1);

            return null;
        }
    }
}
