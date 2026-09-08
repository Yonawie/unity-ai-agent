using System.Collections.Generic;
using UnityEditor;
using UnityAgent.Editor.Agent;

namespace UnityAgent.Editor.Tools.EditorTools
{
    public sealed class EnterPlayModeTool : IAgentTool
    {
        public string Name => "enter_play_mode";
        public string Description => "Enters Play Mode. Use sparingly; prefer checking console/compilation first.";
        public string ParameterSchema => "{}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (EditorApplication.isPlaying)
                return ToolResult.Ok("Already in Play Mode.");
            if (EditorApplication.isCompiling)
                return ToolResult.Fail("Cannot enter Play Mode while compiling.");

            EditorApplication.isPlaying = true;
            return ToolResult.Ok("Entering Play Mode.");
        }
    }

    public sealed class ExitPlayModeTool : IAgentTool
    {
        public string Name => "exit_play_mode";
        public string Description => "Exits Play Mode.";
        public string ParameterSchema => "{}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!EditorApplication.isPlaying)
                return ToolResult.Ok("Not in Play Mode.");

            EditorApplication.isPlaying = false;
            return ToolResult.Ok("Exiting Play Mode.");
        }
    }
}
