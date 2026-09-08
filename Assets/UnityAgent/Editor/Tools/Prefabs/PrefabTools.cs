using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.Prefabs
{
    public sealed class CreatePrefabTool : IAgentTool
    {
        public string Name => "create_prefab";
        public string Description => "Creates a prefab asset from an existing scene GameObject. path must be under Assets/ and end with .prefab.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"path\":string,\"replaceOriginal\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var path = ToolArgs.Str(arguments, "path");
            if (string.IsNullOrWhiteSpace(path))
                path = $"Assets/Prefabs/{go.name}.prefab";
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                return ToolResult.Fail("path must start with Assets/");
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                path += ".prefab";

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var replace = ToolArgs.Bool(arguments, "replaceOriginal", false);
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction, out var success);
            if (!success || prefab == null)
            {
                // Fallback without connect
                prefab = PrefabUtility.SaveAsPrefabAsset(go, path, out success);
            }

            if (!success || prefab == null)
                return ToolResult.Fail($"Failed to create prefab at {path}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!replace && PrefabUtility.IsPartOfPrefabInstance(go))
            {
                // keep instance connected — already done via SaveAsPrefabAssetAndConnect when possible
            }

            return ToolResult.Ok($"Prefab created: {path}", new Dictionary<string, object>
            {
                ["path"] = path,
                ["guid"] = AssetDatabase.AssetPathToGUID(path),
                ["sourceId"] = ObjectIdUtil.GetId(go),
                ["sourceName"] = go.name
            });
        }
    }

    public sealed class InstantiatePrefabTool : IAgentTool
    {
        public string Name => "instantiate_prefab";
        public string Description => "Instantiates a prefab asset into the active scene.";
        public string ParameterSchema => "{\"path\":string,\"name\":string,\"position\":[x,y,z],\"parentId\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = ToolArgs.Str(arguments, "path")?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("Assets/"))
                return ToolResult.Fail("path must be an Assets/*.prefab asset.");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return ToolResult.Fail($"Prefab not found: {path}");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
                return ToolResult.Fail("InstantiatePrefab failed.");

            Undo.RegisterCreatedObjectUndo(instance, "UnityAgent Instantiate Prefab");
            var name = ToolArgs.Str(arguments, "name");
            if (!string.IsNullOrWhiteSpace(name))
                instance.name = name;

            if (arguments.ContainsKey("position"))
                instance.transform.position = ToolArgs.Vec3(arguments, "position", Vector3.zero);

            var parentId = ToolArgs.Str(arguments, "parentId");
            if (!string.IsNullOrEmpty(parentId) && ObjectIdUtil.TryResolve(parentId, out var parent))
                Undo.SetTransformParent(instance.transform, parent.transform, "UnityAgent Prefab Parent");

            Selection.activeGameObject = instance;
            return ToolResult.Ok($"Instantiated prefab '{path}'.", ObjectIdUtil.Describe(instance));
        }
    }

    public sealed class UnpackPrefabTool : IAgentTool
    {
        public string Name => "unpack_prefab";
        public string Description => "Unpacks a prefab instance in the scene (Completely or Outermost).";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"completely\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ToolResult.Fail("Object is not a prefab instance.");

            var mode = ToolArgs.Bool(arguments, "completely", true)
                ? PrefabUnpackMode.Completely
                : PrefabUnpackMode.OutermostRoot;

            Undo.RegisterFullObjectHierarchyUndo(go, "UnityAgent Unpack Prefab");
            PrefabUtility.UnpackPrefabInstance(go, mode, InteractionMode.UserAction);
            return ToolResult.Ok("Prefab unpacked.", ObjectIdUtil.Describe(go));
        }
    }

    public sealed class GetPrefabInfoTool : IAgentTool
    {
        public string Name => "get_prefab_info";
        public string Description => "Returns prefab connection info for a scene object or asset path.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"path\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = ToolArgs.Str(arguments, "path");
            if (!string.IsNullOrEmpty(path))
            {
                path = path.Replace('\\', '/');
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) return ToolResult.Fail($"Prefab asset not found: {path}");
                return ToolResult.Ok("Prefab asset info.", new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["guid"] = AssetDatabase.AssetPathToGUID(path),
                    ["name"] = asset.name,
                    ["isPrefabAsset"] = true
                });
            }

            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            return ToolResult.Ok("Prefab instance info.", new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(go),
                ["name"] = go.name,
                ["isPrefabInstance"] = PrefabUtility.IsPartOfPrefabInstance(go),
                ["isPrefabAsset"] = PrefabUtility.IsPartOfPrefabAsset(go),
                ["assetPath"] = assetPath,
                ["status"] = PrefabUtility.GetPrefabInstanceStatus(go).ToString()
            });
        }
    }
}
