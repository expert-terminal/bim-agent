# BIM-agent

**Open-source local bridge for agentic architecture modeling.** BIM-agent connects MCP-capable coding agents to desktop AEC applications through host extensions. It does not bundle an AI model, require an AI API key, or send model data to a BIM-agent cloud service. Give an agent this repository URL and ask it to follow [AGENTS.md](AGENTS.md) and [capabilities.json](capabilities.json).

> **Status: developer preview.** The original Revit connector passed live smoke tests. The expanded Revit toolset builds against Revit 2026 but awaits live testing. AutoCAD 2026 builds against the installed SDK; the expanded SketchUp extension is packaged but neither has been runtime-tested. Use disposable or backed-up projects.

| Application | Current connector | Status |
| --- | --- | --- |
| Revit 2026 | Native add-in + MCP | 15-tool core live-tested; expanded 56-tool set compiled, live test pending |
| AutoCAD 2026 | Native .NET bundle + MCP | 17 tools compiled; host test pending |
| SketchUp | Ruby extension + MCP | 27 tools packaged; host test pending |
| Blender, 3ds Max, Maya | Scripting APIs researched | Adapter work pending |
| Lumion, Enscape | Host sync / visualization route | No direct agent connector claimed |

See [compatibility and limits](docs/COMPATIBILITY.md), [AutoCAD setup](docs/AUTOCAD.md), [SketchUp setup](docs/SKETCHUP.md), and [professional workflows](docs/PRO_WORKFLOWS.md).

## Priority: Expert AI products

**Expert AI Terminal, Expert AI Terminal Pro, Expert OS, and Expert AI Agent are priority agent hosts for BIM-agent.** Each can use the repository as an agent workspace and run the [shell bridge](scripts/bim-agent-cli.mjs) to discover and call every currently implemented BIM-agent tool. Expert OS additionally has a native stdio MCP client, so its model can receive the tools directly after configuration. Native MCP tool registration inside Terminal, Terminal Pro, and AI Agent has not yet been verified; the shell bridge is their documented integration path today. Host application coverage and modeling depth remain as shown in the table above.

```powershell
node scripts/bim-agent-cli.mjs revit list
node scripts/bim-agent-cli.mjs revit revit_status
# For a write tool, put its arguments in request.json:
node scripts/bim-agent-cli.mjs revit revit_create_walls request.json
```

Read [Expert AI setup and evidence](docs/EXPERT_AI.md) for product-specific instructions. Any agent with local shell access can use this entry point; agents with an MCP client can connect to the corresponding `mcp/*.mjs` server directly.

## Revit modeling scope

The current 56-tool Revit catalog supports project/type discovery, typed walls, floors, ceilings, footprint roofs, rooms, family loading and placement (including hosted openings), parameter/type edits, transforms, groups, plan/drafting/section/elevation/3D views, office view templates, typed text notes, detail lines, grid dimensions, visibility, tags, schedules, title-block sheets, PDF/DWG export, warning inspection and deletion. The agent should inspect available families and types, propose a coordinate-based plan, execute bounded batches, then inspect results and warnings. The complete sequence is in [REVIT_WORKFLOWS.md](docs/REVIT_WORKFLOWS.md); inputs and limitations are in [TOOLS.md](docs/TOOLS.md).

Revit dimensions currently cover parallel straight grids; arbitrary wall/face dimensions, full annotation standards, filled regions, keynote systems, custom parametric families, stairs, curtain systems, MEP systems and linked-model coordination remain unimplemented. Detail lines are simple view-specific segments. Section boxes are axis-aligned to world X/Y, and elevation direction uses Revit marker indices. The connector can prepare useful native BIM and sheet deliverables when the project contains suitable families, types and standards. Construction-document completeness and design compliance require professional review.

Each project-changing operation asks the Revit user to approve its scope in a Revit dialog. Model edits use Revit transactions and Undo; family loading and file export use Revit's native APIs and are explicitly identified as non-Undoable external effects. Current Revit workflows create typed walls, floors, ceilings, roofs, rooms, family instances, plans, tags, schedules, sheets, and PDF/DWG outputs. See the [architectural workflow guide](docs/REVIT_WORKFLOWS.md) and [full tool reference](docs/TOOLS.md).

