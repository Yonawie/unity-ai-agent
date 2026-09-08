using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Tools;
using UnityAgent.Editor.Tools.GameObjects;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor
{
    /// <summary>
    /// Manual smoke tests for tools without LLM (PHASE 3 validation).
    /// </summary>
    public static class AgentSmokeTests
    {
        [MenuItem("Window/AI Agent/Smoke Test/Create TestCube")]
        public static void CreateTestCube()
        {
            var registry = ToolBootstrap.CreateDefaultRegistry();
            registry.TryGet("create_game_object", out var tool);
            var result = tool.Execute(new Dictionary<string, object>
            {
                ["name"] = "TestCube",
                ["primitiveType"] = "Cube",
                ["position"] = new List<object> { 0, 1, 0 }
            });
            Debug.Log(result.Success
                ? $"[UnityAgent] Smoke OK: {result.Message} id={ObjectIdUtil.GetId(GameObject.Find("TestCube"))}"
                : $"[UnityAgent] Smoke FAIL: {result.Error}");
        }

        [MenuItem("Window/AI Agent/Smoke Test/Add Rigidbody To TestCube")]
        public static void AddRigidbody()
        {
            var go = GameObject.Find("TestCube");
            if (go == null)
            {
                Debug.LogError("[UnityAgent] TestCube not found. Run Create TestCube first.");
                return;
            }

            var registry = ToolBootstrap.CreateDefaultRegistry();
            registry.TryGet("add_component", out var tool);
            var result = tool.Execute(new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(go),
                ["componentType"] = "Rigidbody"
            });
            Debug.Log(result.Success ? "[UnityAgent] Rigidbody added." : result.Error);
        }

        [MenuItem("Window/AI Agent/Smoke Test/Dump Hierarchy")]
        public static void DumpHierarchy()
        {
            var registry = ToolBootstrap.CreateDefaultRegistry();
            registry.TryGet("get_scene_hierarchy", out var tool);
            var result = tool.Execute(new Dictionary<string, object>());
            Debug.Log(result.Success
                ? result.Message + "\n" + UnityAgent.Editor.Util.AgentJson.Serialize(result.Data)
                : result.Error);
        }
    }
}
