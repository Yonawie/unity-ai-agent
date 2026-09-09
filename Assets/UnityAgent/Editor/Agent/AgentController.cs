using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityAgent.Editor.Context;
using UnityAgent.Editor.Diff;
using UnityAgent.Editor.LLM;
using UnityAgent.Editor.Logging;
using UnityAgent.Editor.Persistence;
using UnityAgent.Editor.Safety;
using UnityAgent.Editor.Settings;
using UnityAgent.Editor.Tools;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Agent
{
    public sealed class AgentController
    {
        static AgentController _instance;
        public static AgentController Instance => _instance ??= Create();

        static AgentController Create()
        {
            var c = new AgentController();
            c.TryResumeAfterReload();
            return c;
        }

        public ToolRegistry Registry { get; }
        public ToolDispatcher Dispatcher { get; }
        public ContextManager Context { get; }
        public ChangeTracker Changes { get; }
        public AgentSession Session { get; private set; }
        public AgentLoop Loop { get; }

        readonly Queue<string> _requestQueue = new Queue<string>();
        CancellationTokenSource _cts;
        bool _runGate;

        public bool IsRunning => _runGate;
        public int QueuedCount => _requestQueue.Count;

        public event Action SessionChanged;

        AgentController()
        {
            CompilationMonitor.EnsureHooks();
            Registry = ToolBootstrap.CreateDefaultRegistry();
            Context = new ContextManager();
            Changes = new ChangeTracker();
            var validator = new ActionValidator();
            var permissions = new PermissionManager();
            Dispatcher = new ToolDispatcher(Registry, validator, permissions, Changes);
            Loop = new AgentLoop(Registry, Dispatcher, Context, Changes);
            Session = SessionPersistence.Load() ?? new AgentSession { Mode = AgentSettings.DefaultMode };
        }

        void TryResumeAfterReload()
        {
            var restored = SessionPersistence.Load();
            if (restored == null) return;
            Session = restored;

            if (!Session.ResumeAfterReload) return;
            if (Session.Status == AgentStatus.Completed ||
                Session.Status == AgentStatus.Failed ||
                Session.Status == AgentStatus.Cancelled ||
                Session.Status == AgentStatus.Idle)
            {
                Session.ResumeAfterReload = false;
                SessionPersistence.Save(Session);
                return;
            }

            EditorApplication.delayCall += async () =>
            {
                if (_runGate) return;
                AgentLogger.Info("Resuming agent after Domain Reload");
                Session.Status = AgentStatus.WaitingForUnity;
                Session.StatusDetail = "Waiting for compilation settle after Domain Reload";
                SessionPersistence.Save(Session);
                SessionChanged?.Invoke();

                try
                {
                    // Wait until Unity is fully done compiling.
                    var token = CancellationToken.None;
                    var started = DateTime.UtcNow;
                    while (EditorApplication.isCompiling && (DateTime.UtcNow - started).TotalSeconds < 180)
                        await Task.Delay(100);

                    await Task.Delay(300);
                    var console = Dispatcher.Dispatch(new ToolCall
                    {
                        Tool = "read_console",
                        Arguments = new Dictionary<string, object>
                        {
                            ["includeWarnings"] = true,
                            ["includeLogs"] = false,
                            ["maxEntries"] = 40
                        }
                    });
                    var consoleJson = AgentJson.Serialize(console.ToDictionary());
                    Session.Messages.Add(AgentMessage.Tool("read_console", Guid.NewGuid().ToString("N"), consoleJson));
                    Session.Messages.Add(AgentMessage.User(
                        "Domain Reload finished. Fresh console JSON:\n" + consoleJson +
                        "\nContinue the previous task. If hasErrors=true, fix scripts first."));
                    Session.PendingAction = "post_reload_continue";
                    Session.ResumeAfterReload = false;
                    Session.StatusDetail = "Resumed after Domain Reload";
                    SessionPersistence.Save(Session);
                    SessionChanged?.Invoke();
                    ContinueRun();
                }
                catch (Exception ex)
                {
                    AgentLogger.Error("Resume after reload failed: " + ex);
                    Session.Status = AgentStatus.Failed;
                    Session.LastError = ex.Message;
                    Session.ResumeAfterReload = false;
                    SessionPersistence.Save(Session);
                    SessionChanged?.Invoke();
                }
            };
        }

        public void ClearConversation()
        {
            Stop();
            _requestQueue.Clear();
            DiffReview.Clear();
            Session = new AgentSession { Mode = Session?.Mode ?? AgentSettings.DefaultMode };
            SessionPersistence.Clear();
            SessionPersistence.Save(Session);
            SessionChanged?.Invoke();
        }

        public void SetMode(AgentMode mode)
        {
            Session.Mode = mode;
            SessionPersistence.Save(Session);
            SessionChanged?.Invoke();
        }

        public async Task<string> TestConnectionAsync()
        {
            var provider = LLMProviderFactory.CreateFromSettings();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var (ok, message) = await provider.TestConnectionAsync(cts.Token);
            return ok ? message : "ERROR: " + message;
        }

        public void Send(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText)) return;
            userText = userText.Trim();

            if (_runGate)
            {
                _requestQueue.Enqueue(userText);
                Session.Messages.Add(AgentMessage.User($"[queued] {userText}"));
                Session.StatusDetail = $"Queued ({_requestQueue.Count} waiting)";
                SessionPersistence.Save(Session);
                SessionChanged?.Invoke();
                AgentLogger.Info("Queued request: " + userText);
                return;
            }

            StartRequest(userText);
        }

        void StartRequest(string userText)
        {
            Session.UserRequest = userText;
            Session.Messages.Add(AgentMessage.User(Session.UserRequest));
            Session.Status = AgentStatus.Thinking;
            Session.StatusDetail = $"Calling {AgentSettings.Current.Provider} ({AgentSettings.Current.Model})";
            Session.LastError = null;
            Session.FixAttempts = 0;
            Session.CurrentStep = 0;
            Session.PendingAction = null;
            Session.ResumeAfterReload = false;
            Session.StreamingText = null;
            if (Session.Plan == null) Session.Plan = new AgentPlan();
            SessionPersistence.Save(Session);
            SessionChanged?.Invoke();
            AgentLogger.Info("User request: " + Session.UserRequest);
            AgentLogger.Info($"Provider={AgentSettings.Current.Provider} BaseUrl={AgentSettings.Current.BaseUrl} Model={AgentSettings.Current.Model} Streaming={AgentSettings.Current.EnableStreaming}");
            ContinueRun();
        }

        async void ContinueRun()
        {
            if (_runGate) return;
            _runGate = true;
            _cts = new CancellationTokenSource();

            try
            {
                await Loop.RunAsync(Session, _cts.Token, () => SessionChanged?.Invoke());
            }
            catch (OperationCanceledException)
            {
                Session.Status = AgentStatus.Cancelled;
                Session.StatusDetail = "Cancelled by user";
                Session.Messages.Add(AgentMessage.Assistant("Cancelled."));
                SessionPersistence.Save(Session);
                SessionChanged?.Invoke();
            }
            catch (Exception ex)
            {
                if (Session != null && Session.ResumeAfterReload)
                {
                    AgentLogger.Warn("Agent loop interrupted (likely Domain Reload). Will resume.");
                    SessionPersistence.Save(Session);
                    SessionChanged?.Invoke();
                    return;
                }

                AgentLogger.Error("AgentController failed: " + ex);
                Session.Status = AgentStatus.Failed;
                Session.LastError = ex.Message;
                Session.StatusDetail = ex.Message;
                Session.Messages.Add(AgentMessage.Assistant("Failed: " + ex.Message));
                SessionPersistence.Save(Session);
                SessionChanged?.Invoke();
            }
            finally
            {
                _runGate = false;
                _cts?.Dispose();
                _cts = null;

                // Drain queue
                if (_requestQueue.Count > 0 &&
                    Session != null &&
                    Session.Status != AgentStatus.Cancelled &&
                    !Session.ResumeAfterReload)
                {
                    var next = _requestQueue.Dequeue();
                    EditorApplication.delayCall += () => StartRequest(next);
                }
            }
        }

        public void Stop()
        {
            _requestQueue.Clear();
            try { _cts?.Cancel(); } catch { /* ignore */ }

            if (Session != null &&
                Session.Status != AgentStatus.Completed &&
                Session.Status != AgentStatus.Failed)
            {
                Session.ResumeAfterReload = false;
                Session.Status = AgentStatus.Cancelled;
                Session.StatusDetail = "Cancelled";
                SessionPersistence.Save(Session);
                SessionChanged?.Invoke();
            }
        }

        public bool TryUndo(out string message) => Changes.TryUndoTask(out message);
    }
}
