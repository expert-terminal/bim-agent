# SketchUp adapter

## Install and connect

Run `./scripts/package-sketchup.ps1` on Windows, then install the generated `dist/BIM-agent-SketchUp-0.3.0-preview.rbz` through SketchUp's Extension Manager. Restart SketchUp and open a disposable model. Configure an MCP client to run `node C:\absolute\path\to\bim-agent\mcp\sketchup.mjs`. The extension creates a random token and a loopback listener on startup. The MCP server reads its local configuration from `%LOCALAPPDATA%\BIM-agent\sketchup-bridge.json`.

| Tool | Current scope |
| --- | --- |
| `sketchup_status` | Active model and SketchUp version |
| `sketchup_list_groups` | Bounded root-group inventory |
| `sketchup_list_components` | Root component inventory |
| `sketchup_list_materials` / `sketchup_list_tags` | Material colors and tag state |
| `sketchup_create_extrusion` | Named group extruded from 3–50 planar footprint points; millimetres |
| `sketchup_create_component` | Reusable component from a footprint extrusion |
| `sketchup_create_box` | Named grouped box |
| `sketchup_create_material` / `sketchup_assign_material` | Create RGB material and apply it to a group |
| `sketchup_create_tag` / `sketchup_assign_tag` | Create tag and assign it to a group |
| `sketchup_move_group` / `sketchup_rotate_group` / `sketchup_copy_group` | Transform and duplicate groups |
| `sketchup_delete_group` | Delete one group by persistent ID |

Writes ask for confirmation inside SketchUp and use its operation/Undo system. The Ruby API calls run from a UI timer so model changes happen on SketchUp's main thread. The RBZ packaging check passed, but the adapter has **not** been run in SketchUp on this development machine. Scenes, solid validation, terrain, imported model workflows and advanced material textures remain future work.

SketchUp documents [extension packaging](https://developer.sketchup.com/article-creating-a-sketchup-extension), [UI timers](https://ruby.sketchup.com/UI), [model operations](https://ruby.sketchup.com/Sketchup/Model.html), and [geometry creation](https://ruby.sketchup.com/file.generating_geometry.html).
