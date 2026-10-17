# Compatibility and implementation limits

BIM-agent uses one MCP entry point per host today. A coding agent must install the relevant host extension, start the application, and connect its local MCP server. A repo URL gives an agent the instructions and source; it cannot by itself grant access to a closed desktop process. A ChatGPT or Claude subscription alone does not guarantee local MCP support in every client.

| Host | Route | Evidence and current limit |
| --- | --- | --- |
| Revit 2026 | In-process .NET add-in; ExternalEvent and transactions | Earlier 15-tool core passed live smoke tests. Expanded 56-tool source compiles; new documentation/view operations await live host tests. |
| AutoCAD 2026 | In-process .NET extension; idle dispatch and transactions | Expanded 17-tool source compiles against installed 2026 DLLs; no host runtime test yet |
| SketchUp | Ruby extension; UI timer and model operations | Expanded 27-tool RBZ package built; SketchUp unavailable on development machine, so runtime unverified |
| Blender | Python API and main-thread timers | Feasible adapter path; no shipped connector |
| 3ds Max | MAXScript / Python APIs | Feasible adapter path; no shipped connector or local host test |
| Maya | Python API | Feasible adapter path; no shipped connector or local host test |
| Lumion | Supported import and LiveSync from modeling hosts | Visualization downstream; no documented general-purpose direct modeling API validated here |
| Enscape | Live rendering plugin in supported modeling hosts | Visualization downstream; no documented general-purpose direct modeling API validated here |

The API exposes more surface than any preview tool catalog. This build covers a useful architectural slice, not complete production documentation or every Revit feature. It creates grid-to-grid dimensions, simple detail lines, axis-aligned sections and marker-based elevations, but not general wall/face dimensions, annotation standards, assemblies, custom parametric families, stairs, or MEP systems. Available families, hosts, phases, templates and view settings constrain what can be placed. Each operation has host-specific limits; external effects such as exports and family loading do not roll back with Undo.

Sources: [Revit external events](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_External_Events_html.html), [Revit section views](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/d6228f68-3643-8aaf-72bb-f9a0b4125886.htm), [Revit elevation markers](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/2f707487-817c-d648-4b99-559f9000f123.htm), [AutoCAD managed API](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-A6C680F2-DE2E-418A-A182-E4884073338A.htm), [AutoCAD hatch workflows](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-DevGuide-Managed/files/GUID-89CAED67-B3A0-4045-9D8B-9034D67E8D91.htm), [SketchUp Ruby API](https://ruby.sketchup.com/), [SketchUp geometry](https://ruby.sketchup.com/Sketchup/Entities.html), [Blender Python API](https://docs.blender.org/api/main/index.html), [3ds Max developer guide](https://help.autodesk.com/view/MAXDEV/2026/ENU/), [Maya developer guide](https://help.autodesk.com/view/MAYADEV/2026/ENU/), [Lumion LiveSync](https://support.lumion.com/knowledge-base/api/v2/help_center/en-us/articles/4407284963858.json), [Enscape SketchUp integration](https://docs.chaos.com/display/ESKETCHUP/Enable%2BEnscape).
