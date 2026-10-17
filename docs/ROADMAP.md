# Roadmap

## Multi-application sequence

1. **Revit:** live-test the 52-tool catalog, then add arbitrary wall/face dimensions, crop controls, filled regions, text notes, type-level material editing, stairs and MEP operations.
2. **AutoCAD:** complete a live 2026 host smoke test, then add blocks/inserts, layouts/viewports, block attributes, layer standards and file exchange.
3. **SketchUp:** test the 27-tool RBZ in a real SketchUp session, then add solid validation, terrain, stronger nested component workflows, textures and LayOut automation.
4. **Blender, 3ds Max, Maya:** add native adapters using their documented scripting APIs, with version-specific install/test paths.
5. **Lumion and Enscape:** validate host synchronization and import workflows. Direct controls require a documented, supported automation surface.

For each host, prioritize status and inventory, bounded creation/editing, native undo/transaction semantics, validation, and smoke tests before broadening scope. See [compatibility](COMPATIBILITY.md).

The goal is natural-language control of a complete BIM workflow through small, typed and verifiable operations. Revit exposes different APIs and constraints for different element families, so full project coverage requires many deliberately designed workflows.

## Current preview — 0.4.0

Current release adds typed architectural geometry and family placement; plan, drafting, section, elevation and 3D views; detail lines and parallel-grid dimensions; groups; schedules, tags, sheets and exports. Revit 2026 and AutoCAD 2026 compile; the SketchUp extension packages. The original 15-tool Revit core has live smoke-test evidence, while expanded operations await fresh host validation.

## 0.2 — Trustworthy modeling workflow

- Declarative operation plan with dry-run summaries and document identity preconditions.
- Stable element references, idempotency keys, result verification and failure reporting.
- Before/after snapshots and Revit view images so an agent can inspect what it made.
- Multi-operation transaction groups and an audit trail.
- Clean install/uninstall, packaged Node runtime, and disposable-model integration tests.

## Next Revit coverage

Prioritize arbitrary face and wall dimensions, detail annotation, crop regions and material workflows, with typed validation for families, hosts, levels, phases and units. Add natural-language workflow examples and reproducible benchmark models.

## Later — Structural, MEP and coordination

Structural framing and foundations; ducts, pipes, equipment and connectors; links, clashes, worksets and design options; IFC and document export. These require discipline-specific validation and cannot be promised as universal modeling from a single generic API.

## Public beta gate

Live read/write/Undo/rollback tests on disposable Revit models, malformed-input tests, clean install/uninstall, version compatibility checks, workshared/read-only behavior, signed release artifacts, support matrix and clear issue reporting. A GitHub source repository alone is not a finished one-click product.
