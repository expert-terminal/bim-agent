# MCP tool reference

All tools operate on the **active Revit document**. Tool results use JSON. If Revit is closed, has no active document, or rejects an operation, the MCP result has `isError: true`.

| Tool | Input | Result |
| --- | --- | --- |
| `revit_status` | `{}` | Document title/path, Revit version, read-only flag |
| `revit_list_levels` | `{}` | Level IDs, names, elevations in mm |
| `revit_list_wall_types` | `{}` | Wall type IDs, names, kind |
| `revit_list_views` | Optional `limit` 1–200 | View IDs, names, types and scales |
| `revit_list_sheets` | Optional `limit` 1–200 | Sheet IDs, numbers, names and placeholder flags |
| `revit_list_family_types` | Optional `category` and `limit` 1–200 | Family type IDs, names and categories |
| `revit_list_walls` | Optional `limit` 1–200 | Wall IDs, names, types, levels, lengths in mm |
| `revit_list_elements` | Optional `category` display name and `limit` 1–200 | Placed element IDs, unique IDs, names, categories, type IDs |
| `revit_inspect_element` | `elementId` | Element identity and up to 300 instance parameters |
| `revit_create_walls` | 1–50 wall specifications | Approval state and created IDs |
| `revit_create_level` | `name`, `elevationMm` | Approval state and created level ID |
| `revit_create_grid` | `name`, `startMm`, `endMm` | Approval state and created grid ID |
| `revit_create_sheet` | `number`, `name` | Approval state and created sheet ID; no title block |
| `revit_set_parameter` | `elementId`, `parameterName`, `value` | Approval state and new raw value |
| `revit_delete_elements` | 1–20 distinct `elementIds` | Approval state, requested and actually deleted IDs |

## Expanded Revit 2026 tools

The expanded source build provides 56 tools. Fifteen core tools were included in the earlier live smoke test; the expanded operations compile, but require a fresh interactive Revit validation before users rely on them in production.

| Workflow | Tools | Purpose |
| --- | --- | --- |
| Discover types | `revit_list_floor_types`, `revit_list_ceiling_types`, `revit_list_roof_types`, `revit_list_view_types`, `revit_list_titleblock_types`, `revit_list_materials`, `revit_list_rooms` | Discover project content before making placements |
| Architectural geometry | `revit_create_floors`, `revit_create_ceilings`, `revit_create_roof`, `revit_create_room` | Create typed boundary-based elements and place rooms inside enclosed boundaries |
| Families and openings | `revit_load_family_type`, `revit_place_family_instances` | Load one local family type and place compatible instances; wall-hosted doors/windows need a valid host wall |
| Views and sheets | `revit_create_plan_view`, `revit_create_3d_view`, `revit_duplicate_view`, `revit_set_view_scale`, `revit_set_view_visibility`, `revit_list_view_templates`, `revit_apply_view_template`, `revit_place_view_on_sheet`, `revit_create_sheet` | Create views, apply a compatible office template, control scale/visibility, and arrange views on sheets |
| Documentation views and graphics | `revit_list_text_note_types`, `revit_create_drafting_view`, `revit_create_section_view`, `revit_create_elevation_view`, `revit_create_detail_lines`, `revit_create_text_notes`, `revit_dimension_grids` | Discover loaded note styles; create a drafting view, bounded axis-aligned section, marker-based elevation, view-specific lines and typed notes, and dimensions between parallel straight grids |
| Annotation and schedules | `revit_tag_elements`, `revit_list_schedule_categories`, `revit_list_schedule_fields`, `revit_create_schedule` | Place compatible tag types and build category schedules using available fields |
| Editing and recovery | `revit_move_elements`, `revit_rotate_elements`, `revit_copy_elements`, `revit_change_element_type`, `revit_create_group`, `revit_ungroup_elements` | Transform, retype, group, or ungroup elements from the active project |
| QA and deliverables | `revit_model_health`, `revit_export_sheets_pdf`, `revit_export_views_dwg` | Inspect Revit warnings and export confirmed deliverables |

The footprint roof tool sets one slope angle on every boundary edge. Grid dimensions cover straight parallel grids only; arbitrary wall/face dimensioning is not implemented. Section boxes are aligned to world X or Y; elevation direction uses the four Revit marker indices. Detail lines are plain segments without line styles or filled regions. Text note coordinates are view/model coordinates and can need adjustment in the target view. Applying a template can override view settings controlled by that template. Custom family authoring, stairs, curtain systems, MEP systems and linked-model coordination remain gaps. File exports and family loading are external effects and do not use Revit's project transaction Undo behavior.

## Create a wall

Find valid `levelId` and basic `wallTypeId` with the list tools. The wall's endpoints use XY coordinates in millimetres; its Z position comes from the selected level.

```json
{
  "walls": [
    {
      "startMm": [0, 0],
      "endMm": [4000, 0],
      "levelId": 123456,
      "wallTypeId": 234567,
      "heightMm": 3000
    }
  ]
}
```

IDs above are placeholders. The user sees a Revit confirmation dialog. One batch produces one transaction and one Undo entry.

## Edit a parameter

Inspect the element first. `parameterName` must match exactly one parameter on that element. A text parameter takes a string; an integer takes an integer; a double takes a unit-formatted string compatible with the project's Revit display settings (for example `"300 mm"`). The tool will reject read-only or ambiguous parameters.

```json
{"elementId": 123456, "parameterName": "Comments", "value": "Reviewed by BIM-agent"}
```

## Delete elements

Deletion can remove dependent elements too. The source previews affected IDs in a temporary transaction, rolls it back, then presents the affected set in the approval dialog. The core deletion behavior was live-smoke-tested; the latest expanded binary still needs a fresh host validation. The result reports every ID Revit says it deleted. Inspect the project again after deletion.

```json
{"elementIds": [123456]}
```
