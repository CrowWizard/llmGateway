---
name: noderuntime
description: Resolve and use the application-managed Node.js runtime for Node-based Codex skills, with the system Node.js installation as a fallback.
---

# Managed Node Runtime

Use this runtime for Node-based skills installed by LlmGateway Desktop.

## Resolution order

1. `$CODEX_NODE_PATH` when explicitly configured.
2. The managed Windows x64 executable under `$CODEX_HOME/skills/noderuntime/runtime/win-x64/`.
3. `node` from the system `PATH`.

Do not install Node.js, npm packages, or modify `PATH` from a business skill. Ask the user to run the desktop application's managed Node runtime action when no runtime is available.

## Managed paths

- Windows x64: `%USERPROFILE%\.codex\skills\noderuntime\runtime\win-x64\node.exe`

Shared packages are installed in this skill's `node_modules/`.

## Launching a script

Business skills must invoke Node scripts through:

```bat
"%USERPROFILE%\.codex\skills\noderuntime\scripts\run-node.cmd" "<absolute-script-path>" <arguments>
```

The launcher uses `CODEX_NODE_PATH`, managed Windows x64 Node.js, then system `node.exe`. It does not invoke PowerShell or modify the user's environment.