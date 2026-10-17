# BIM-agent instructions for coding agents

This repository connects AI coding agents to AEC and 3D software. Treat it as a connector and workflow toolkit, not as proof that every application operation is implemented.

## First five actions

1. Read `README.md`, `capabilities.json`, `docs/PRO_WORKFLOWS.md`, the target tool reference, and `docs/REVIT_WORKFLOWS.md` when modeling in Revit.
2. On Windows, run `./scripts/doctor.ps1`. Identify which host applications are installed and which connector builds are available. Ask the human to open the target application and a **disposable or backed-up** project before the first write.
3. Connect the relevant local MCP server. Use `mcp/server.mjs` for Revit, `mcp/autocad.mjs` for AutoCAD, and `mcp/sketchup.mjs` for SketchUp. Do not assume a generic chat subscription exposes local MCP tools.
   - In Expert OS, configure its stdio MCP client as shown in `docs/EXPERT_AI.md`.
   - In Expert AI Terminal, Terminal Pro, or AI Agent, use `node scripts/bim-agent-cli.mjs <host> list` and call tools through the shell bridge when a native MCP client is unavailable.
4. Call the application's status and read tools. Record the active file, units, levels/layers/tags, origin, existing types/components, and stable IDs. Never invent IDs or silently guess units.
5. Propose a specific modeling plan with dimensions, element types, scope and validation checks. Execute small batches, approve in the host application, read back every result, and inspect the model visually.

## Professional modeling discipline

- Revit: respect levels, hosts, families, phases, design options and worksets. Create typed BIM elements when possible. A generic 3D shape is not a substitute for a wall, door or room.
- AutoCAD: check `INSUNITS` before generating geometry. Coordinates in the current tools are native drawing units. Use layers deliberately; keep 2D linework and 3D solids distinct.
- SketchUp: coordinates in the current extrusion tool are millimetres. Keep generated geometry grouped, named and tagged; verify face orientation and solids in SketchUp.
- Across applications: keep a project coordinate strategy, named object conventions, geometry tolerances, material intent, deliverable views, and review checkpoints. Document assumptions and unresolved design decisions.
- Revit workflows: inspect types and family availability first; use typed elements; place hosted families against verified host IDs; build views, tags, schedules and sheets from project standards; query model warnings after each major phase. Follow the task's source dimensions, and ask for missing design constraints before inventing critical dimensions.
- Never claim a code compile proves host runtime behavior. If a target app is absent, report a packaged but untested adapter.

## Extending a host adapter

Add a narrow typed operation with bounded inputs, validation, approval for writes, host-native undo/transaction behavior, a structured result, documentation and a host smoke test. Avoid exposing arbitrary script execution by default. Update `capabilities.json` only after evidence supports a new status.

## Current status

Revit 2026 has live smoke tests. AutoCAD 2026 compiles against the installed SDK but awaits an interactive host test. SketchUp has a packaged Ruby extension but SketchUp is not installed on the development machine, so runtime behavior remains unverified. Blender, Maya and 3ds Max have scripting routes under assessment. Lumion and Enscape are treated as visualization integrations through supported host applications and formats until a documented direct automation API is established.
