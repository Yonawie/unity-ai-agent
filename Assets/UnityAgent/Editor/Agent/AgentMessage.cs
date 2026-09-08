using System;
using System.Collections.Generic;

namespace UnityAgent.Editor.Agent
{
    [Serializable]
    public class AgentMessage
    {
        public string Id;
        public string Role; // system | user | assistant | tool
        public string Content;
        public string ToolName;
        public string ToolCallId;
        public long TimestampUtcTicks;
        public bool IsError;

        public static AgentMessage User(string content) => new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = "user",
            Content = content ?? string.Empty,
            TimestampUtcTicks = DateTime.UtcNow.Ticks
        };

        public static AgentMessage Assistant(string content) => new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = "assistant",
            Content = content ?? string.Empty,
            TimestampUtcTicks = DateTime.UtcNow.Ticks
        };

        public static AgentMessage System(string content) => new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = "system",
            Content = content ?? string.Empty,
            TimestampUtcTicks = DateTime.UtcNow.Ticks
        };

        public static AgentMessage Tool(string toolName, string toolCallId, string content, bool isError = false) => new AgentMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Role = "tool",
            ToolName = toolName,
            ToolCallId = toolCallId,
            Content = content ?? string.Empty,
            IsError = isError,
            TimestampUtcTicks = DateTime.UtcNow.Ticks
        };
    }

    [Serializable]
    public class AgentSession
    {
        public string SessionId = Guid.NewGuid().ToString("N");
        public string UserRequest;
        public AgentMode Mode = AgentMode.Agent;
        public AgentStatus Status = AgentStatus.Idle;
        public string StatusDetail;
        public List<AgentMessage> Messages = new List<AgentMessage>();
        public AgentPlan Plan = new AgentPlan();
        public List<string> ActionLog = new List<string>();
        public string LastError;
        public string PendingAction;
        public int CurrentStep;
        public int FixAttempts;
        public bool ResumeAfterReload;
        public long UpdatedUtcTicks = DateTime.UtcNow.Ticks;
    }
}
