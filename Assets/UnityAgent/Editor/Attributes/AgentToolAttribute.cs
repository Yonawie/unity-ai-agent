using System;

namespace UnityAgent.Editor.Attributes
{
    /// <summary>
    /// Mark an IAgentTool implementation for automatic discovery/registration.
    /// Tools in the UnityAgent.Editor assembly are also auto-discovered without this attribute.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class AgentToolAttribute : Attribute
    {
        public string Name { get; }
        public AgentToolAttribute(string name = null) => Name = name;
    }
}
