# BIM-agent

**Open-source local bridge for agentic Revit modeling.** BIM-agent exposes a live Revit model to MCP-capable coding agents through a small stdio server and a Revit add-in. It does not bundle a model, require an AI API key, or send model data to a BIM-agent cloud service.

> **Status: developer preview for Revit 2026.** The add-in compiles against Revit 2026 and has passed a live connection check. Model writes and a clean installer still need broader testing. Use a disposable Revit project.

## What an agent can do today

| Tool | Purpose |
| --- | --- |
| `revit_status` | Identify the active document, Revit version, and read-only state |
| `revit_list_levels` | Get level IDs and elevations in millimetres |
| `revit_list_wall_types` | Get wall type IDs and kinds |
| `revit_list_walls` | Inspect a bounded list of walls |
| `revit_list_elements` | Discover placed elements, optionally by category name |
| `revit_inspect_element` | Read an element and its instance parameters |
| `revit_create_walls` | Create a batch of straight basic walls in millimetres |
| `revit_set_parameter` | Edit a writable instance parameter |
| `revit_delete_elements` | Delete selected elements and report dependent deletions |

Each write asks the Revit user to approve it. Changes execute in a Revit transaction, so a successful operation appears in Revit's Undo menu. **Undo is a consequence of Revit's required transaction system, not a limit on what an agent can eventually do.** Some project operations have special APIs or external side effects and need their own tools and safeguards; see [architecture and scope](docs/ARCHITECTURE.md) and [roadmap](docs/ROADMAP.md).

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
2. Ask the agent: “Use `revit_status`, then list levels and wall types.”
3. Ask it to inspect an element or create one short wall. Check the proposed dimensions in the Revit approval dialog.
4. Inspect the resulting element and confirm that Revit Undo contains the change.

The wall tool accepts `[x, y]` endpoints and height in **millimetres**. IDs must come from the currently open document. For `revit_set_parameter`, inspect the parameter first: string values are text, integer values are integers, and double values are strings parsed in the project's displayed units, such as `"300 mm"`.

## Documentation

- [Architecture, transaction behavior, security and limits](docs/ARCHITECTURE.md)
- [Tool reference](docs/TOOLS.md)
- [Roadmap and release criteria](docs/ROADMAP.md)
- [Research and existing projects](STUDY.md)
- [Contributing](CONTRIBUTING.md)

## License

MIT. BIM-agent is independent and is not affiliated with Autodesk, Anthropic, or OpenAI.
