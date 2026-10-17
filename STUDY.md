# Feasibility study and build plan (17 October 2026)

## What exists already

Several open-source Revit MCP connectors exist. A new project should earn its place through predictable modeling, trust, and install quality, not simply a larger tool count.

| Project | Relevant capability | Observed constraint or tradeoff |
| --- | --- | --- |
| [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server) | Large TypeScript plus Revit add-in tool surface, multiple Revit versions, model actions | Large tool surface raises discoverability and validation burden; evaluate implementation and license before reuse |
| [oakplank/RevitMCP](https://github.com/oakplank/RevitMCP) | pyRevit extension, local MCP and web UI, view image export | Requires pyRevit and Python installation for direct server use |
| [bimwright/rvt-mcp](https://github.com/bimwright/rvt-mcp) | .NET MCP server and per-version Revit add-ins | Another established baseline to benchmark rather than duplicate |
| [Sam-AEC/aec-model-bridge](https://github.com/Sam-AEC/aec-model-bridge) | Approval-oriented Revit MCP and broader AEC integration | Broad scope; inspect exact guarantees and tests before adopting claims |
| [HorizunGroup/horizun-revit-mcp](https://github.com/HorizunGroup/horizun-revit-mcp) | Apache-2.0, Windows installer, typed BIM tools, verification | Strong packaging and verification benchmark |
| [KenLP/RevitMCPServer](https://github.com/KenLP/RevitMCPServer) | Multi-step operations in one undoable transaction | Useful benchmark for atomic operations |

## Mainstream feature snapshot (17 October 2026)

This is a public-documentation survey, not a controlled quality or runtime benchmark. Counts below are claims in project documentation and may change. BIM-agent currently has smaller catalogs: 55 Revit, 17 AutoCAD and 27 SketchUp tools. The survey highlights workflow depth, visual feedback, batch operations, and version coverage as priorities; BIM-agent currently targets Revit 2026 and AutoCAD 2026.

| Reference project | Documented coverage | Product implication |
| --- | --- | --- |
| [LuDattilo/revit-mcp-server](https://github.com/LuDattilo/revit-mcp-server) | README describes 124–138 Revit tools, Revit 2023–2027 builds, project/model info, clash detection, CRUD, batch operations, PDF/DWG/IFC/CSV exports, and optional in-Revit chat. | Prioritize supported version builds, coordination QA, IFC/CSV, visual feedback, and end-to-end batch workflows. |
| [thewisekape/autocad-mcp](https://github.com/thewisekape/autocad-mcp) | 22 tools / 130+ operations over AutoLISP IPC, COM and headless DXF, including drawing management and broad entity/layer operations. | Expand beyond live model-space primitives toward drawing lifecycle, layout/block operations and an optional headless exchange backend. |
| [zinin/sketchup-mcp2](https://github.com/zinin/sketchup-mcp2) | Typed solids/components, booleans, edge operations, export, model search, viewport screenshots and atomic undo. Also offers an arbitrary Ruby escape hatch controlled through settings. | Add search, screenshots, export and richer solid/component workflows. Any arbitrary code route must be explicitly opt-in and identified as high risk. |

### What can be built and what remains a genuine gap

- Typed architectural geometry, view/sheet documentation, schedules, parameter edits, transformations, warning inspection and file exports are feasible through a local adapter, subject to the host API and loaded project content. View-template and text-note operations are now part of this connector.
- Face-referenced dimensions, crop/annotation standards, custom family authoring, stairs, curtain-wall systems, MEP routing/connectivity, linked model coordination, clash review, worksharing-aware changes and visual feedback require substantial design and live validation.
- Unattended Revit jobs use Autodesk's separate Automation API with restrictions; they do not provide the same interactive UI access as a local add-in. A coding-agent subscription does not guarantee local MCP access in every product surface.
- Valid Revit API objects can still be architecturally, dimensionally, code, accessibility, fire/life-safety, structural or documentation-wrong. Specification checks, visual review and licensed professional judgment remain necessary.

The next milestone should prove a complete brief-to-verified-model-and-sheets workflow with visual feedback, repeatable QA, family lookup and supported-version builds, rather than optimize only for tool count.

## Platform facts

- A Revit add-in loads through a `.addin` manifest. [Autodesk registration guide](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Introduction/Add_In_Integration/Revit_API_Revit_API_Developers_Guide_Introduction_Add_In_Integration_Add_in_Registration_html.html)
- Revit API work initiated outside an API callback must be marshalled through `ExternalEvent`; the callback runs when Revit can service it. [Autodesk external events guide](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_External_Events_html.html)
- Model changes require a transaction. Commit can fail or await failure handling; successful changes appear in the undo stack. [Autodesk transaction API](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/308ebf8d-d96d-4643-cd1d-34fffcea53fd.htm)
- MCP defines stdio and Streamable HTTP transport. Local stdio is well suited to coding agents on the Revit computer. [MCP transport specification](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2025-11-25/basic/transports.mdx)
- Claude Desktop supports local MCP desktop extensions. Codex supports MCP configuration. ChatGPT web custom MCP connections are remote, so an arbitrary local stdio server is not universally available to all ChatGPT subscribers. Local OpenAI desktop plugin distribution has separate approval requirements. [Claude local MCP](https://support.claude.com/en/articles/10949351-getting-started-with-local-mcp-servers-on-claude-desktop), [Codex MCP docs](https://developers.openai.com/learn/docs-mcp), [ChatGPT custom MCP](https://help.openai.com/en/articles/12584461-connecting-custom-mcp-servers-in-chatgpt), [OpenAI desktop plugin submission](https://developers.openai.com/plugins/deploy/desktop-only)

## Possible now

- Read active model metadata, selected elements, levels, types, parameter values, geometry summaries and views.
- Create and edit supported Revit elements through typed, version-aware operations, including walls, floors, doors, rooms, grids, levels, sheets, annotations and many MEP elements, subject to available families, hosts and constraints.
- Group operations in transactions, validate inputs, generate a preview/diff, request approval, execute, then query the model for verification.
- Provide local MCP access to compatible coding agents without collecting the user's model on a connector-hosted cloud service.

## Limits that matter

- “All types of modeling” cannot be guaranteed through one generic tool. Revit elements have different creation APIs and constraints; families, hosts, views, worksets, design options and linked models affect what can be created.
- Natural-language intent is probabilistic. A connector can validate and report operations; it cannot guarantee architectural correctness, code compliance or clash-free design without domain checks and human review.
- Revit's UI must be open and able to process external events for this local live-model path. A blocked dialog, active command or user approval can delay a request.
- Revit and the AI client remain separately licensed. A ChatGPT subscription alone does not guarantee access to arbitrary local MCP tools in every ChatGPT surface.
- Autodesk's cloud Automation API is a separate route, with no Revit UI access and batch-job restrictions. It does not replace a live interactive connector. [Autodesk Automation restrictions](https://aps.autodesk.com/en/docs/design-automation/v3/developers_guide/restrictions)
- Neither Revit runtime behavior nor compatibility across versions is proven by compiling this prototype. They require interactive test models and a version matrix.

## Product direction

1. **Foundation (started here):** local stdio MCP, per-user named pipe, Revit API thread dispatch, read tools, approval-gated writes, atomic transactions, explicit millimetre units.
2. **Modeling IR:** a typed declarative plan of objects and constraints. `inspect → plan → dry-run → human approve → execute → verify`; stable element handles, idempotency keys and document/version preconditions.
3. **Coverage:** build tool families around real workflows (architectural shell, openings, rooms, sheets, MEP routing), each with domain validation, not hundreds of unstructured methods.
4. **Visual feedback:** active-view snapshots and focused element images for agent perception, plus warnings, failure reports and before/after diffs.
5. **Distribution:** signed Windows installer, bundled server runtime, automatic client setup where supported, per-Revit-version builds, CI and disposable-model integration tests.
6. **Trust:** per-operation approval policies, audit log, user identity, worksharing awareness, bounded operations, rollback/recovery guidance and zero arbitrary code execution by default.

## Acceptance gate for a public beta

Test clean install and uninstall on supported Windows/Revit versions; read and write against disposable projects; verify undo and rollback; exercise closed/no-document/busy Revit, read-only and workshared models, malformed tool calls, unit conversions and disconnected clients. Publish a precise compatibility matrix and reproducible release build before calling it ready for general users.
