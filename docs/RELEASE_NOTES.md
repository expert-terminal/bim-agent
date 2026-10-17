# v0.4.0-preview

- Revit grew from 45 to 52 tools: drafting, section and elevation views, detail lines, grid dimensions, and group/ungroup operations.
- AutoCAD grew from 9 to 17 tools: multiline text, circles, arcs, associative hatches, move/copy/rotate, and deletion.
- SketchUp grew from 16 to 27 tools: planar faces, dimensions, scenes, section planes, camera control, edge paths, circles, arcs, and cylinders.
- Updated agent instructions, capabilities, tool references and workflow limits for the expanded catalogs.
- Rebuilt both native extensions. Revit and AutoCAD compile against the installed 2026 SDKs; SketchUp RBZ is packaged. New host operations require interactive validation.

This remains a developer preview. These tools do not generate complete construction documentation or guarantee design/code compliance. See [compatibility](COMPATIBILITY.md), [Revit workflows](REVIT_WORKFLOWS.md), and [tool reference](TOOLS.md).

## v0.3.0-preview

- Expanded the Revit catalog to 45 tools for architecture modeling, family placement, rooms, views, tags, schedules, sheets, PDF/DWG exports, model inspection and edits.
- Expanded AutoCAD to 9 tools, including polylines, dimensions and text.
- Expanded SketchUp to 16 tools for component geometry, materials, tags and transforms.
- Added guided Revit workflows, capability discovery and host-specific setup documentation.
- Compiled Revit 2026 and AutoCAD 2026 bridges. The original 15-tool Revit core has live smoke-test evidence; expanded Revit, AutoCAD and SketchUp behavior still needs runtime validation in their host applications.

This remains a developer preview. It does not create complete construction documentation or support every Revit modeling domain. See [compatibility](COMPATIBILITY.md), [Revit workflows](REVIT_WORKFLOWS.md), and [tool reference](TOOLS.md).

## Previous release: v0.1.0-preview

First public developer preview for Windows and Autodesk Revit 2026.

## Included

- Local MCP server for Codex, Claude Desktop and other local stdio MCP clients.
- Revit add-in connected through a current-user-only named pipe and Revit `ExternalEvent`.
- Project, level, wall type, wall, element, view, sheet and family type discovery.
- Approval-gated wall, level, grid and sheet creation; instance parameter editing; element deletion.
- Binary ZIP with a per-user installer. Revit 2026 and Node.js 22+ are still required; the .NET SDK is not needed for the ZIP.

## Validation and limits

The add-in compiled against installed Revit 2026 APIs. MCP protocol tests passed locally and in GitHub Actions. Live Revit testing covered status, levels, wall types, wall creation, element inspection, parameter editing and deletion. The later view, sheet, family type, level, grid and sheet tools compile but have not completed live runtime validation. Use a disposable model.

The preview does not provide complete Revit modeling coverage, a one-click extension package, worksharing validation, or support for other Revit versions. See [roadmap](ROADMAP.md) and [test status](TESTING.md).
