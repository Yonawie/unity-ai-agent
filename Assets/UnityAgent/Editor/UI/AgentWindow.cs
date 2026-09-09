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
        VisualElement _settingsPanel;
        VisualElement _activityPanel;
        VisualElement _tabPlan;
        VisualElement _tabTools;
        VisualElement _tabChanges;
        VisualElement _tabDiff;
        Button _tabBtnPlan;
        Button _tabBtnTools;
        Button _tabBtnChanges;
        Button _tabBtnDiff;
        Label _diffLabel;
        Label _statusLabel;
        Label _errorLabel;
        Label _modelChip;
        TextField _inputField;
        TextField _providerField;
        TextField _baseUrlField;
        TextField _modelField;
        EnumField _modeField;
        bool _uiBound;
        bool _settingsOpen;
        bool _activityOpen;
        string _activeTab = "plan";

        static readonly string[] Suggestions =
        {
            "Create a cube named TestCube at 0,1,0",
            "Add Rigidbody and a WASD movement script",
            "Make a simple Canvas with a Play button"
        };

        [MenuItem("Window/AI Agent")]
        public static void Open()
        {
            var window = GetWindow<AgentWindow>();
            window.titleContent = new GUIContent("Agent");
            window.minSize = new Vector2(640, 520);
            window.Show();
        }

        [MenuItem("Tools/AI Agent/Use LM Studio Defaults")]
        public static void UseLmStudioDefaults()
        {
            AgentSettings.ApplyLmStudioDefaults();
            Debug.Log("[UnityAgent] Applied LM Studio defaults: Provider=LMStudio, BaseUrl=http://127.0.0.1:1234/v1");
            if (HasOpenInstances<AgentWindow>())
            {
                var window = GetWindow<AgentWindow>();
                window.CreateGUI();
            }
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
            _uiBound = false;

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
            ApplyPanelVisibility();
            ShowTab(_activeTab);
            RefreshAll();
        }

        void BuildFallbackUi(VisualElement root)
        {
            var shell = new VisualElement { name = "root" };
            shell.AddToClassList("root");
            root.Add(shell);

            shell.Add(new Label("Agent") { name = "brand" });
            shell.Add(new Label { name = "model-chip" });
            shell.Add(new Label("Idle") { name = "status-label" });
            shell.Add(new Button { name = "toggle-settings-button", text = "Settings" });
            shell.Add(new Button { name = "toggle-activity-button", text = "Details" });

            var settings = new VisualElement { name = "settings-panel" };
            settings.Add(new TextField("Provider") { name = "provider-field" });
            settings.Add(new TextField("Base URL") { name = "base-url-field" });
            settings.Add(new TextField("Model") { name = "model-field" });
            settings.Add(new Button { name = "lmstudio-defaults-button", text = "LM Studio" });
            settings.Add(new Button { name = "test-connection-button", text = "Test Connection" });
            settings.Add(new Label { name = "error-label" });
            shell.Add(settings);

            var chat = new ScrollView { name = "chat-scroll" };
            chat.Add(new VisualElement { name = "chat-container" });
            shell.Add(chat);

            shell.Add(new EnumField(AgentMode.Agent) { name = "mode-field" });
            shell.Add(new TextField { name = "input-field", multiline = true });
            shell.Add(new Button { name = "send-button", text = "Send" });
            shell.Add(new Button { name = "stop-button", text = "Stop" });
            shell.Add(new Button { name = "clear-button", text = "Clear" });
            shell.Add(new Button { name = "undo-button", text = "Undo" });

            var activity = new VisualElement { name = "activity-panel" };
            activity.Add(new Button { name = "tab-plan", text = "Plan" });
            activity.Add(new Button { name = "tab-tools", text = "Tools" });
            activity.Add(new Button { name = "tab-changes", text = "Changes" });
            activity.Add(new Button { name = "tab-diff", text = "Diff" });

            var planPanel = new VisualElement { name = "tab-panel-plan" };
            var plan = new ScrollView { name = "plan-scroll" };
            plan.Add(new VisualElement { name = "plan-container" });
            planPanel.Add(plan);
            activity.Add(planPanel);

            var toolsPanel = new VisualElement { name = "tab-panel-tools" };
            var actions = new ScrollView { name = "actions-scroll" };
            actions.Add(new VisualElement { name = "actions-container" });
            toolsPanel.Add(actions);
            activity.Add(toolsPanel);

            var changesPanel = new VisualElement { name = "tab-panel-changes" };
            var changes = new ScrollView { name = "changes-scroll" };
            changes.Add(new VisualElement { name = "changes-container" });
            changesPanel.Add(changes);
            activity.Add(changesPanel);

            var diffPanel = new VisualElement { name = "tab-panel-diff" };
            diffPanel.Add(new Label("No diff yet.") { name = "diff-label" });
            activity.Add(diffPanel);
            shell.Add(activity);
        }

        void BindUi(VisualElement root)
        {
            _chatScroll = root.Q<ScrollView>("chat-scroll");
            _chatContainer = root.Q<VisualElement>("chat-container") ?? _chatScroll?.contentContainer;
            _planContainer = root.Q<VisualElement>("plan-container");
            _actionsContainer = root.Q<VisualElement>("actions-container");
            _changesContainer = root.Q<VisualElement>("changes-container");
            _settingsPanel = root.Q<VisualElement>("settings-panel");
            _activityPanel = root.Q<VisualElement>("activity-panel");
            _tabPlan = root.Q("tab-panel-plan");
            _tabTools = root.Q("tab-panel-tools");
            _tabChanges = root.Q("tab-panel-changes");
            _tabDiff = root.Q("tab-panel-diff");
            _tabBtnPlan = root.Q<Button>("tab-plan");
            _tabBtnTools = root.Q<Button>("tab-tools");
            _tabBtnChanges = root.Q<Button>("tab-changes");
            _tabBtnDiff = root.Q<Button>("tab-diff");
            _diffLabel = root.Q<Label>("diff-label");
            _statusLabel = root.Q<Label>("status-label");
            _errorLabel = root.Q<Label>("error-label");
            _modelChip = root.Q<Label>("model-chip");
            _inputField = root.Q<TextField>("input-field");
            _providerField = root.Q<TextField>("provider-field");
            _baseUrlField = root.Q<TextField>("base-url-field");
            _modelField = root.Q<TextField>("model-field");
            _modeField = root.Q<EnumField>("mode-field");

            if (_inputField != null)
            {
                // Placeholder-like hint via tooltip; UI Toolkit TextField has no built-in placeholder in older Unity.
                _inputField.tooltip = "Plan, search, build anything in Unity…";
            }

            if (_modeField != null)
            {
                _modeField.Init(_controller.Session?.Mode ?? AgentMode.Agent);
                _modeField.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue is AgentMode mode)
                        _controller.SetMode(mode);
                });
            }

            var settings = AgentSettings.Current;
            BindSettingField(_providerField, settings.Provider, v =>
            {
                AgentSettings.Current.Provider = v;
                RefreshModelChip();
            });
            BindSettingField(_baseUrlField, settings.BaseUrl, v => AgentSettings.Current.BaseUrl = v);
            BindSettingField(_modelField, settings.Model, v =>
            {
                AgentSettings.Current.Model = v;
                RefreshModelChip();
            });

            root.Q<Button>("toggle-settings-button")?.RegisterCallback<ClickEvent>(_ =>
            {
                _settingsOpen = !_settingsOpen;
                ApplyPanelVisibility();
            });
            root.Q<Button>("toggle-activity-button")?.RegisterCallback<ClickEvent>(_ =>
            {
                _activityOpen = !_activityOpen;
                ApplyPanelVisibility();
            });

            _tabBtnPlan?.RegisterCallback<ClickEvent>(_ => ShowTab("plan"));
            _tabBtnTools?.RegisterCallback<ClickEvent>(_ => ShowTab("tools"));
            _tabBtnChanges?.RegisterCallback<ClickEvent>(_ => ShowTab("changes"));
            _tabBtnDiff?.RegisterCallback<ClickEvent>(_ => ShowTab("diff"));

            root.Q<Button>("lmstudio-defaults-button")?.RegisterCallback<ClickEvent>(_ =>
            {
                AgentSettings.ApplyLmStudioDefaults();
                AgentSettings.Reload();
                var s = AgentSettings.Current;
                _providerField?.SetValueWithoutNotify(s.Provider);
                _baseUrlField?.SetValueWithoutNotify(s.BaseUrl);
                if (_errorLabel != null)
                    _errorLabel.text = "LM Studio defaults applied. Check model, then Test Connection.";
                _settingsOpen = true;
                ApplyPanelVisibility();
                RefreshModelChip();
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
                _controller.TryUndo(out var msg);
                if (_errorLabel != null) _errorLabel.text = msg;
                _settingsOpen = true;
                ApplyPanelVisibility();
            });
            root.Q<Button>("test-connection-button")?.RegisterCallback<ClickEvent>(async _ =>
            {
                if (_errorLabel != null) _errorLabel.text = "Testing connection…";
                _settingsOpen = true;
                ApplyPanelVisibility();
                try
                {
                    // Persist fields before test.
                    if (_providerField != null) AgentSettings.Current.Provider = _providerField.value;
                    if (_baseUrlField != null) AgentSettings.Current.BaseUrl = _baseUrlField.value;
                    if (_modelField != null) AgentSettings.Current.Model = _modelField.value;
                    AgentSettings.Current.EnableStreaming = false;
                    AgentSettings.Save();

                    var result = await _controller.TestConnectionAsync();
                    if (_errorLabel != null) _errorLabel.text = result;

                    // If model empty and test succeeded, try to adopt first listed model id from message.
                    if (string.IsNullOrWhiteSpace(AgentSettings.Current.Model) &&
                        result != null &&
                        result.IndexOf("Models loaded:", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var idx = result.IndexOf("Models loaded:", StringComparison.OrdinalIgnoreCase);
                        var list = result.Substring(idx + "Models loaded:".Length).Trim();
                        var first = list.Split(',')[0].Trim().TrimEnd('.');
                        if (!string.IsNullOrWhiteSpace(first) &&
                            !first.StartsWith("Set ", StringComparison.OrdinalIgnoreCase))
                        {
                            AgentSettings.Current.Model = first;
                            AgentSettings.Save();
                            _modelField?.SetValueWithoutNotify(first);
                            if (_errorLabel != null)
                                _errorLabel.text = result + $" Auto-filled Model={first}";
                        }
                    }

                    RefreshModelChip();
                }
                catch (Exception ex)
                {
                    if (_errorLabel != null) _errorLabel.text = "ERROR: " + ex.Message;
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
            RefreshModelChip();
        }

        static void BindSettingField(TextField field, string value, Action<string> assign)
        {
            if (field == null) return;
            field.value = value ?? string.Empty;
            field.RegisterValueChangedCallback(evt =>
            {
                assign(evt.newValue);
                AgentSettings.Save();
            });
        }

        void ApplyPanelVisibility()
        {
            if (_settingsPanel != null)
            {
                if (_settingsOpen) _settingsPanel.RemoveFromClassList("settings-hidden");
                else _settingsPanel.AddToClassList("settings-hidden");
            }

            if (_activityPanel != null)
            {
                if (_activityOpen) _activityPanel.RemoveFromClassList("activity-hidden");
                else _activityPanel.AddToClassList("activity-hidden");
            }
        }

        void ShowTab(string tab)
        {
            _activeTab = tab ?? "plan";
            SetTabVisible(_tabPlan, _tabBtnPlan, _activeTab == "plan");
            SetTabVisible(_tabTools, _tabBtnTools, _activeTab == "tools");
            SetTabVisible(_tabChanges, _tabBtnChanges, _activeTab == "changes");
            SetTabVisible(_tabDiff, _tabBtnDiff, _activeTab == "diff");
        }

        static void SetTabVisible(VisualElement panel, Button button, bool active)
        {
            if (panel != null)
            {
                if (active) panel.RemoveFromClassList("tab-panel-hidden");
                else panel.AddToClassList("tab-panel-hidden");
            }

            if (button == null) return;
            if (active) button.AddToClassList("tab-active");
            else button.RemoveFromClassList("tab-active");
        }

        void RefreshModelChip()
        {
            if (_modelChip == null) return;
            var s = AgentSettings.Current;
            var model = string.IsNullOrEmpty(s.Model) ? "no model" : s.Model;
            var provider = string.IsNullOrEmpty(s.Provider) ? "?" : s.Provider;
            _modelChip.text = $"{provider} · {model}";
            _modelChip.tooltip = $"{s.Provider}\n{s.BaseUrl}\n{s.Model}";
        }

        void OnSend()
        {
            if (_inputField == null) return;
            var text = _inputField.value;
            if (string.IsNullOrWhiteSpace(text))
            {
                _settingsOpen = true;
                ApplyPanelVisibility();
                if (_errorLabel != null) _errorLabel.text = "Type a message first.";
                return;
            }

            if (_providerField != null) AgentSettings.Current.Provider = _providerField.value;
            if (_baseUrlField != null) AgentSettings.Current.BaseUrl = _baseUrlField.value;
            if (_modelField != null) AgentSettings.Current.Model = _modelField.value;
            AgentSettings.Save();
            RefreshModelChip();

            _inputField.value = string.Empty;
            _controller.Send(text);
            RefreshAll();
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
                var queue = _controller.QueuedCount > 0 ? $" · queue {_controller.QueuedCount}" : "";
                var detail = session.StatusDetail;
                if (string.IsNullOrEmpty(detail) && !string.IsNullOrEmpty(session.LastError))
                    detail = session.LastError;
                // Keep status short in the pill; full text in tooltip.
                var shortDetail = detail ?? "";
                if (shortDetail.Length > 42) shortDetail = shortDetail.Substring(0, 42) + "…";
                _statusLabel.text = string.IsNullOrEmpty(shortDetail)
                    ? session.Status + queue
                    : $"{session.Status}: {shortDetail}{queue}";
                _statusLabel.tooltip = detail ?? session.Status.ToString();
            }

            if (_modeField != null && !Equals(_modeField.value, session.Mode))
                _modeField.SetValueWithoutNotify(session.Mode);

            if (_errorLabel != null && !string.IsNullOrEmpty(session.LastError))
            {
                _errorLabel.text = session.LastError;
                _settingsOpen = true;
                ApplyPanelVisibility();
            }

            // Auto-open Details when there is plan/tools/diff activity.
            if (!_activityOpen && HasActivity(session))
            {
                _activityOpen = true;
                ApplyPanelVisibility();
            }

            RefreshModelChip();
            RefreshChat(session);
            RefreshPlan(session);
            RefreshActions(session);
            RefreshChanges();
            RefreshDiff();
        }

        static bool HasActivity(AgentSession session)
        {
            if (session.Plan != null && session.Plan.Steps != null && session.Plan.Steps.Count > 0)
                return true;
            if (session.ActionLog != null && session.ActionLog.Count > 0)
                return true;
            if (!string.IsNullOrEmpty(DiffReview.UnifiedDiff))
                return true;
            return false;
        }

        void RefreshChat(AgentSession session)
        {
            if (_chatContainer == null) return;
            _chatContainer.Clear();

            var hasVisible = false;
            foreach (var msg in session.Messages)
            {
                if (msg.Role == "system") continue;
                hasVisible = true;
                _chatContainer.Add(BuildMessage(msg.Role, msg.ToolName, msg.Content, msg.IsError, streaming: false));
            }

            if (!string.IsNullOrEmpty(session.StreamingText))
            {
                hasVisible = true;
                _chatContainer.Add(BuildMessage("assistant", null, session.StreamingText, false, streaming: true));
            }

            if (!hasVisible)
                _chatContainer.Add(BuildEmptyState());

            _chatScroll?.schedule.Execute(() => _chatScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        VisualElement BuildEmptyState()
        {
            var box = new VisualElement();
            box.AddToClassList("chat-empty");

            var title = new Label("What should we build in Unity?");
            title.AddToClassList("chat-empty-title");
            box.Add(title);

            var sub = new Label("Ask, plan, or let Agent edit the project through tools.");
            sub.AddToClassList("chat-empty-sub");
            box.Add(sub);

            foreach (var suggestion in Suggestions)
            {
                var chip = new Button(() =>
                {
                    if (_inputField == null) return;
                    _inputField.value = suggestion;
                    _inputField.Focus();
                })
                {
                    text = suggestion
                };
                chip.AddToClassList("suggestion");
                box.Add(chip);
            }

            return box;
        }

        static VisualElement BuildMessage(string role, string toolName, string content, bool isError, bool streaming)
        {
            var row = new VisualElement();
            row.AddToClassList("msg");

            var displayRole = "Agent";
            var avatarClass = "msg-avatar-agent";
            if (isError || string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase) &&
                (content?.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                row.AddToClassList("msg-error");
                displayRole = "Error";
                avatarClass = "msg-avatar-error";
            }
            else if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
            {
                row.AddToClassList("msg-user");
                displayRole = "You";
                avatarClass = "msg-avatar-user";
            }
            else if (string.Equals(role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                row.AddToClassList("msg-tool");
                displayRole = string.IsNullOrEmpty(toolName) ? "Tool" : toolName;
                avatarClass = "msg-avatar-tool";
            }
            else
            {
                row.AddToClassList("msg-assistant");
                if (streaming)
                    row.AddToClassList("msg-streaming");
            }

            var header = new VisualElement();
            header.AddToClassList("msg-header");
            var avatar = new VisualElement();
            avatar.AddToClassList("msg-avatar");
            avatar.AddToClassList(avatarClass);
            header.Add(avatar);
            var roleLabel = new Label(displayRole);
            roleLabel.AddToClassList("msg-role");
            header.Add(roleLabel);
            if (streaming)
            {
                var meta = new Label("streaming");
                meta.AddToClassList("msg-meta");
                header.Add(meta);
            }
            row.Add(header);

            var body = new Label(TrimForUi(content));
            body.AddToClassList("msg-body");
            row.Add(body);
            return row;
        }

        void RefreshPlan(AgentSession session)
        {
            if (_planContainer == null) return;
            _planContainer.Clear();
            if (!string.IsNullOrEmpty(session.Plan?.Goal))
            {
                var goal = new Label("Goal: " + session.Plan.Goal);
                goal.AddToClassList("plan-item");
                _planContainer.Add(goal);
            }

            if (session.Plan == null || session.Plan.Steps.Count == 0)
            {
                var empty = new Label("No plan yet");
                empty.AddToClassList("action-item");
                _planContainer.Add(empty);
                return;
            }

            for (var i = 0; i < session.Plan.Steps.Count; i++)
            {
                var step = session.Plan.Steps[i];
                var row = new Label($"{i + 1}. {step.Title}");
                row.tooltip = step.Status.ToString();
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
            if (session.ActionLog == null || session.ActionLog.Count == 0)
            {
                var empty = new Label("No tool calls yet");
                empty.AddToClassList("action-item");
                _actionsContainer.Add(empty);
                return;
            }

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

            var candidate = "Assets/UnityAgent/Editor/UI/" + fileName;
            return File.Exists(candidate) ? candidate : null;
        }
    }
}
