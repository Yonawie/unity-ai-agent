using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.Materials
{
    public sealed class CreateMaterialTool : IAgentTool
    {
        public string Name => "create_material";
        public string Description => "Creates a Material asset. Optional shader (default Universal Render Pipeline/Lit or Standard fallback), color RGBA 0-1.";
        public string ParameterSchema => "{\"path\":string,\"shader\":string,\"color\":[r,g,b,a],\"name\":string}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = ToolArgs.Str(arguments, "path");
            var name = ToolArgs.Str(arguments, "name", "NewMaterial");
            if (string.IsNullOrWhiteSpace(path))
                path = $"Assets/Materials/{name}.mat";
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                return ToolResult.Fail("path must start with Assets/");
            if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                path += ".mat";

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var shaderName = ToolArgs.Str(arguments, "shader");
            var shader = FindShader(shaderName);
            if (shader == null)
                return ToolResult.Fail($"Shader not found: {shaderName ?? "(default)"}");

            var mat = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            if (arguments.ContainsKey("color"))
            {
                var c = ToolArgs.Vec3(arguments, "color", new Vector3(1, 1, 1));
                var a = 1f;
                if (arguments["color"] is List<object> list && list.Count >= 4)
                    a = Convert.ToSingle(list[3]);
                var color = new Color(c.x, c.y, c.z, a);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                mat.color = color;
            }

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return ToolResult.Ok($"Material created: {path}", new Dictionary<string, object>
            {
                ["path"] = path,
                ["guid"] = AssetDatabase.AssetPathToGUID(path),
                ["shader"] = shader.name
            });
        }

        public static Shader FindShader(string preferred)
        {
            if (!string.IsNullOrWhiteSpace(preferred))
            {
                var s = Shader.Find(preferred);
                if (s != null) return s;
            }

            foreach (var candidate in new[]
                     {
                         "Universal Render Pipeline/Lit",
                         "HDRP/Lit",
                         "Standard",
                         "Unlit/Color",
                         "Sprites/Default"
                     })
            {
                var s = Shader.Find(candidate);
                if (s != null) return s;
            }

            return Shader.Find("Hidden/InternalErrorShader");
        }
    }

    public sealed class AssignMaterialTool : IAgentTool
    {
        public string Name => "assign_material";
        public string Description => "Assigns a material asset to a Renderer on a GameObject (optional materialIndex).";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"materialPath\":string,\"materialIndex\":number}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var matPath = ToolArgs.Str(arguments, "materialPath")?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(matPath))
                return ToolResult.Fail("materialPath is required.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
                return ToolResult.Fail($"Material not found: {matPath}");

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return ToolResult.Fail("GameObject has no Renderer.");

            var index = ToolArgs.Int(arguments, "materialIndex", 0);
            Undo.RecordObject(renderer, "UnityAgent Assign Material");
            var mats = renderer.sharedMaterials;
            if (mats == null || mats.Length == 0)
            {
                renderer.sharedMaterial = mat;
            }
            else
            {
                if (index < 0 || index >= mats.Length)
                    return ToolResult.Fail($"materialIndex out of range (0..{mats.Length - 1}).");
                mats[index] = mat;
                renderer.sharedMaterials = mats;
            }

            return ToolResult.Ok($"Assigned material {matPath}.", new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(go),
                ["materialPath"] = matPath,
                ["materialIndex"] = index
            });
        }
    }

    public sealed class SetMaterialColorTool : IAgentTool
    {
        public string Name => "set_material_color";
        public string Description => "Sets color on a material asset (_BaseColor/_Color).";
        public string ParameterSchema => "{\"path\":string,\"color\":[r,g,b,a]}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var path = ToolArgs.Str(arguments, "path")?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(path))
                return ToolResult.Fail("path is required.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
                return ToolResult.Fail($"Material not found: {path}");

            var c = ToolArgs.Vec3(arguments, "color", new Vector3(1, 1, 1));
            var a = 1f;
            if (arguments.TryGetValue("color", out var raw) && raw is List<object> list && list.Count >= 4)
                a = Convert.ToSingle(list[3]);
            var color = new Color(c.x, c.y, c.z, a);

            Undo.RecordObject(mat, "UnityAgent Set Material Color");
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            mat.color = color;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            return ToolResult.Ok("Material color updated.", new Dictionary<string, object>
            {
                ["path"] = path,
                ["color"] = new List<object> { color.r, color.g, color.b, color.a }
            });
        }
    }
}
