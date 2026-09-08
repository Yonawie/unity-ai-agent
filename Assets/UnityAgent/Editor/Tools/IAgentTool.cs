using System.Collections.Generic;
using UnityAgent.Editor.Agent;

namespace UnityAgent.Editor.Tools
{
    public interface IAgentTool
    {
        string Name { get; }
        string Description { get; }
        string ParameterSchema { get; }
        RiskLevel RiskLevel { get; }
        ToolResult Execute(Dictionary<string, object> arguments);
    }

    public sealed class ToolResult
    {
        public bool Success;
        public string Message;
        public object Data;
        public string Error;

        public static ToolResult Ok(string message, object data = null) => new ToolResult
        {
            Success = true,
            Message = message,
            Data = data
        };

        public static ToolResult Fail(string error, string message = null) => new ToolResult
        {
            Success = false,
            Message = message ?? error,
            Error = error
        };

        public Dictionary<string, object> ToDictionary()
        {
            return new Dictionary<string, object>
            {
                ["success"] = Success,
                ["message"] = Message,
                ["data"] = Data,
                ["error"] = Error
            };
        }
    }

    public sealed class ToolCall
    {
        public string Tool;
        public Dictionary<string, object> Arguments = new Dictionary<string, object>();
        public string Id;
    }
}