## How it works

```text
Coding agent ── MCP over stdio ── Node server ── Windows named pipe ── Revit add-in
                                                                  │
                                                       ExternalEvent on Revit API thread
                                                                  │
                                                       active Revit document
```

The agent cannot call Revit API methods from its own process. BIM-agent moves requests into Revit's API context. Reads return structured JSON. Writes request approval, execute, and return IDs or errors. The named pipe is restricted to the current Windows user; there is no public listener. [Autodesk documents external events](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/Revit_API_Revit_API_Developers_Guide_Advanced_Topics_External_Events_html.html) and [transactions](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/308ebf8d-d96d-4643-cd1d-34fffcea53fd.htm).

## Install from source on Windows

Requirements: Autodesk Revit 2026, Windows, .NET 8 SDK, Node.js 22 or newer, and a local MCP-capable agent. Revit and the agent are licensed separately. Autodesk DLLs are referenced from your Revit installation and are not redistributed.

```powershell
git clone https://github.com/expert-terminal/bim-agent.git
cd bim-agent
dotnet build addin/BimAgent.csproj -c Release
./scripts/install.ps1
```

Restart Revit and open a project. The installer writes a per-user `.addin` manifest pointing to the built DLL. Keep the cloned folder at the same path. **Close Revit before rebuilding:** Revit locks the loaded DLL. Run `./scripts/uninstall.ps1` to remove that manifest.

### Binary preview package

Download the latest BIM-agent preview ZIP asset from [Releases](https://github.com/expert-terminal/bim-agent/releases), extract it, and run install.ps1 from the extracted folder. The package includes the built add-in and MCP servers, so it does not require the .NET SDK. It still requires Revit 2026 and Node.js 22+. The installer copies files under your local app data folder, registers a per-user Revit add-in, and prints the MCP server path. Restart Revit afterward.

### Connect Codex

```powershell
codex mcp add bim-agent -- node "C:\absolute\path\to\bim-agent\mcp\server.mjs"
codex mcp list
```

### Connect Claude Desktop

Add this entry under `mcpServers` in Claude Desktop's local MCP configuration, then restart Claude Desktop:

```json
{
  "bim-agent": {
    "command": "node",
    "args": ["C:\\absolute\\path\\to\\bim-agent\\mcp\\server.mjs"]
  }
}
```

Claude also supports packaged desktop extensions; a `.mcpb` package is on the roadmap. [Claude local MCP guide](https://support.claude.com/en/articles/10949351-getting-started-with-local-mcp-servers-on-claude-desktop)

### ChatGPT availability

Codex supports MCP servers, but ChatGPT web custom MCP connections do not directly attach to a local stdio process. Availability also depends on the ChatGPT product and plan. A future remote or approved desktop distribution path must be evaluated separately; this repository does not claim universal ChatGPT subscription support. [OpenAI Docs for Codex MCP](https://developers.openai.com/learn/docs-mcp), [ChatGPT custom MCP connections](https://help.openai.com/en/articles/12584461-connecting-custom-mcp-servers-in-chatgpt)

## First test

1. Create or open a disposable Revit project.
2. Ask the agent to read AGENTS.md and docs/REVIT_WORKFLOWS.md.
3. Ask it to inspect the active document, levels, types, families and warnings before proposing a building plan.
4. Approve each summarized batch in Revit, then ask the agent to inspect created elements, views, schedules and warnings. Check PDF/DWG exports before sharing.

The wall tool accepts `[x, y]` endpoints and height in **millimetres**. IDs must come from the currently open document. For `revit_set_parameter`, inspect the parameter first: string values are text, integer values are integers, and double values are strings parsed in the project's displayed units, such as `"300 mm"`.

## Documentation

- [Architecture, transaction behavior, security and limits](docs/ARCHITECTURE.md)
- [Tool reference](docs/TOOLS.md)
- [Roadmap and release criteria](docs/ROADMAP.md)
- [Test status and repeatable smoke test](docs/TESTING.md)
- [Research and existing projects](STUDY.md)
- [Contributing](CONTRIBUTING.md)

## License

MIT. BIM-agent is independent and is not affiliated with Autodesk, Anthropic, or OpenAI.
