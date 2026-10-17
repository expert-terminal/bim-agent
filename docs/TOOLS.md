# MCP tool reference

All tools operate on the **active Revit document**. Tool results use JSON. If Revit is closed, has no active document, or rejects an operation, the MCP result has `isError: true`.

| Tool | Input | Result |
| --- | --- | --- |
| `revit_status` | `{}` | Document title/path, Revit version, read-only flag |
| `revit_list_levels` | `{}` | Level IDs, names, elevations in mm |
| `revit_list_wall_types` | `{}` | Wall type IDs, names, kind |
| `revit_list_walls` | Optional `limit` 1–200 | Wall IDs, names, types, levels, lengths in mm |
| `revit_list_elements` | Optional `category` display name and `limit` 1–200 | Placed element IDs, unique IDs, names, categories, type IDs |
| `revit_inspect_element` | `elementId` | Element identity and up to 300 instance parameters |
| `revit_create_walls` | 1–50 wall specifications | Approval state and created IDs |
| `revit_set_parameter` | `elementId`, `parameterName`, `value` | Approval state and new raw value |
| `revit_delete_elements` | 1–20 distinct `elementIds` | Approval state, requested and actually deleted IDs |

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

Deletion can remove dependent elements too. The confirmation dialog warns about this, and the result reports every ID that Revit says it deleted. Inspect the project again after deletion.

```json
{"elementIds": [123456]}
```
