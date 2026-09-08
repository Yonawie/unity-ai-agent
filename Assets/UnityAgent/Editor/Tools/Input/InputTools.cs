using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityAgent.Editor.Agent;

namespace UnityAgent.Editor.Tools.InputTools
{
    public sealed class DetectInputSetupTool : IAgentTool
    {
        public string Name => "detect_input_setup";
        public string Description => "Detects whether Unity Input System package is installed and summarizes recommended input approach.";
        public string ParameterSchema => "{}";
        public RiskLevel RiskLevel => RiskLevel.Low;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var hasInputSystemAsm = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "Unity.InputSystem");

            var manifestPath = Path.Combine("Packages", "manifest.json");
            var manifestMentions = false;
            if (File.Exists(manifestPath))
            {
                var text = File.ReadAllText(manifestPath);
                manifestMentions = text.IndexOf("com.unity.inputsystem", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            var activeInput = "Unknown";
            try
            {
                // PlayerSettings.GetPropertyInt exists for activeInputHandler in modern Unity.
                activeInput = PlayerSettings.GetPropertyInt("activeInputHandler", BuildTargetGroup.Standalone) switch
                {
                    0 => "InputManager (Old)",
                    1 => "InputSystem (New)",
                    2 => "Both",
                    _ => "Unknown"
                };
            }
            catch
            {
                activeInput = "Unavailable";
            }

            string recommendation;
            if (hasInputSystemAsm || manifestMentions)
                recommendation = "Prefer Unity Input System APIs (UnityEngine.InputSystem). If generating movement scripts, use Keyboard.current / InputAction when possible, with legacy Input fallback only if asked.";
            else
                recommendation = "Input System package not detected. Use legacy UnityEngine.Input (GetAxis/GetKey) unless user asks to install Input System.";

            return ToolResult.Ok("Input setup detected.", new Dictionary<string, object>
            {
                ["inputSystemAssemblyPresent"] = hasInputSystemAsm,
                ["manifestMentionsInputSystem"] = manifestMentions,
                ["activeInputHandler"] = activeInput,
                ["recommendation"] = recommendation
            });
        }
    }

    public sealed class CreateWasdControllerScriptTool : IAgentTool
    {
        public string Name => "create_wasd_controller_script";
        public string Description => "Scaffolds a WASD (+ optional jump) movement script using CharacterController or Rigidbody. Uses Input System if available, else legacy Input.";
        public string ParameterSchema => "{\"path\":string,\"className\":string,\"useCharacterController\":boolean,\"includeJump\":boolean}";
        public RiskLevel RiskLevel => RiskLevel.Medium;

        public ToolResult Execute(Dictionary<string, object> arguments)
        {
            var className = ToolArgs.Str(arguments, "className", "PlayerController");
            var path = ToolArgs.Str(arguments, "path");
            if (string.IsNullOrWhiteSpace(path))
                path = $"Assets/Scripts/{className}.cs";
            path = path.Replace('\\', '/');
            if (!path.StartsWith("Assets/") || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                return ToolResult.Fail("path must be Assets/.../*.cs");

            if (File.Exists(path))
                return ToolResult.Fail($"File already exists: {path}. Use patch_script instead.");

            var useCc = ToolArgs.Bool(arguments, "useCharacterController", true);
            var includeJump = ToolArgs.Bool(arguments, "includeJump", true);
            var hasInputSystem = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "Unity.InputSystem");

            var content = useCc
                ? BuildCharacterControllerScript(className, includeJump, hasInputSystem)
                : BuildRigidbodyScript(className, includeJump, hasInputSystem);

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, content, Encoding.UTF8);
            AssetDatabase.ImportAsset(path);
            AssetDatabase.Refresh();

            return ToolResult.Ok($"WASD controller script created: {path}", new Dictionary<string, object>
            {
                ["path"] = path,
                ["className"] = className,
                ["usesInputSystem"] = hasInputSystem,
                ["needsCompile"] = true
            });
        }

        static string BuildCharacterControllerScript(string className, bool jump, bool inputSystem)
        {
            var usings = inputSystem
                ? "using UnityEngine;\nusing UnityEngine.InputSystem;\n"
                : "using UnityEngine;\n";

            var readMove = inputSystem
                ? @"        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        var x = 0f; var z = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) z += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) z -= 1f;
        var jumpPressed = keyboard.spaceKey.wasPressedThisFrame;"
                : @"        var x = Input.GetAxisRaw(""Horizontal"");
        var z = Input.GetAxisRaw(""Vertical"");
        var jumpPressed = Input.GetButtonDown(""Jump"");";

            var jumpBlock = jump
                ? @"
        if (_controller.isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;
        if (jumpPressed && _controller.isGrounded)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        _velocity.y += gravity * Time.deltaTime;
        _controller.Move(_velocity * Time.deltaTime);"
                : "";

            return
$@"{usings}
public class {className} : MonoBehaviour
{{
    [SerializeField] float moveSpeed = 6f;
    [SerializeField] float gravity = -9.81f;
    [SerializeField] float jumpHeight = 1.2f;

    CharacterController _controller;
    Vector3 _velocity;

    void Awake()
    {{
        _controller = GetComponent<CharacterController>();
        if (_controller == null)
            _controller = gameObject.AddComponent<CharacterController>();
    }}

    void Update()
    {{
{readMove}
        var move = transform.right * x + transform.forward * z;
        _controller.Move(move.normalized * moveSpeed * Time.deltaTime);
{jumpBlock}
    }}
}}
";
        }

        static string BuildRigidbodyScript(string className, bool jump, bool inputSystem)
        {
            var usings = inputSystem
                ? "using UnityEngine;\nusing UnityEngine.InputSystem;\n"
                : "using UnityEngine;\n";

            var readMove = inputSystem
                ? @"        var keyboard = Keyboard.current;
        if (keyboard == null) return;
        var x = 0f; var z = 0f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) z += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) z -= 1f;
        var jumpPressed = keyboard.spaceKey.wasPressedThisFrame;"
                : @"        var x = Input.GetAxisRaw(""Horizontal"");
        var z = Input.GetAxisRaw(""Vertical"");
        var jumpPressed = Input.GetButtonDown(""Jump"");";

            var jumpBlock = jump
                ? @"
        if (jumpPressed)
            _rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);"
                : "";

            return
$@"{usings}
[RequireComponent(typeof(Rigidbody))]
public class {className} : MonoBehaviour
{{
    [SerializeField] float moveSpeed = 6f;
    [SerializeField] float jumpForce = 5f;

    Rigidbody _rb;

    void Awake() => _rb = GetComponent<Rigidbody>();

    void FixedUpdate()
    {{
{readMove}
        var move = new Vector3(x, 0f, z).normalized;
        var target = _rb.position + move * moveSpeed * Time.fixedDeltaTime;
        _rb.MovePosition(target);
{jumpBlock}
    }}
}}
";
        }
    }
}
