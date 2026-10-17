# v0.1.0-preview

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
