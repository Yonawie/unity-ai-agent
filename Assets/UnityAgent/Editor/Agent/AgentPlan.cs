using System;
using System.Collections.Generic;

namespace UnityAgent.Editor.Agent
{
    [Serializable]
    public class AgentPlanStep
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title;
        public string Detail;
        public PlanStepStatus Status = PlanStepStatus.Pending;
    }

    [Serializable]
    public class AgentPlan
    {
        public string Goal;
        public List<AgentPlanStep> Steps = new List<AgentPlanStep>();

        public void Clear()
        {
            Goal = null;
            Steps.Clear();
        }

        public AgentPlanStep AddStep(string title, string detail = null)
        {
            var step = new AgentPlanStep { Title = title, Detail = detail };
            Steps.Add(step);
            return step;
        }

        public void SetStepStatus(int index, PlanStepStatus status)
        {
            if (index < 0 || index >= Steps.Count) return;
            Steps[index].Status = status;
        }
    }
}
