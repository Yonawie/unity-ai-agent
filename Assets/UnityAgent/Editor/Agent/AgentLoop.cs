using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityAgent.Editor.Context;
using UnityAgent.Editor.LLM;
using UnityAgent.Editor.Logging;
using UnityAgent.Editor.Persistence;
using UnityAgent.Editor.Safety;
using UnityAgent.Editor.Settings;
using UnityAgent.Editor.Tools;
using UnityAgent.Editor.Tools.Vision;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Agent
{
    public sealed class AgentLoop
    {
        readonly ToolRegistry _registry;
        readonly ToolDispatcher _dispatcher;
        readonly ContextManager _context;
        readonly ChangeTracker _changes;
        readonly Func<ILLMProvider> _providerFactory;

        public AgentLoop(
            ToolRegistry registry,
            ToolDispatcher dispatcher,
            ContextManager context,
            ChangeTracker changes,
            Func<ILLMProvider> providerFactory = null)
        {
            _registry = registry;
            _dispatcher = dispatcher;
            _context = context;
            _changes = changes;
            _providerFactory = providerFactory ?? LLMProviderFactory.CreateFromSettings;
        }

        public async Task RunAsync(
            AgentSession session,
            CancellationToken token,
            Action onChanged = null)
        {
            CompilationMonitor.EnsureHooks();
            var settings = AgentSettings.Current;
            var provider = _providerFactory();
            var maxSteps = Math.Max(1, settings.MaxAgentSteps);
            var maxFixes = Math.Max(1, settings.MaxFixAttempts);

            _changes.BeginTask(session.UserRequest ?? "task");
            SetStatus(session, AgentStatus.Thinking, "Gathering context", onChanged);

            var systemPrompt = _context.BuildSystemPrompt(session.Mode, _registry);
            var llmMessages = BuildInitialMessages(session, systemPrompt);

            for (var step = session.CurrentStep; step < maxSteps; step++)
            {
                token.ThrowIfCancellationRequested();
                session.CurrentStep = step;
                SessionPersistence.Save(session);
                onChanged?.Invoke();

                if (EditorApplication.isCompiling)
                {
                    SetStatus(session, AgentStatus.Compiling, "Waiting for Unity compilation", onChanged);
                    session.ResumeAfterReload = true;
                    SessionPersistence.Save(session);
                    await CompilationMonitor.WaitForCompilationAsync(token);
                    session.ResumeAfterReload = false;
                }

                SetStatus(session, AgentStatus.Thinking, $"LLM step {step + 1}/{maxSteps}", onChanged);

                var request = new LLMRequest
                {
                    Model = settings.Model,
                    Temperature = settings.Temperature,
                    JsonMode = true,
                    SystemPrompt = systemPrompt,
                    Messages = llmMessages.Select(m => new LLMMessage(m.Role, m.Content)).ToList(),
                    OnPartial = chunk =>
                    {
                        session.StreamingText = (session.StreamingText ?? string.Empty) + chunk;
                        onChanged?.Invoke();
                    }
                };

                // Attach queued vision captures for multimodal models.
                if (settings.EnableVision)
                {
                    var images = VisionQueue.Drain();
                    if (images.Count > 0)
                    {
                        request.ImagePaths.AddRange(images);
                        request.Messages.Add(new LLMMessage("user",
                            "Vision captures attached (" + images.Count +
                            "). Describe what you see and continue the task using tools if needed.\nPaths:\n- " +
                            string.Join("\n- ", images)));
                    }
                }

                session.StreamingText = string.Empty;

                // Inject fresh minimal context as a hidden user note each few steps.
                if (step == 0 || step % 4 == 0)
                {
                    request.Messages.Insert(0, new LLMMessage("user",
                        "Current Unity context (JSON):\n" + _context.BuildMinimalContextJson()));
                }

                AgentLogger.Info($"Agent step {step + 1}: sending LLM request");
                session.Messages.Add(AgentMessage.Assistant("… waiting for model …"));
                onChanged?.Invoke();

                var response = await provider.SendAsync(request, token);
                session.StreamingText = null;

                // Replace the waiting placeholder with real outcome.
                if (session.Messages.Count > 0 &&
                    session.Messages[session.Messages.Count - 1].Role == "assistant" &&
                    session.Messages[session.Messages.Count - 1].Content == "… waiting for model …")
                {
                    session.Messages.RemoveAt(session.Messages.Count - 1);
                }

                if (!response.Success)
                {
                    SetStatus(session, AgentStatus.Failed, response.Error, onChanged);
                    session.LastError = response.Error;
                    session.Messages.Add(AgentMessage.Assistant("Error: " + response.Error));
                    SessionPersistence.Save(session);
                    onChanged?.Invoke();
                    return;
                }

                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    var emptyError = "Model returned an empty response. Check LM Studio server/model, set Provider=LMStudio, Base URL=http://127.0.0.1:1234/v1, and disable Enable LLM Streaming.";
                    SetStatus(session, AgentStatus.Failed, emptyError, onChanged);
                    session.LastError = emptyError;
                    session.Messages.Add(AgentMessage.Assistant("Error: " + emptyError));
                    SessionPersistence.Save(session);
                    onChanged?.Invoke();
                    return;
                }

                var parsed = ToolCallParser.Parse(response.Content);
                ApplyPlan(session, parsed, onChanged);

                if (parsed.HasToolCall && session.Mode != AgentMode.Agent && IsMutatingTool(parsed.ToolCall.Tool))
                {
                    session.Messages.Add(AgentMessage.Assistant(
                        $"Blocked mutating tool '{parsed.ToolCall.Tool}' in {session.Mode} mode. Switch to AGENT to execute changes."));
                    SetStatus(session, AgentStatus.Completed, "Blocked in non-agent mode", onChanged);
                    SessionPersistence.Save(session);
                    return;
                }

                if (session.Mode == AgentMode.Ask && !parsed.HasToolCall)
                {
                    var askText = parsed.FinalMessage ?? parsed.AssistantText ?? response.Content;
                    session.Messages.Add(AgentMessage.Assistant(askText));
                    SetStatus(session, AgentStatus.Completed, "Ask completed", onChanged);
                    SessionPersistence.Save(session);
                    return;
                }

                if (session.Mode == AgentMode.Plan && !parsed.HasToolCall)
                {
                    var planText = BuildPlanText(session, parsed.FinalMessage ?? parsed.AssistantText);
                    session.Messages.Add(AgentMessage.Assistant(planText));
                    SetStatus(session, AgentStatus.Completed, "Plan ready", onChanged);
                    SessionPersistence.Save(session);
                    return;
                }

                if (parsed.HasToolCall)
                {
                    MarkPlanRunning(session, parsed.ToolCall.Tool, onChanged);
                    SetStatus(session, AgentStatus.Executing, parsed.ToolCall.Tool, onChanged);
                    session.ActionLog.Add($"→ {parsed.ToolCall.Tool} {AgentJson.Serialize(parsed.ToolCall.Arguments)}");
                    session.Messages.Add(AgentMessage.Assistant(parsed.AssistantText ?? $"Calling {parsed.ToolCall.Tool}"));
                    onChanged?.Invoke();

                    var result = _dispatcher.Dispatch(parsed.ToolCall);
                    var resultJson = AgentJson.Serialize(result.ToDictionary());
                    session.Messages.Add(AgentMessage.Tool(parsed.ToolCall.Tool, parsed.ToolCall.Id, resultJson, !result.Success));
                    session.ActionLog.Add(result.Success
                        ? $"✓ {parsed.ToolCall.Tool}: {result.Message}"
                        : $"✗ {parsed.ToolCall.Tool}: {result.Error}");
                    MarkPlanStepResult(session, parsed.ToolCall.Tool, result.Success, onChanged);
                    llmMessages.Add(new AgentMessage { Role = "assistant", Content = response.Content });
                    llmMessages.Add(AgentMessage.Tool(parsed.ToolCall.Tool, parsed.ToolCall.Id, resultJson, !result.Success));
                    onChanged?.Invoke();

                    if (NeedsCompileWait(parsed.ToolCall.Tool, result))
                    {
                        SetStatus(session, AgentStatus.Compiling, "Script changed — waiting for compile", onChanged);
                        session.PendingAction = "post_compile_console_check";
                        session.ResumeAfterReload = true;
                        SessionPersistence.Save(session);

                        await CompilationMonitor.WaitForCompilationAsync(token);
                        session.ResumeAfterReload = false;

                        SetStatus(session, AgentStatus.Testing, "Reading console after compile", onChanged);
                        var consoleCall = new ToolCall
                        {
                            Tool = "read_console",
                            Arguments = new Dictionary<string, object>
                            {
                                ["includeWarnings"] = true,
                                ["includeLogs"] = false,
                                ["maxEntries"] = 40
                            }
                        };
                        var consoleResult = _dispatcher.Dispatch(consoleCall);
                        var consoleJson = AgentJson.Serialize(consoleResult.ToDictionary());
                        session.Messages.Add(AgentMessage.Tool("read_console", Guid.NewGuid().ToString("N"), consoleJson));
                        llmMessages.Add(AgentMessage.User(
                            "Compilation finished. Console result JSON:\n" + consoleJson +
                            "\nIf hasErrors=true, fix with read_script/patch_script. Otherwise continue the task."));

                        if (consoleResult.Data is Dictionary<string, object> data &&
                            AgentJson.GetBool(data, "hasErrors"))
                        {
                            session.FixAttempts++;
                            if (session.FixAttempts > maxFixes)
                            {
                                SetStatus(session, AgentStatus.Failed, "Max fix attempts exceeded", onChanged);
                                session.LastError = "Compilation errors remain after MaxFixAttempts.";
                                session.Messages.Add(AgentMessage.Assistant(session.LastError + "\n" + consoleJson));
                                SessionPersistence.Save(session);
                                return;
                            }
                            SetStatus(session, AgentStatus.Fixing, $"Fix attempt {session.FixAttempts}/{maxFixes}", onChanged);
                        }

                        SessionPersistence.Save(session);
                    }

                    continue;
                }

                // Final response
                var finalText = parsed.FinalMessage ?? parsed.AssistantText ?? response.Content;
                if (!string.IsNullOrWhiteSpace(_changes.Summarize()) &&
                    _changes.Summarize() != "No tracked changes.")
                {
                    finalText = finalText.TrimEnd() + "\n\n" + _changes.Summarize();
                }

                session.Messages.Add(AgentMessage.Assistant(finalText));
                CompleteRemainingPlanSteps(session);
                SetStatus(session, AgentStatus.Completed, "Done", onChanged);
                SessionPersistence.Save(session);
                return;
            }

            SetStatus(session, AgentStatus.Failed, "Max agent steps reached", onChanged);
            session.LastError = $"Stopped after {maxSteps} steps.";
            session.Messages.Add(AgentMessage.Assistant(session.LastError));
            SessionPersistence.Save(session);
        }

        static List<AgentMessage> BuildInitialMessages(AgentSession session, string systemPrompt)
        {
            var list = new List<AgentMessage>();
            // Keep conversation history except system.
            foreach (var m in session.Messages)
            {
                if (m.Role == "system") continue;
                list.Add(m);
            }

            if (list.Count == 0 || list.All(m => m.Role != "user"))
                list.Insert(0, AgentMessage.User(session.UserRequest));

            return list;
        }

        static void ApplyPlan(AgentSession session, ToolCallParser.ParsedLlmTurn parsed, Action onChanged)
        {
            if (!string.IsNullOrEmpty(parsed.Goal))
                session.Plan.Goal = parsed.Goal;

            if (parsed.PlanSteps == null || parsed.PlanSteps.Count == 0) return;

            if (session.Plan.Steps.Count == 0)
            {
                SetStatus(session, AgentStatus.Planning, "Building plan", onChanged);
                foreach (var step in parsed.PlanSteps)
                {
                    var title = AgentJson.GetString(step, "title") ?? AgentJson.GetString(step, "name") ?? "Step";
                    var detail = AgentJson.GetString(step, "detail");
                    session.Plan.AddStep(title, detail);
                }
            }
            else
            {
                // Allow plan extension when model adds more steps mid-run.
                foreach (var step in parsed.PlanSteps)
                {
                    var title = AgentJson.GetString(step, "title") ?? AgentJson.GetString(step, "name");
                    if (string.IsNullOrEmpty(title)) continue;
                    if (session.Plan.Steps.Exists(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase)))
                        continue;
                    session.Plan.AddStep(title, AgentJson.GetString(step, "detail"));
                }
            }
        }

        static void MarkPlanRunning(AgentSession session, string toolName, Action onChanged)
        {
            if (session.Plan?.Steps == null || session.Plan.Steps.Count == 0) return;
            foreach (var step in session.Plan.Steps)
            {
                if (step.Status == PlanStepStatus.Running)
                    step.Status = PlanStepStatus.Completed;
            }

            var match = FindBestPlanStep(session, toolName);
            if (match != null && match.Status == PlanStepStatus.Pending)
            {
                match.Status = PlanStepStatus.Running;
                match.Detail = toolName;
            }
            onChanged?.Invoke();
        }

        static void MarkPlanStepResult(AgentSession session, string toolName, bool success, Action onChanged)
        {
            var match = FindBestPlanStep(session, toolName);
            if (match == null)
            {
                // Fall back to first running/pending step.
                match = session.Plan.Steps.Find(s => s.Status == PlanStepStatus.Running)
                        ?? session.Plan.Steps.Find(s => s.Status == PlanStepStatus.Pending);
            }
            if (match == null) return;
            match.Status = success ? PlanStepStatus.Completed : PlanStepStatus.Failed;
            match.Detail = toolName;
            onChanged?.Invoke();
        }

        static AgentPlanStep FindBestPlanStep(AgentSession session, string toolName)
        {
            if (session.Plan?.Steps == null) return null;
            var running = session.Plan.Steps.Find(s => s.Status == PlanStepStatus.Running);
            if (running != null) return running;

            foreach (var step in session.Plan.Steps)
            {
                if (step.Status != PlanStepStatus.Pending) continue;
                if (string.IsNullOrEmpty(toolName)) return step;
                if (!string.IsNullOrEmpty(step.Title) &&
                    step.Title.IndexOf(toolName.Replace('_', ' '), StringComparison.OrdinalIgnoreCase) >= 0)
                    return step;
                if (!string.IsNullOrEmpty(step.Title) &&
                    ToolImpliesStep(toolName, step.Title))
                    return step;
            }
            return session.Plan.Steps.Find(s => s.Status == PlanStepStatus.Pending);
        }

        static bool ToolImpliesStep(string tool, string title)
        {
            tool = tool.ToLowerInvariant();
            title = title.ToLowerInvariant();
            if (tool.Contains("script") && title.Contains("script")) return true;
            if (tool.Contains("prefab") && title.Contains("prefab")) return true;
            if (tool.Contains("material") && title.Contains("material")) return true;
            if (tool.Contains("game_object") && (title.Contains("create") || title.Contains("object") || title.Contains("player"))) return true;
            if (tool.Contains("console") && (title.Contains("console") || title.Contains("compil") || title.Contains("error"))) return true;
            if (tool.Contains("component") && title.Contains("component")) return true;
            if (tool.Contains("save_scene") && title.Contains("save")) return true;
            return false;
        }

        static void CompleteRemainingPlanSteps(AgentSession session)
        {
            if (session.Plan?.Steps == null) return;
            foreach (var step in session.Plan.Steps)
            {
                if (step.Status == PlanStepStatus.Pending || step.Status == PlanStepStatus.Running)
                    step.Status = PlanStepStatus.Completed;
            }
        }

        static string BuildPlanText(AgentSession session, string message)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(message)) lines.Add(message);
            if (!string.IsNullOrEmpty(session.Plan.Goal)) lines.Add("Goal: " + session.Plan.Goal);
            if (session.Plan.Steps.Count > 0)
            {
                lines.Add("Plan:");
                for (var i = 0; i < session.Plan.Steps.Count; i++)
                    lines.Add($"{i + 1}. {session.Plan.Steps[i].Title}");
            }
            return string.Join("\n", lines);
        }

        static bool IsMutatingTool(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            name = name.ToLowerInvariant();
            return name.StartsWith("create_") ||
                   name.StartsWith("delete_") ||
                   name.StartsWith("set_") ||
                   name.StartsWith("add_") ||
                   name.StartsWith("remove_") ||
                   name.StartsWith("patch_") ||
                   name.StartsWith("rename_") ||
                   name.StartsWith("duplicate_") ||
                   name.StartsWith("assign_") ||
                   name.StartsWith("instantiate_") ||
                   name.StartsWith("unpack_") ||
                   name == "save_scene" ||
                   name == "enter_play_mode" ||
                   name == "exit_play_mode" ||
                   name == "clear_console";
        }

        static bool NeedsCompileWait(string toolName, ToolResult result)
        {
            if (!result.Success) return false;
            if (toolName == "create_script" || toolName == "patch_script") return true;
            if (result.Data is Dictionary<string, object> data && AgentJson.GetBool(data, "needsCompile"))
                return true;
            return false;
        }

        static void SetStatus(AgentSession session, AgentStatus status, string detail, Action onChanged)
        {
            session.Status = status;
            session.StatusDetail = detail;
            SessionPersistence.Save(session);
            onChanged?.Invoke();
        }
    }
}
