# Unity AI Agent (Editor)

In-editor AI assistant for **Unity 6+**. Open **Window → AI Agent**, send a text command, and the agent plans + executes **only** through registered Unity tools (no arbitrary shell/OS execution).

## Requirements

- Unity **6000.0+** (Unity 6)
- UI Toolkit (built-in)
- [Ollama](https://ollama.com) for the default provider (or any OpenAI-compatible endpoint)

## Install

1. Copy the `Assets/UnityAgent` folder into your Unity project’s `Assets/` directory.
2. Wait for Unity to compile `UnityAgent.Editor`.
3. Open **Window → AI Agent**.

Optional project rules file in the Unity project root:

```text
UNITY_AGENT.md
```

## Ollama setup

1. Install Ollama from https://ollama.com
2. Pull a model, for example:

```bash
ollama pull qwen2.5-coder:7b
```

3. Ensure the daemon is running (`ollama serve` if needed). Default URL: `http://localhost:11434`
4. In **Window → AI Agent** (or **Edit → Project Settings → AI Agent**):
   - Provider: `Ollama`
   - Base URL: `http://localhost:11434`
   - Model: exact name from `ollama list` (not hard-coded)
5. Click **Test Connection**.

## Modes

| Mode | Behavior |
|------|----------|
| **ASK** | Analyze / answer only. No mutating tools. |
| **PLAN** | Inspect + produce a plan. No mutations. |
| **AGENT** | Full tool loop with safety checks. |

## Example prompts

```text
Создай Cube с именем TestCube в позиции 0,1,0.
```

```text
Добавь Rigidbody к TestCube.
```

```text
Создай красный материал, назначь его TestCube и сохрани TestCube как Prefab.
```

```text
Определи Input System и создай WASD-контроллер, затем повесь его на Player.
```

```text
Создай скрипт RotateObject.cs, который вращает объект, добавь его к TestCube и проверь Console.
```

```text
Создай Cube, назови его PlayerCube, добавь Rigidbody и создай скрипт, который позволяет двигать его клавишами WASD.
```

## New capabilities (v0.2)

- Prefabs: `create_prefab`, `instantiate_prefab`, `unpack_prefab`, `get_prefab_info`
- Materials: `create_material`, `assign_material`, `set_material_color`
- Selection: `get_selection`, `set_selection`, `focus_object`, `set_tag`, `set_layer`
- Input helpers: `detect_input_setup`, `create_wasd_controller_script`
- Script review: `preview_script_patch` + optional **Require Script Diff Approval** in Project Settings
- Plan step status updates in the UI
- Changes panel (Created / Modified / Deleted)
## Architecture

```text
Assets/UnityAgent/Editor/
  UI/           AgentWindow (UI Toolkit)
  Agent/        AgentController, AgentLoop, plan/session state, compilation monitor
  Context/      Minimal project/scene/selection/console context
  Tools/        IAgentTool + registry/dispatcher + Unity tools
  LLM/          ILLMProvider, OllamaProvider, OpenAICompatibleProvider, tool-call parser
  Safety/       Risk validation, permissions, ChangeTracker/Undo/backups
  Settings/     Project Settings provider
  Persistence/  Session restore across Domain Reload
  Logging/      Logs/UnityAgent/*.log (secrets scrubbed)
```

Flow:

`User → Context → LLM → tool_call JSON → Validate → Execute Unity API → tool result → LLM → … → final`

## Safety

- No shell / PowerShell / cmd / bash tools
- Paths limited to the Unity project (`Assets/…`)
- Risk levels: Low / Medium / High (High requires confirmation)
- Permissions toggles in Project Settings
- Scene changes go through Unity `Undo` where possible
- Script edits create backups under `Library/UnityAgent/Backups/`

## Add a new Tool

1. Implement `IAgentTool` under `Editor/Tools/...`
2. Register it in `ToolBootstrap.CreateDefaultRegistry()`
3. Document parameters in `ParameterSchema`
4. Return `ToolResult.Ok/Fail` with structured `data` (include stable object `id`s)

## Add a new LLM Provider

1. Implement `ILLMProvider`
2. Register selection in `LLMProviderFactory.CreateFromSettings()`
3. Keep Agent Core free of provider-specific HTTP details

### OpenAI-compatible

- Set Provider to `OpenAICompatible` (or `OpenAI` / `LMStudio`)
- Set Base URL (e.g. `http://localhost:1234/v1` for LM Studio)
- Set Model
- Optional API key via env var `UNITY_AGENT_API_KEY` (never written to logs)

## Smoke tests (no LLM)

- **Window → AI Agent → Smoke Test → Create TestCube**
- **Add Rigidbody To TestCube**
- **Dump Hierarchy**

## Limitations

- Domain Reload aborts in-flight `async` work; session is persisted and resumed when safe
- Console reading uses UnityEditor reflection (`LogEntries`) and may differ slightly by Unity version
- LLM quality depends entirely on the local/remote model
- Play Mode automation is limited; prefer compile + console checks for MVP validation
- Do not commit API keys; use env vars

## Logs

- `Logs/UnityAgent/agent-YYYYMMDD.log`
- Session snapshot: `Library/UnityAgent/session.json`
