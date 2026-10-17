# AutoCAD 2026 adapter

## Install and connect

On Windows with AutoCAD 2026, .NET 8 SDK, and Node.js 22+:

```powershell
dotnet build autocad/BimAgent.AutoCAD.csproj -c Release
./scripts/install-autocad.ps1
```

Restart AutoCAD and open a disposable drawing. Configure your MCP client to run `node C:\absolute\path\to\bim-agent\mcp\autocad.mjs`. Run `autocad_status` before writes and check `units`; coordinates and dimensions use native drawing units. `./scripts/uninstall-autocad.ps1` removes the per-user bundle. The installer copies the compiled DLL into `%APPDATA%\Autodesk\ApplicationPlugins\BIM-agent-AutoCAD.bundle`; rebuild and reinstall after source changes.

| Tool | Current scope |
| --- | --- |
| `autocad_status` | Active drawing, version and INSUNITS |
| `autocad_list_layers` | Layer names and lock/freeze state |
| `autocad_list_entities` | Bounded model-space handle, type and layer inventory |
| `autocad_create_layer` | Named layer |
| `autocad_create_lines` | Batch of 1–100 line entities, optional existing layer |
| `autocad_create_polylines` | Batch of open or closed XY polylines |
| `autocad_create_dimension` | Linear dimension between two coplanar points |
| `autocad_create_text` | Single-line drawing text |
| `autocad_create_box` | One 3D solid box |

Writes ask for confirmation inside AutoCAD and run inside a database transaction. A compilation check has passed; interactive startup and operation tests remain pending. The current tools do not implement blocks, hatches, layouts, parametric constraints, annotation standards, or external file import/export. Do not treat generic solids as typed BIM elements.

Autodesk documents [AutoCAD .NET customization](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm), [database transactions](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-DevGuide-Managed/files/GUID-EC036F5A-1F02-40D3-B348-4193BA58CF0C.htm), and [application bundles](https://help.autodesk.com/view/ACD/2026/ENU/?caas=caas%2Fdocumentation%2FCIV3D%2F2014%2FENU%2FfilesACD%2FGUID-3C25E517-8660-4BB7-9447-2310462EF06F-htm.html).
