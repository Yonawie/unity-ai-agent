using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Diff;
using UnityAgent.Editor.Settings;

namespace UnityAgent.Editor.UI
{
    public class AgentWindow : EditorWindow
    {
        AgentController _controller;
        ScrollView _chatScroll;
        VisualElement _chatContainer;
        VisualElement _planContainer;
        VisualElement _actionsContainer;
        VisualElement _changesContainer;
        Label _diffLabel;
        Label _statusLabel;
        Label _errorLabel;
        TextField _inputField;
        TextField _providerField;
        TextField _baseUrlField;
        TextField _modelField;
        EnumField _modeField;
        bool _uiBound;

        [MenuItem("Window/AI Agent")]
        public static void Open()
        {
            var window = GetWindow<AgentWindow>();
            window.titleContent = new GUIContent("AI Agent");
            window.minSize = new Vector2(780, 520);
            window.Show();
        }

        [MenuItem("Window/AI Agent/Use LM Studio Defaults")]
        public static void UseLmStudioDefaults()
        {
            AgentSettings.ApplyLmStudioDefaults();
            Debug.Log("[UnityAgent] Applied LM Studio defaults: Provider=LMStudio, BaseUrl=http://127.0.0.1:1234/v1");
            var window = GetWindow<AgentWindow>();
            window.CreateGUI();
        }

        void OnEnable()
        {
            _controller = AgentController.Instance;
            _controller.SessionChanged += OnSessionChanged;
            CompilationMonitor.EnsureHooks();
        }

        void OnDisable()
        {
            if (_controller != null)
                _controller.SessionChanged -= OnSessionChanged;
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.Clear();

            var uxmlPath = FindAssetPath("AgentWindow.uxml");
            var ussPath = FindAssetPath("AgentWindow.uss");

            if (!string.IsNullOrEmpty(uxmlPath))
            {
                var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
                if (visualTree != null)
                    visualTree.CloneTree(root);
            }

            if (root.Q("root") == null)
                BuildFallbackUi(root);

            if (!string.IsNullOrEmpty(ussPath))
            {
                var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
                if (style != null)
                {
                    var already = false;
                    for (var i = 0; i < root.styleSheets.count; i++)
                    {
                        if (root.styleSheets[i] == style) { already = true; break; }
                    }
                    if (!already) root.styleSheets.Add(style);
                }
            }

            BindUi(root);
            RefreshAll();
        }

        void BuildFallbackUi(VisualElement root)
        {
            root.Add(new Label("Unity AI Agent") { name = "title" });
            var mode = new EnumField("Mode", AgentMode.Agent) { name = "mode-field" };
            root.Add(mode);
            root.Add(new Label("Idle") { name = "status-label" });
            root.Add(new TextField("Provider") { name = "provider-field" });
            root.Add(new TextField("Base URL") { name = "base-url-field" });
            root.Add(new TextField("Model") { name = "model-field" });
            root.Add(new Button { name = "lmstudio-defaults-button", text = "LM Studio" });
            root.Add(new Button { name = "test-connection-button", text = "Test Connection" });
            var chat = new ScrollView { name = "chat-scroll" };
            chat.Add(new VisualElement { name = "chat-container" });
            root.Add(chat);
            root.Add(new TextField { name = "input-field", multiline = true });
            root.Add(new Button { name = "send-button", text = "Send" });
            root.Add(new Button { name = "stop-button", text = "Stop" });
            root.Add(new Button { name = "clear-button", text = "Clear" });
            root.Add(new Button { name = "undo-button", text = "Undo Task" });
            var plan = new ScrollView { name = "plan-scroll" };
            plan.Add(new VisualElement { name = "plan-container" });
            root.Add(plan);
            var actions = new ScrollView { name = "actions-scroll" };
            actions.Add(new VisualElement { name = "actions-container" });
            root.Add(actions);
            var changes = new ScrollView { name = "changes-scroll" };
            changes.Add(new VisualElement { name = "changes-container" });
            root.Add(changes);
            root.Add(new Label("No diff yet.") { name = "diff-label" });
            root.Add(new Label { name = "error-label" });
        }

