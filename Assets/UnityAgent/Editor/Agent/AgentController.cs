using System;
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

        CancellationTokenSource _cts;
        bool _runGate;

        public bool IsRunning => _runGate;

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
            AssemblyReloadEvents.afterAssemblyReload += () =>
            {
                // Static instance may be new after reload; UI binds to Instance.
            };
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

            EditorApplication.delayCall += () =>
            {
                if (_runGate) return;
                AgentLogger.Info("Resuming agent after Domain Reload");
                Session.StatusDetail = "Resumed after Domain Reload";
                Session.ResumeAfterReload = false;
                SessionPersistence.Save(Session);
                SessionChanged?.Invoke();
                ContinueRun();
            };
        }

        public void ClearConversation()
        {
            Stop();
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
            if (_runGate)
            {
                Session.LastError = "Agent is already running. Press Stop first.";
                SessionChanged?.Invoke();
                return;
            }

            Session.UserRequest = userText.Trim();
            Session.Messages.Add(AgentMessage.User(Session.UserRequest));
            Session.Status = AgentStatus.Thinking;
            Session.StatusDetail = "Starting";
            Session.LastError = null;
            Session.FixAttempts = 0;
            Session.CurrentStep = 0;
            Session.PendingAction = null;
            Session.ResumeAfterReload = false;
            if (Session.Plan == null) Session.Plan = new AgentPlan();
            SessionPersistence.Save(Session);
            SessionChanged?.Invoke();
            AgentLogger.Info("User request: " + Session.UserRequest);
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
                // Domain reload aborts tasks; persist resume flag if we were compiling.
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
            }
        }

        public void Stop()
        {
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
