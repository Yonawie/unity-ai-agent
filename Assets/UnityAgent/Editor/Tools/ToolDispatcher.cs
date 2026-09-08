using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityAgent.Editor.Logging;
using UnityAgent.Editor.Safety;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools
{
    public sealed class ToolDispatcher
    {
        readonly ToolRegistry _registry;
        readonly ActionValidator _validator;
        readonly PermissionManager _permissions;
        readonly ChangeTracker _changes;

        public ToolDispatcher(
            ToolRegistry registry,
            ActionValidator validator,
            PermissionManager permissions,
            ChangeTracker changes)
        {
            _registry = registry;
            _validator = validator;
            _permissions = permissions;
            _changes = changes;
        }

        public ToolResult Dispatch(ToolCall call)
        {
            if (call == null || string.IsNullOrWhiteSpace(call.Tool))
                return ToolResult.Fail("Missing tool name.");

            if (!_registry.TryGet(call.Tool, out var tool))
                return ToolResult.Fail($"Unknown tool: {call.Tool}");

            var args = call.Arguments ?? new Dictionary<string, object>();
            var validation = _validator.Validate(tool, args);
            if (!validation.Allowed)
                return ToolResult.Fail(validation.Reason);

            if (!_permissions.CanExecute(tool, out var permissionError))
                return ToolResult.Fail(permissionError);

            if (!_permissions.ConfirmIfNeeded(tool, args, out var confirmError))
                return ToolResult.Fail(confirmError);

            var sw = Stopwatch.StartNew();
            try
            {
                var result = tool.Execute(args);
                sw.Stop();
                AgentLogger.Tool(tool.Name, AgentJson.Serialize(args), AgentJson.Serialize(result.ToDictionary()), sw.ElapsedMilliseconds);
                if (result.Success)
                    _changes.RecordTool(tool.Name, result);
                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                AgentLogger.Error($"Tool {tool.Name} threw: {ex}");
                return ToolResult.Fail(ex.Message);
            }
        }
    }
}
