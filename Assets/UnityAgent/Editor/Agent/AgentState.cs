namespace UnityAgent.Editor.Agent
{
    public enum AgentMode
    {
        Ask,
        Plan,
        Agent
    }

    public enum AgentStatus
    {
        Idle,
        Thinking,
        Planning,
        Executing,
        WaitingForUnity,
        Compiling,
        Testing,
        Fixing,
        Completed,
        Failed,
        Cancelled
    }

    public enum PlanStepStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Skipped
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High
    }
}
