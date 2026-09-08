using System;
using System.Collections.Generic;
using System.Linq;
using UnityAgent.Editor.Logging;

namespace UnityAgent.Editor.Tools
{
    public sealed class ToolRegistry
    {
        readonly Dictionary<string, IAgentTool> _tools = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);

        public void Register(IAgentTool tool)
        {
            if (tool == null) throw new ArgumentNullException(nameof(tool));
            _tools[tool.Name] = tool;
            AgentLogger.Info($"Registered tool: {tool.Name}");
        }

        public bool TryGet(string name, out IAgentTool tool) => _tools.TryGetValue(name, out tool);

        public IReadOnlyList<IAgentTool> All => _tools.Values.OrderBy(t => t.Name).ToList();

        public List<Dictionary<string, object>> DescribeForLlm()
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var tool in All)
            {
                list.Add(new Dictionary<string, object>
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = tool.ParameterSchema,
                    ["risk"] = tool.RiskLevel.ToString()
                });
            }
            return list;
        }
    }
}
