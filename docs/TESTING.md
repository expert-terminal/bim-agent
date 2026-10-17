# Test status

## Automated checks

Run from the repository root:

```powershell
node --test mcp/server.test.mjs
node --check mcp/server.mjs
dotnet build addin/BimAgent.csproj -c Release
```

Close Revit before a normal build because it locks the loaded DLL. For a compile check while Revit is open, build to a different output folder:

```powershell
dotnet build addin/BimAgent.csproj -c Release -p:OutputPath=../build-check
```

The automated MCP test checks initialization, tool discovery and method errors. It does not simulate Revit. The .NET build checks API signatures against the locally installed Revit 2026 assemblies.

## Live checks completed on Revit 2026

- `revit_status` returned the active project, version and read-only state.
- Consecutive calls listed levels and wall types.
- Created a 1,000 mm basic wall on L1 after Revit approval; read-back showed the selected type, level, 1,000 mm length and 3,000 mm height.
- Edited its `Comments` parameter after approval.
- Deleted that test wall after approval; Revit reported the created ID among deleted IDs.

The test was on a disposable unsaved `Project1` model. The level, grid and sheet creation tools were added afterward; they compile but require live write checks. Worksharing, read-only documents, other Revit versions and clean-machine installation remain untested.

## Repeatable manual smoke test

1. Close Revit, build Release, run `./scripts/install.ps1`, then reopen Revit with a disposable project.
2. Call `revit_status`, `revit_list_levels`, `revit_list_wall_types`, `revit_list_views`, `revit_list_sheets` and `revit_list_family_types`.
3. Create a short wall, approve it in Revit, inspect the returned ID, and verify length, type, level and Undo.
4. Set its `Comments`, inspect again, delete it, and confirm it no longer appears.
5. Separately test level, grid and sheet creation. Confirm their names/IDs and Undo entries. Delete only test objects you created.
6. Repeat with a read-only document, malformed arguments, no active document, and a closed Revit process to verify clear errors.
