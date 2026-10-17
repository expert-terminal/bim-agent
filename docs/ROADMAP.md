# Roadmap

The goal is natural-language control of a complete BIM workflow through small, typed and verifiable operations. Revit exposes different APIs and constraints for different element families, so full project coverage requires many deliberately designed workflows.

## 0.1 — Developer preview (current)

Local MCP and Revit transport, bounded reads, straight wall, level, grid and sheet creation, parameter editing, deletion, Revit approval, transactions and explicit units. Source build for Revit 2026. The new level, grid and sheet tools compile but still need a live write test.

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
