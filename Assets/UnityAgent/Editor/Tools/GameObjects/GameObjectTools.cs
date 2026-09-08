using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.GameObjects
{
    public sealed class CreateGameObjectTool : IAgentTool
    {
        public string Name => "create_game_object";
        public string Description => "Creates a GameObject. Optional primitiveType: Empty, Cube, Sphere, Capsule, Cylinder, Plane, Quad.";
        public string ParameterSchema => "{\"name\":string,\"primitiveType\":string,\"position\":[x,y,z],\"parentId\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var name = ToolArgs.Str(arguments, "name", "GameObject");
            var primitive = ToolArgs.Str(arguments, "primitiveType", "Empty");
            var position = ToolArgs.Vec3(arguments, "position", Vector3.zero);

            GameObject go;
            if (string.Equals(primitive, "Empty", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(primitive))
            {
                go = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(go, "UnityAgent Create GameObject");
            }
            else if (Enum.TryParse(primitive, true, out PrimitiveType ptype))
            {
                go = GameObject.CreatePrimitive(ptype);
                go.name = name;
                Undo.RegisterCreatedObjectUndo(go, "UnityAgent Create Primitive");
            }
            else
            {
                return ToolResult.Fail($"Unknown primitiveType '{primitive}'. Use Empty/Cube/Sphere/Capsule/Cylinder/Plane/Quad.");
            }

            go.transform.position = position;

            var parentId = ToolArgs.Str(arguments, "parentId");
            if (!string.IsNullOrEmpty(parentId) && ObjectIdUtil.TryResolve(parentId, out var parent))
                Undo.SetTransformParent(go.transform, parent.transform, "UnityAgent Set Parent");

            Selection.activeGameObject = go;
            return ToolResult.Ok($"Created '{go.name}'.", ObjectIdUtil.Describe(go));
        }
    }

    public sealed class DeleteGameObjectTool : IAgentTool
    {
        public string Name => "delete_game_object";
        public string Description => "Deletes a GameObject by stable id (preferred) or unique name/path.";
        public string ParameterSchema => "{\"id\":string,\"name\":string}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var desc = ObjectIdUtil.Describe(go);
            Undo.DestroyObjectImmediate(go);
            return ToolResult.Ok("GameObject deleted.", desc);
        }
    }

    public sealed class RenameGameObjectTool : IAgentTool
    {
        public string Name => "rename_game_object";
        public string Description => "Renames a GameObject.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"newName\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var newName = ToolArgs.Str(arguments, "newName");
            if (string.IsNullOrWhiteSpace(newName))
                return ToolResult.Fail("newName is required.");

            Undo.RecordObject(go, "UnityAgent Rename");
            go.name = newName;
            return ToolResult.Ok($"Renamed to '{newName}'.", ObjectIdUtil.Describe(go));
        }
    }

    public sealed class DuplicateGameObjectTool : IAgentTool
    {
        public string Name => "duplicate_game_object";
        public string Description => "Duplicates a GameObject.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"newName\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var clone = UnityEngine.Object.Instantiate(go);
            clone.name = ToolArgs.Str(arguments, "newName", go.name + "_Copy");
            clone.transform.SetParent(go.transform.parent, true);
            Undo.RegisterCreatedObjectUndo(clone, "UnityAgent Duplicate");
            return ToolResult.Ok($"Duplicated '{go.name}'.", ObjectIdUtil.Describe(clone));
        }
    }

    public sealed class SetTransformTool : IAgentTool
    {
        public string Name => "set_transform";
        public string Description => "Sets position/rotation/scale of a GameObject. Vectors are world-space for position/rotation.";
        public string ParameterSchema => "{\"id\":string,\"position\":[x,y,z],\"rotation\":[x,y,z],\"scale\":[x,y,z]}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            Undo.RecordObject(go.transform, "UnityAgent Set Transform");
            if (arguments.ContainsKey("position"))
                go.transform.position = ToolArgs.Vec3(arguments, "position", go.transform.position);
            if (arguments.ContainsKey("rotation"))
                go.transform.eulerAngles = ToolArgs.Vec3(arguments, "rotation", go.transform.eulerAngles);
            if (arguments.ContainsKey("scale"))
                go.transform.localScale = ToolArgs.Vec3(arguments, "scale", go.transform.localScale);

            return ToolResult.Ok("Transform updated.", ObjectIdUtil.Describe(go));
        }
    }

    public sealed class SetParentTool : IAgentTool
    {
        public string Name => "set_parent";
        public string Description => "Parents a GameObject under another (or unparents if parentId is null/empty).";
        public string ParameterSchema => "{\"id\":string,\"parentId\":string,\"worldPositionStays\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            Transform parent = null;
            var parentId = ToolArgs.Str(arguments, "parentId");
            if (!string.IsNullOrEmpty(parentId))
            {
                if (!ObjectIdUtil.TryResolve(parentId, out var parentGo))
                    return ToolResult.Fail($"Parent not found: {parentId}");
                parent = parentGo.transform;
            }

            var stays = ToolArgs.Bool(arguments, "worldPositionStays", true);
            Undo.SetTransformParent(go.transform, parent, "UnityAgent Set Parent");
            if (!stays && parent != null)
                go.transform.localPosition = Vector3.zero;

            return ToolResult.Ok("Parent updated.", ObjectIdUtil.Describe(go));
        }
    }

    public sealed class GetComponentsTool : IAgentTool
    {
        public string Name => "get_components";
        public string Description => "Lists components on a GameObject.";
        public string ParameterSchema => "{\"id\":string,\"name\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var comps = go.GetComponents<Component>()
                .Where(c => c != null)
                .Select(c => (object)new Dictionary<string, object>
                {
                    ["type"] = c.GetType().FullName,
                    ["name"] = c.GetType().Name
                })
                .ToList();

            return ToolResult.Ok($"Components: {comps.Count}", new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(go),
                ["name"] = go.name,
                ["components"] = comps
            });
        }
    }

    public sealed class AddComponentTool : IAgentTool
    {
        public string Name => "add_component";
        public string Description => "Adds a component by type name (e.g. Rigidbody, CharacterController, or full script type name).";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"componentType\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var typeName = ToolArgs.Str(arguments, "componentType");
            if (string.IsNullOrWhiteSpace(typeName))
                return ToolResult.Fail("componentType is required.");

            var type = FindComponentType(typeName);
            if (type == null)
                return ToolResult.Fail($"Component type not found: {typeName}. Wait for compilation if you just created a script.");

            var existing = go.GetComponent(type);
            if (existing != null)
                return ToolResult.Ok("Component already present.", ObjectIdUtil.Describe(go));

            var added = Undo.AddComponent(go, type);
            if (added == null)
                return ToolResult.Fail($"Failed to add component {typeName}.");

            return ToolResult.Ok($"Added {type.Name}.", new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(go),
                ["name"] = go.name,
                ["componentType"] = type.FullName
            });
        }

        public static Type FindComponentType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try
                {
                    type = assembly.GetType(typeName, false, true);
                    if (type == null)
                    {
                        foreach (var t in assembly.GetTypes())
                        {
                            if (t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                                typeof(Component).IsAssignableFrom(t))
                            {
                                type = t;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Dynamic assemblies may throw on GetTypes.
                }

                if (type != null && typeof(Component).IsAssignableFrom(type))
                    return type;
            }

            return null;
        }
    }

    public sealed class RemoveComponentTool : IAgentTool
    {
        public string Name => "remove_component";
        public string Description => "Removes a component by type name from a GameObject.";
        public string ParameterSchema => "{\"id\":string,\"name\":string,\"componentType\":string}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            if (!ToolArgs.TryResolveGameObject(arguments, out var go, out var error))
                return ToolResult.Fail(error);

            var typeName = ToolArgs.Str(arguments, "componentType");
            var type = AddComponentTool.FindComponentType(typeName);
            if (type == null)
                return ToolResult.Fail($"Component type not found: {typeName}");

            var comp = go.GetComponent(type);
            if (comp == null)
                return ToolResult.Fail($"Component {typeName} not present on object.");

            if (comp is Transform)
                return ToolResult.Fail("Cannot remove Transform.");

            Undo.DestroyObjectImmediate(comp);
            return ToolResult.Ok($"Removed {type.Name}.", ObjectIdUtil.Describe(go));
        }
    }
}
