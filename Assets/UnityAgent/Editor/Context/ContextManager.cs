using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityAgent.Editor.Tools.Console;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Context
{
    public sealed class SceneContext
    {
        public Dictionary<string, object> Capture(int maxRoots = 40)
        {
            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var list = new List<object>();
            foreach (var root in roots.Take(maxRoots))
            {
                list.Add(new Dictionary<string, object>
                {
                    ["id"] = ObjectIdUtil.GetId(root),
                    ["name"] = root.name,
                    ["childCount"] = root.transform.childCount,
                    ["components"] = root.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name).ToList()
                });
            }

            return new Dictionary<string, object>
            {
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["isDirty"] = scene.isDirty,
                ["rootCount"] = scene.rootCount,
                ["rootsPreview"] = list
            };
        }
    }

    public sealed class SelectionContext
    {
        public Dictionary<string, object> Capture()
        {
            var selected = Selection.gameObjects ?? Array.Empty<GameObject>();
            return new Dictionary<string, object>
            {
                ["count"] = selected.Length,
                ["objects"] = selected.Select(ObjectIdUtil.Describe).Cast<object>().ToList()
            };
        }
    }

    public sealed class ProjectContext
    {
        public Dictionary<string, object> Capture()
        {
            return new Dictionary<string, object>
            {
                ["productName"] = PlayerSettings.productName,
                ["companyName"] = PlayerSettings.companyName,
                ["unityVersion"] = Application.unityVersion,
                ["dataPath"] = Application.dataPath,
                ["platform"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["hasUnityAgentRules"] = File.Exists("UNITY_AGENT.md")
            };
        }

        public string ReadProjectRules()
        {
            const string path = "UNITY_AGENT.md";
            if (!File.Exists(path)) return null;
            try
            {
                var text = File.ReadAllText(path);
                if (text.Length > 8000) text = text.Substring(0, 8000) + "\n…";
                return text;
            }
            catch
            {
                return null;
            }
        }
    }

    public sealed class ConsoleContext
    {
        public Dictionary<string, object> Capture(int maxEntries = 20)
        {
            var entries = ConsoleReader.GetEntries(includeWarnings: true, includeLogs: false, maxEntries: maxEntries);
            return new Dictionary<string, object>
            {
                ["errorCount"] = entries.Count(e => e.Type == "Error" || e.Type == "Exception" || e.Type == "Assert"),
                ["entries"] = entries.Select(e => e.ToDict()).Cast<object>().ToList()
            };
        }
    }

    public sealed class ContextManager
    {
        readonly SceneContext _scene = new SceneContext();
        readonly SelectionContext _selection = new SelectionContext();
        readonly ProjectContext _project = new ProjectContext();
        readonly ConsoleContext _console = new ConsoleContext();

        public string BuildMinimalContextJson()
        {
            var payload = new Dictionary<string, object>
            {
                ["project"] = _project.Capture(),
                ["scene"] = _scene.Capture(),
                ["selection"] = _selection.Capture(),
                ["console"] = _console.Capture()
            };

            var rules = _project.ReadProjectRules();
            if (!string.IsNullOrEmpty(rules))
                payload["projectRules"] = rules;

            return AgentJson.Serialize(payload);
        }

        public string BuildSystemPrompt(Agent.AgentMode mode, Tools.ToolRegistry registry)
        {
            var sb = new StringBuilder();
            sb.AppendLine("You are UnityAgent, an AI assistant that works inside the Unity Editor.");
            sb.AppendLine("You must ONLY interact with the project through the provided tools.");
            sb.AppendLine("Never invent Unity APIs. Prefer stable object ids returned by tools over names.");
            sb.AppendLine("Keep context minimal: inspect with tools only when needed.");
            sb.AppendLine();
            sb.AppendLine($"Current mode: {mode}");
            if (mode == Agent.AgentMode.Ask)
                sb.AppendLine("ASK mode: answer questions only. Do NOT call mutating tools.");
            else if (mode == Agent.AgentMode.Plan)
                sb.AppendLine("PLAN mode: inspect if needed and return a plan. Do NOT call mutating tools.");
            else
                sb.AppendLine("AGENT mode: you may call allowed tools to complete the task.");

            sb.AppendLine();
            sb.AppendLine("Response format: ALWAYS reply with a single JSON object.");
            sb.AppendLine("Tool call:");
            sb.AppendLine("{\"type\":\"tool_call\",\"thought\":\"...\",\"tool\":\"tool_name\",\"arguments\":{...}}");
            sb.AppendLine("Final answer:");
            sb.AppendLine("{\"type\":\"final\",\"message\":\"summary of what was done or the answer\"}");
            sb.AppendLine("Optional plan fields: \"goal\", \"plan\":[{\"title\":\"...\"}]");
            sb.AppendLine();
            sb.AppendLine("After create_script or patch_script, wait for compilation feedback from the system, then use read_console.");
            sb.AppendLine("If compilation errors appear, read_script then patch_script, then re-check console.");
            sb.AppendLine("When adding a newly created script component, use the class name as componentType after successful compile.");
            sb.AppendLine();
            sb.AppendLine("Available tools:");
            foreach (var tool in registry.All)
            {
                sb.AppendLine($"- {tool.Name} [{tool.RiskLevel}]: {tool.Description} params={tool.ParameterSchema}");
            }

            var rules = _project.ReadProjectRules();
            if (!string.IsNullOrEmpty(rules))
            {
                sb.AppendLine();
                sb.AppendLine("Project rules (UNITY_AGENT.md):");
                sb.AppendLine(rules);
            }

            return sb.ToString();
        }
    }
}
