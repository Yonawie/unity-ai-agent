using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;
using UnityAgent.Editor.Util;

namespace UnityAgent.Editor.Tools.CameraTools
{
    public sealed class SetupMainCameraTool : IAgentTool
    {
        public string Name => "setup_main_camera";
        public string Description => "Configures the Main Camera position/rotation/FOV. Optionally look at a target object.";
        public string ParameterSchema => "{\"position\":[x,y,z],\"rotation\":[x,y,z],\"fieldOfView\":number,\"lookAtId\":string,\"lookAtName\":string}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera");
                Undo.RegisterCreatedObjectUndo(go, "UnityAgent Main Camera");
                cam = go.AddComponent<Camera>();
                go.tag = "MainCamera";
                go.AddComponent<AudioListener>();
            }

            Undo.RecordObject(cam.transform, "UnityAgent Setup Camera");
            Undo.RecordObject(cam, "UnityAgent Setup Camera FOV");

            if (arguments.ContainsKey("position"))
                cam.transform.position = ToolArgs.Vec3(arguments, "position", cam.transform.position);
            if (arguments.ContainsKey("rotation"))
                cam.transform.eulerAngles = ToolArgs.Vec3(arguments, "rotation", cam.transform.eulerAngles);
            if (arguments.ContainsKey("fieldOfView"))
                cam.fieldOfView = ToolArgs.Float(arguments, "fieldOfView", cam.fieldOfView);

            GameObject lookTarget = null;
            var lookAtId = ToolArgs.Str(arguments, "lookAtId");
            var lookAtName = ToolArgs.Str(arguments, "lookAtName");
            if (!string.IsNullOrEmpty(lookAtId))
                ObjectIdUtil.TryResolve(lookAtId, out lookTarget);
            if (lookTarget == null && !string.IsNullOrEmpty(lookAtName))
                lookTarget = ObjectIdUtil.FindByPathOrName(lookAtName);
            if (lookTarget != null)
                cam.transform.LookAt(lookTarget.transform);

            return ToolResult.Ok("Main Camera configured.", new Dictionary<string, object>
            {
                ["id"] = ObjectIdUtil.GetId(cam.gameObject),
                ["name"] = cam.gameObject.name,
                ["position"] = new List<object> { cam.transform.position.x, cam.transform.position.y, cam.transform.position.z },
                ["rotation"] = new List<object> { cam.transform.eulerAngles.x, cam.transform.eulerAngles.y, cam.transform.eulerAngles.z },
                ["fieldOfView"] = cam.fieldOfView
            });
        }
    }

    public sealed class CreateThirdPersonCameraTool : IAgentTool
    {
        public string Name => "create_third_person_camera";
        public string Description => "Positions Main Camera behind a target for a simple third-person view and optionally parents/follows via a lightweight follow script scaffold note. Does not require Cinemachine.";
        public string ParameterSchema => "{\"targetId\":string,\"targetName\":string,\"distance\":number,\"height\":number,\"createFollowScript\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            GameObject target = null;
            if (!ToolArgs.TryResolveGameObject(new Dictionary<string, object>
                {
                    ["id"] = ToolArgs.Str(arguments, "targetId"),
                    ["name"] = ToolArgs.Str(arguments, "targetName")
                }, out target, out var error))
            {
                // Try name-only convenience
                target = ObjectIdUtil.FindByPathOrName(ToolArgs.Str(arguments, "targetName"));
                if (target == null)
                    return ToolResult.Fail(error ?? "targetId or targetName required.");
            }

            var distance = ToolArgs.Float(arguments, "distance", 5f);
            var height = ToolArgs.Float(arguments, "height", 2f);

            var camSetup = new SetupMainCameraTool().Execute(new Dictionary<string, object>
            {
                ["position"] = new List<object>
                {
                    target.transform.position.x,
                    target.transform.position.y + height,
                    target.transform.position.z - distance
                },
                ["lookAtId"] = ObjectIdUtil.GetId(target)
            });

            var followCreated = false;
            string followPath = null;
            if (ToolArgs.Bool(arguments, "createFollowScript", true))
            {
                followPath = "Assets/Scripts/SimpleCameraFollow.cs";
                if (!System.IO.File.Exists(followPath))
                {
                    var dir = System.IO.Path.GetDirectoryName(followPath);
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    System.IO.File.WriteAllText(followPath,
@"using UnityEngine;

public class SimpleCameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Vector3 offset = new Vector3(0f, 2f, -5f);
    [SerializeField] float smooth = 10f;

    public void SetTarget(Transform t) => target = t;

    void LateUpdate()
    {
        if (target == null) return;
        var desired = target.position + target.TransformDirection(offset);
        transform.position = Vector3.Lerp(transform.position, desired, Time.deltaTime * smooth);
        transform.LookAt(target.position + Vector3.up * 1.5f);
    }
}
");
                    AssetDatabase.ImportAsset(followPath);
                    AssetDatabase.Refresh();
                    followCreated = true;
                }
            }

            return ToolResult.Ok("Third-person camera set up.", new Dictionary<string, object>
            {
                ["camera"] = camSetup.Data,
                ["targetId"] = ObjectIdUtil.GetId(target),
                ["followScriptPath"] = followPath,
                ["followScriptCreated"] = followCreated,
                ["needsCompile"] = followCreated,
                ["note"] = followCreated
                    ? "After compilation, add_component SimpleCameraFollow on Main Camera and assign target."
                    : "Follow script already exists or was skipped."
            });
        }
    }

    public sealed class CreateLightTool : IAgentTool
    {
        public string Name => "create_light";
        public string Description => "Creates a Light (Directional/Point/Spot). Useful for basic scene lighting.";
        public string ParameterSchema => "{\"name\":string,\"type\":string,\"position\":[x,y,z],\"rotation\":[x,y,z],\"intensity\":number,\"color\":[r,g,b]}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var name = ToolArgs.Str(arguments, "name", "Directional Light");
            var typeName = ToolArgs.Str(arguments, "type", "Directional");
            if (!Enum.TryParse(typeName, true, out LightType lightType) ||
                (lightType != LightType.Directional && lightType != LightType.Point && lightType != LightType.Spot))
                return ToolResult.Fail("type must be Directional, Point, or Spot.");

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "UnityAgent Create Light");
            var light = go.AddComponent<Light>();
            light.type = lightType;
            light.intensity = ToolArgs.Float(arguments, "intensity", lightType == LightType.Directional ? 1f : 3f);

            if (arguments.ContainsKey("color"))
            {
                var c = ToolArgs.Vec3(arguments, "color", new Vector3(1, 1, 1));
                light.color = new Color(c.x, c.y, c.z, 1f);
            }

            if (arguments.ContainsKey("position"))
                go.transform.position = ToolArgs.Vec3(arguments, "position", Vector3.zero);
            if (arguments.ContainsKey("rotation"))
                go.transform.eulerAngles = ToolArgs.Vec3(arguments, "rotation", new Vector3(50, -30, 0));
            else if (lightType == LightType.Directional)
                go.transform.eulerAngles = new Vector3(50, -30, 0);

            Selection.activeGameObject = go;
            return ToolResult.Ok($"Light '{name}' created.", ObjectIdUtil.Describe(go));
        }
    }
}