        void BindUi(VisualElement root)
        {
            _chatScroll = root.Q<ScrollView>("chat-scroll");
            _chatContainer = root.Q<VisualElement>("chat-container") ?? _chatScroll?.contentContainer;
            _planContainer = root.Q<VisualElement>("plan-container");
            _actionsContainer = root.Q<VisualElement>("actions-container");
            _changesContainer = root.Q<VisualElement>("changes-container");
            _diffLabel = root.Q<Label>("diff-label");
            _statusLabel = root.Q<Label>("status-label");
            _errorLabel = root.Q<Label>("error-label");
            _inputField = root.Q<TextField>("input-field");
            _providerField = root.Q<TextField>("provider-field");
            _baseUrlField = root.Q<TextField>("base-url-field");
            _modelField = root.Q<TextField>("model-field");
            _modeField = root.Q<EnumField>("mode-field");

            if (_modeField != null)
            {
                _modeField.Init(_controller.Session.Mode);
                _modeField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is AgentMode mode)
                        _controller.SetMode(mode);
                });
            }

            var settings = AgentSettings.Current;
            if (_providerField != null)
            {
                _providerField.value = settings.Provider;
                _providerField.RegisterValueChangedCallback(evt =>
                {
                    AgentSettings.Current.Provider = evt.newValue;
                    AgentSettings.Save();
                });
            }

            if (_baseUrlField != null)
            {
                _baseUrlField.value = settings.BaseUrl;
                _baseUrlField.RegisterValueChangedCallback(evt =>
                {
                    AgentSettings.Current.BaseUrl = evt.newValue;
                    AgentSettings.Save();
                });
            }

            if (_modelField != null)
            {
                _modelField.value = settings.Model;
                _modelField.RegisterValueChangedCallback(evt =>
                {
                    AgentSettings.Current.Model = evt.newValue;
                    AgentSettings.Save();
                });
            }

            root.Q<Button>("lmstudio-defaults-button")?.RegisterCallback<ClickEvent>(_ =>
            {
                AgentSettings.ApplyLmStudioDefaults();
                AgentSettings.Reload();
                var s = AgentSettings.Current;
                if (_providerField != null) _providerField.SetValueWithoutNotify(s.Provider);
                if (_baseUrlField != null) _baseUrlField.SetValueWithoutNotify(s.BaseUrl);
                if (_errorLabel != null)
                    _errorLabel.text = "LM Studio defaults applied. Check Model name, then Test Connection.";
            });

            root.Q<Button>("send-button")?.RegisterCallback<ClickEvent>(_ => OnSend());
            root.Q<Button>("stop-button")?.RegisterCallback<ClickEvent>(_ => _controller.Stop());
            root.Q<Button>("clear-button")?.RegisterCallback<ClickEvent>(_ =>
            {
                _controller.ClearConversation();
                RefreshAll();
            });
            root.Q<Button>("undo-button")?.RegisterCallback<ClickEvent>(_ =>
            {
                if (_controller.TryUndo(out var msg))
                    _errorLabel.text = msg;
                else
                    _errorLabel.text = msg;
            });
            root.Q<Button>("test-connection-button")?.RegisterCallback<ClickEvent>(async _ =>
            {
                _errorLabel.text = "Testing connection…";
                try
                {
                    var result = await _controller.TestConnectionAsync();
                    _errorLabel.text = result;
                }
                catch (Exception ex)
                {
                    _errorLabel.text = "ERROR: " + ex.Message;
                }
            });

            _inputField?.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return && !evt.shiftKey)
                {
                    evt.StopPropagation();
                    OnSend();
                }
            });

            _uiBound = true;
        }

        void OnSend()
        {
            if (_inputField == null) return;
            var text = _inputField.value;
            if (string.IsNullOrWhiteSpace(text)) return;
            _inputField.value = string.Empty;
            _controller.Send(text);
        }

        void OnSessionChanged()
        {
            EditorApplication.delayCall += RefreshAll;
        }

        void RefreshAll()
        {
            if (!_uiBound || _controller == null) return;
            var session = _controller.Session;
            if (session == null) return;

            if (_statusLabel != null)
            {
                var queue = _controller.QueuedCount > 0 ? $" | queue:{_controller.QueuedCount}" : "";
                _statusLabel.text = string.IsNullOrEmpty(session.StatusDetail)
                    ? session.Status + queue
                    : $"{session.Status}: {session.StatusDetail}{queue}";
            }

            if (_modeField != null && !Equals(_modeField.value, session.Mode))
                _modeField.SetValueWithoutNotify(session.Mode);

            if (_errorLabel != null)
                _errorLabel.text = session.LastError ?? _errorLabel.text;

            RefreshChat(session);
            RefreshPlan(session);
            RefreshActions(session);
            RefreshChanges();
            RefreshDiff();
        }

        void RefreshChat(AgentSession session)
        {
            if (_chatContainer == null) return;
            _chatContainer.Clear();
            foreach (var msg in session.Messages)
            {
                if (msg.Role == "system") continue;
                var bubble = new VisualElement();
                bubble.AddToClassList("chat-bubble");
                if (msg.IsError) bubble.AddToClassList("chat-error");
                else if (msg.Role == "user") bubble.AddToClassList("chat-user");
                else if (msg.Role == "tool") bubble.AddToClassList("chat-tool");
                else bubble.AddToClassList("chat-assistant");

                var role = new Label(msg.Role + (string.IsNullOrEmpty(msg.ToolName) ? "" : $" ({msg.ToolName})"));
                role.AddToClassList("bubble-role");
                bubble.Add(role);
                bubble.Add(new Label(TrimForUi(msg.Content)));
                _chatContainer.Add(bubble);
            }

            if (!string.IsNullOrEmpty(session.StreamingText))
            {
                var streamBubble = new VisualElement();
                streamBubble.AddToClassList("chat-bubble");
                streamBubble.AddToClassList("chat-assistant");
                var role = new Label("assistant (streaming…)");
                role.AddToClassList("bubble-role");
                streamBubble.Add(role);
                streamBubble.Add(new Label(TrimForUi(session.StreamingText)));
                _chatContainer.Add(streamBubble);
            }

            _chatScroll?.schedule.Execute(() => _chatScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        void RefreshPlan(AgentSession session)
        {
            if (_planContainer == null) return;
            _planContainer.Clear();
            if (!string.IsNullOrEmpty(session.Plan?.Goal))
                _planContainer.Add(new Label("Goal: " + session.Plan.Goal));

            if (session.Plan == null) return;
            for (var i = 0; i < session.Plan.Steps.Count; i++)
            {
                var step = session.Plan.Steps[i];
                var row = new Label($"{i + 1}. [{step.Status}] {step.Title}");
                row.AddToClassList("plan-item");
                if (step.Status == PlanStepStatus.Running) row.AddToClassList("plan-running");
                if (step.Status == PlanStepStatus.Completed) row.AddToClassList("plan-done");
                if (step.Status == PlanStepStatus.Failed) row.AddToClassList("plan-failed");
                _planContainer.Add(row);
            }
        }

        void RefreshActions(AgentSession session)
        {
            if (_actionsContainer == null) return;
            _actionsContainer.Clear();
            foreach (var action in session.ActionLog)
            {
                var label = new Label(action);
                label.AddToClassList("action-item");
                _actionsContainer.Add(label);
            }
        }

        void RefreshChanges()
        {
            if (_changesContainer == null || _controller == null) return;
            _changesContainer.Clear();
            var summary = _controller.Changes?.Summarize() ?? "No tracked changes.";
            foreach (var line in summary.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var label = new Label(line.TrimEnd());
                label.AddToClassList("action-item");
                _changesContainer.Add(label);
            }
        }

        void RefreshDiff()
        {
            if (_diffLabel == null) return;
            if (string.IsNullOrEmpty(DiffReview.UnifiedDiff))
            {
                _diffLabel.text = "No diff yet.";
                return;
            }

            var header = string.IsNullOrEmpty(DiffReview.Path) ? "" : DiffReview.Path + "\n" + DiffReview.Brief + "\n\n";
            _diffLabel.text = TrimForUi(header + DiffReview.UnifiedDiff);
        }

        static string TrimForUi(string content)
        {
            if (string.IsNullOrEmpty(content)) return string.Empty;
            return content.Length <= 4000 ? content : content.Substring(0, 4000) + "…";
        }

        static string FindAssetPath(string fileName)
        {
            var guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(fileName));
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase) &&
                    path.Replace('\\', '/').Contains("/UnityAgent/"))
                    return path;
            }

            // Fallback conventional path
            var candidate = "Assets/UnityAgent/Editor/UI/" + fileName;
            return File.Exists(candidate) ? candidate : null;
        }
    }
}
