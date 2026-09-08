# Unity Agent Project Rules

Use these instructions when planning and modifying this project.

## General

- Prefer Unity 6 APIs.
- Do not modify files under `Packages/` or third-party vendor folders.
- Keep new gameplay scripts under `Assets/Scripts/` unless a more specific folder already exists.
- Use `[SerializeField] private` fields for Inspector references instead of public fields when practical.
- Prefer `CharacterController` or Rigidbody consistently — do not mix conflicting movement systems on the same object.

## Input

- Prefer the **Unity Input System** package when it is installed.
- Otherwise fall back to `Input.GetAxis` / `Input.GetKey` and note that in the final report.

## Scripts

- One public MonoBehaviour per file; class name must match file name.
- After creating or patching scripts, wait for compilation and check the Console before adding components.

## Safety

- Never delete assets outside `Assets/`.
- Ask before deleting scenes or large folder trees.
- Prefer `patch_script` over rewriting entire files.
