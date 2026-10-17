# Roadmap

## Multi-application sequence

1. **Revit:** expand typed BIM creation/editing (doors, windows, floors, roofs, rooms, dimensions, families, materials, views and schedules) with preview and read-back validation.
2. **AutoCAD:** complete a live 2026 host smoke test, then add polylines, blocks, dimensions, layouts, annotations and layer standards.
3. **SketchUp:** test the packaged RBZ in a real SketchUp session, then add components, materials, tags, scenes and solid checks.
4. **Blender, 3ds Max, Maya:** add native adapters using their documented scripting APIs, with version-specific install/test paths.
5. **Lumion and Enscape:** validate host synchronization and import workflows. Direct controls require a documented, supported automation surface.

For each host, prioritize status and inventory, bounded creation/editing, native undo/transaction semantics, validation, and smoke tests before broadening scope. See [compatibility](COMPATIBILITY.md).

The goal is natural-language control of a complete BIM workflow through small, typed and verifiable operations. Revit exposes different APIs and constraints for different element families, so full project coverage requires many deliberately designed workflows.

## 0.1 — Developer preview (current)

Local MCP and Revit transport, bounded reads, straight wall, level, grid and sheet creation, parameter editing, deletion, Revit approval, transactions and explicit units. Source build for Revit 2026. These tools passed live smoke tests on a disposable Revit project. Dependent-deletion preview is compiled but awaits a live test.

## 0.2 — Trustworthy modeling workflow

- Declarative operation plan with dry-run summaries and document identity preconditions.
- Stable element references, idempotency keys, result verification and failure reporting.
- Before/after snapshots and Revit view images so an agent can inspect what it made.
- Multi-operation transaction groups and an audit trail.
- Clean install/uninstall, packaged Node runtime, and disposable-model integration tests.

## 0.3 — Architectural and documentation coverage

Dedicated tools for floors, openings, doors, windows, rooms, roofs, dimensions, tags, schedules and family placement. Each tool will validate required types, hosts, levels, phases and units. Add natural-language workflow examples and reproducible benchmark models.

## Later — Structural, MEP and coordination

Structural framing and foundations; ducts, pipes, equipment and connectors; links, clashes, worksets and design options; IFC and document export. These require discipline-specific validation and cannot be promised as universal modeling from a single generic API.

## Public beta gate

Live read/write/Undo/rollback tests on disposable Revit models, malformed-input tests, clean install/uninstall, version compatibility checks, workshared/read-only behavior, signed release artifacts, support matrix and clear issue reporting. A GitHub source repository alone is not a finished one-click product.
