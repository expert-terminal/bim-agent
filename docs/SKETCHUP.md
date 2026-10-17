# SketchUp adapter

## Install and connect

Run `./scripts/package-sketchup.ps1` on Windows, then install `dist/BIM-agent-SketchUp-0.2.0.rbz` through SketchUp's Extension Manager. Restart SketchUp and open a disposable model. Configure an MCP client to run `node C:\absolute\path\to\bim-agent\mcp\sketchup.mjs`. The extension creates a random token and a loopback listener on startup. The MCP server reads its local configuration from `%LOCALAPPDATA%\BIM-agent\sketchup-bridge.json`.

| Tool | Current scope |
| --- | --- |
| `sketchup_status` | Active model and SketchUp version |
| `sketchup_list_groups` | Bounded root-group inventory |
| `sketchup_create_extrusion` | Named group extruded from 3–50 planar footprint points; millimetres |
| `sketchup_delete_group` | Delete one group by persistent ID |

Writes ask for confirmation inside SketchUp and use its operation/Undo system. The Ruby API calls run from a UI timer so model changes happen on SketchUp's main thread. The RBZ packaging check passed, but the adapter has **not** been run in SketchUp on this development machine. Material assignment, tags, components, scenes, solid validation, terrain and import/export tools are future work.

SketchUp documents [extension packaging](https://developer.sketchup.com/article-creating-a-sketchup-extension), [UI timers](https://ruby.sketchup.com/UI), [model operations](https://ruby.sketchup.com/Sketchup/Model.html), and [geometry creation](https://ruby.sketchup.com/file.generating_geometry.html).
