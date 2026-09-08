using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Logging;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Persistence
{
    public static class SessionPersistence
    {
        const string SessionStateKey = "UnityAgent.ActiveSession";
        static string SessionPath => Path.Combine("Library", "UnityAgent", "session.json");

        public static void Save(Agent.AgentSession session)
        {
            if (session == null) return;
            try
            {
                session.UpdatedUtcTicks = DateTime.UtcNow.Ticks;
                var json = AgentJson.Serialize(SessionToDict(session));
                var dir = Path.GetDirectoryName(SessionPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(SessionPath, json);
                SessionState.SetString(SessionStateKey, json);
            }
            catch (Exception ex)
            {
                AgentLogger.Warn($"Failed to persist session: {ex.Message}");
            }
        }

        public static Agent.AgentSession Load()
        {
            try
            {
                var json = SessionState.GetString(SessionStateKey, string.Empty);
                if (string.IsNullOrEmpty(json) && File.Exists(SessionPath))
                    json = File.ReadAllText(SessionPath);

                if (string.IsNullOrEmpty(json)) return null;
                var obj = AgentJson.ParseObject(json);
                return obj == null ? null : SessionFromDict(obj);
            }
            catch (Exception ex)
            {
                AgentLogger.Warn($"Failed to load session: {ex.Message}");
                return null;
            }
        }

        public static void Clear()
        {
            SessionState.EraseString(SessionStateKey);
            try
            {
                if (File.Exists(SessionPath)) File.Delete(SessionPath);
            }
            catch { /* ignore */ }
        }

        static Dictionary<string, object> SessionToDict(Agent.AgentSession s)
        {
            var messages = new List<object>();
            foreach (var m in s.Messages)
            {
                messages.Add(new Dictionary<string, object>
                {
                    ["Id"] = m.Id,
                    ["Role"] = m.Role,
                    ["Content"] = m.Content,
                    ["ToolName"] = m.ToolName,
                    ["ToolCallId"] = m.ToolCallId,
                    ["TimestampUtcTicks"] = m.TimestampUtcTicks,
                    ["IsError"] = m.IsError
                });
            }

            var steps = new List<object>();
            foreach (var step in s.Plan.Steps)
            {
                steps.Add(new Dictionary<string, object>
                {
                    ["Id"] = step.Id,
                    ["Title"] = step.Title,
                    ["Detail"] = step.Detail,
                    ["Status"] = step.Status.ToString()
                });
            }

            return new Dictionary<string, object>
            {
                ["SessionId"] = s.SessionId,
                ["UserRequest"] = s.UserRequest,
                ["Mode"] = s.Mode.ToString(),
                ["Status"] = s.Status.ToString(),
                ["StatusDetail"] = s.StatusDetail,
                ["Messages"] = messages,
                ["PlanGoal"] = s.Plan.Goal,
                ["PlanSteps"] = steps,
                ["ActionLog"] = s.ActionLog,
                ["LastError"] = s.LastError,
                ["PendingAction"] = s.PendingAction,
                ["CurrentStep"] = s.CurrentStep,
                ["FixAttempts"] = s.FixAttempts,
                ["ResumeAfterReload"] = s.ResumeAfterReload,
                ["UpdatedUtcTicks"] = s.UpdatedUtcTicks
            };
        }

        static Agent.AgentSession SessionFromDict(Dictionary<string, object> o)
        {
            var s = new Agent.AgentSession
            {
                SessionId = AgentJson.GetString(o, "SessionId", Guid.NewGuid().ToString("N")),
                UserRequest = AgentJson.GetString(o, "UserRequest"),
                Mode = AgentJson.GetEnum(o, "Mode", Agent.AgentMode.Agent),
                Status = AgentJson.GetEnum(o, "Status", Agent.AgentStatus.Idle),
                StatusDetail = AgentJson.GetString(o, "StatusDetail"),
                LastError = AgentJson.GetString(o, "LastError"),
                PendingAction = AgentJson.GetString(o, "PendingAction"),
                CurrentStep = AgentJson.GetInt(o, "CurrentStep"),
                FixAttempts = AgentJson.GetInt(o, "FixAttempts"),
                ResumeAfterReload = AgentJson.GetBool(o, "ResumeAfterReload"),
                UpdatedUtcTicks = o.TryGetValue("UpdatedUtcTicks", out var t) ? Convert.ToInt64(t) : DateTime.UtcNow.Ticks
            };

            var messages = AgentJson.GetArray(o, "Messages");
            if (messages != null)
            {
                foreach (var item in messages)
                {
                    if (item is not Dictionary<string, object> m) continue;
                    s.Messages.Add(new Agent.AgentMessage
                    {
                        Id = AgentJson.GetString(m, "Id"),
                        Role = AgentJson.GetString(m, "Role"),
                        Content = AgentJson.GetString(m, "Content"),
                        ToolName = AgentJson.GetString(m, "ToolName"),
                        ToolCallId = AgentJson.GetString(m, "ToolCallId"),
                        TimestampUtcTicks = m.TryGetValue("TimestampUtcTicks", out var ticks) ? Convert.ToInt64(ticks) : 0,
                        IsError = AgentJson.GetBool(m, "IsError")
                    });
                }
            }

            s.Plan.Goal = AgentJson.GetString(o, "PlanGoal");
            var steps = AgentJson.GetArray(o, "PlanSteps");
            if (steps != null)
            {
                foreach (var item in steps)
                {
                    if (item is not Dictionary<string, object> st) continue;
                    s.Plan.Steps.Add(new Agent.AgentPlanStep
                    {
                        Id = AgentJson.GetString(st, "Id", Guid.NewGuid().ToString("N")),
                        Title = AgentJson.GetString(st, "Title"),
                        Detail = AgentJson.GetString(st, "Detail"),
                        Status = AgentJson.GetEnum(st, "Status", Agent.PlanStepStatus.Pending)
                    });
                }
            }

            var actions = AgentJson.GetArray(o, "ActionLog");
            if (actions != null)
            {
                foreach (var a in actions)
                    s.ActionLog.Add(Convert.ToString(a));
            }

            return s;
        }
    }
}
