# Contributing

Issues and pull requests are welcome. BIM-agent is an independent MIT-licensed project.

1. Open an issue describing the Revit version, model type, expected result and actual result. Do not upload confidential project files.
2. Build the add-in with `dotnet build addin/BimAgent.csproj -c Release` and check the MCP server with `node --check mcp/server.mjs`.
3. Test writes only on a disposable Revit model. Record the returned IDs, visible model result and Undo behavior.
4. Keep agent-facing tools typed and bounded. Add validation and a clear Revit approval summary for writes. Do not add arbitrary code execution to the default server.
5. Document new tools in `docs/TOOLS.md` and update the compatibility notes.

The project does not redistribute Autodesk assemblies. Contributors need their own licensed Revit installation to compile and run the add-in.
