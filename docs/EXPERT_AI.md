# Expert AI product integration

These four products are first-priority agent hosts. BIM-agent is a separate open-source connector; it does not require modifications to their private repositories to use the shell entry point. The user must install the selected CAD/BIM application and its BIM-agent host adapter first.

| Product | Integration available from BIM-agent | Evidence and limit |
| --- | --- | --- |
| Expert OS | Native stdio MCP client | Its `@deepseek-ai/dsh-mcp-client` plugin maps MCP tools to model tools. Configuration below is derived from its client README. Product-level live test pending. |
| Expert AI Terminal | Shell bridge through its local coding-agent workspace | Its Pi-based package and CLI are documented in its repository. Native MCP tool registration has not been verified. |
| Expert AI Terminal Pro | Shell bridge through its local coding-agent workspace | Its Pi-based package includes extensions; native BIM-agent MCP tool registration has not been verified. |
| Expert AI Agent | Shell bridge through its permitted local workspace | Its Pi server includes file/bash tools and an extension setting. Native BIM-agent MCP tool registration has not been verified. The selected allowed folder must include the BIM-agent checkout or a permitted path to it. |

## Expert OS: direct MCP tools

Add one entry per installed CAD/BIM adapter to Expert OS `cordis.yml`, using absolute paths:

```yaml
- id: mcp-bim-revit
  name: '@deepseek-ai/dsh-mcp-client'
  config:
    serverName: bim_revit
    transport: stdio
    command: node
    args: ['C:/absolute/path/to/bim-agent/mcp/server.mjs']
    toolCallTimeoutMs: 150000
```

For AutoCAD use `mcp/autocad.mjs`, and for SketchUp use `mcp/sketchup.mjs`, each with a distinct `id` and `serverName`. Start the design application before asking for model status. Expert OS exposes names such as `mcp__bim_revit__revit_status` after connecting. Its default 60-second MCP timeout is shorter than BIM-agent's approval window, so the example increases it.

## Expert AI Terminal, Terminal Pro, and AI Agent: shell bridge

Open the BIM-agent checkout as the agent's workspace, or give the agent an absolute path to it. Ask it to read `AGENTS.md` and run:

```powershell
node C:\absolute\path\to\bim-agent\scripts\bim-agent-cli.mjs revit list
node C:\absolute\path\to\bim-agent\scripts\bim-agent-cli.mjs revit revit_status
```

Replace `revit` with `autocad` or `sketchup` when using those adapters. To call a tool with arguments, save a JSON object in a file and provide its path as the third argument. The bridge prints the MCP result as JSON and exits nonzero for a tool error. This route gives access to all currently exposed tools through the host's shell capability. It does not automatically register those tools as model-native function calls.

The Terminal repositories' READMEs describe AEC-focused Pi-based agents. Expert AI Agent's `main/pi-server.js` configures file/bash tools and accepts extension settings. Expert OS documents its [MCP client plugin](https://github.com/expert-terminal/expert-os/blob/main/packages/mcp/mcp-client/README.md). These private repository links require access to the `expert-terminal` organization.

## Verification

The shell bridge's `list` command has been exercised against the Revit, AutoCAD and SketchUp MCP servers. A status call without an open host correctly returns a connection error. Product UI integration and native MCP tool registration remain untested. Do not present a configured shell bridge as proof of an end-to-end modeling session.
