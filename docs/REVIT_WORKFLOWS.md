# Revit architectural modeling workflow

This guide is for an agent helping a new Revit user. Keep the user in control of every model change: Revit displays a short Yes/No approval for each bounded write batch. Reads do not require confirmation. Ask the user to open a disposable or backed-up project first.

## Start with the actual project

1. Call revit_status, revit_list_levels, revit_list_wall_types, revit_list_floor_types, revit_list_roof_types, revit_list_ceiling_types, revit_list_family_types, revit_list_views, and revit_model_health.
2. Record active document, level elevations, units, available types/families, and current warnings. Tool coordinates use millimetres; AutoCAD native drawing units differ.
3. Get a concise architectural brief: footprint and orientation, storey heights, wall build-ups/types, floor and roof construction, room layout, openings, family/type choices, and deliverable views/sheets. State assumptions before proposing coordinates.
4. Choose one coordinate origin and keep every footprint, wall, family, room, roof and annotation position in the same project coordinate system.

## Build a basic architectural model

1. If levels or grids are missing, create them with revit_create_level and revit_create_grid.
2. Create wall batches with revit_create_walls. Each wall is straight and uses a basic wall type. The start and end are XY millimetres; the selected level sets the base elevation.
3. Use revit_create_floors for typed floor slabs from closed XY polygon boundaries. Use revit_create_ceilings for typed ceiling boundaries and height offsets.
4. Create a footprint roof using revit_create_roof. A single slope angle is assigned to every boundary edge, so a closed rectangle produces a hip-style slope pattern; complex gables, dormers and roof-by-face work are not covered.
5. For doors, windows, columns, furniture and other families: call revit_load_family_type on a local .rfa file when necessary, call revit_list_family_types for the loaded type ID, and use revit_place_family_instances. Supply a wall host ID for wall-hosted openings. Revit can reject incompatible family placement conditions; report the error and inspect the host/type rather than guessing.
6. Place rooms with revit_create_room only after room-bounding geometry encloses the target point. Check calculated area with revit_list_rooms.
7. Inspect created objects with revit_list_elements and revit_inspect_element. Check revit_model_health again and report unresolved warnings.

## Prepare views, schedules and sheets

- Create floor/ceiling/structural plan views with revit_create_plan_view and isometric views with revit_create_3d_view. Use revit_set_view_scale and revit_set_view_visibility to tune existing non-template views.
- Place loaded annotation family tags with revit_tag_elements. The type must be a tag family that Revit can use for the target category and view.
- Discover schedule categories and fields with revit_list_schedule_categories and revit_list_schedule_fields, then create a typed schedule with revit_create_schedule.
- Discover title blocks with revit_list_titleblock_types. Create a sheet with revit_create_sheet and its optional title block type ID, then place views using revit_place_view_on_sheet.
- Export selected sheets as a combined PDF using revit_export_sheets_pdf, or export selected views/sheets as DWG using revit_export_views_dwg. These write external files, so they are separately confirmed and cannot be undone from Revit's Undo menu.

## Edit and recover

revit_set_parameter edits one unambiguous writable instance or type parameter. revit_change_element_type changes one element's type. revit_move_elements, revit_rotate_elements, and revit_copy_elements transform selected IDs. revit_delete_elements previews dependent deletions before asking the user. Every committed model transaction is undoable in Revit; if any operation fails, the transaction rolls back and the agent must reread the model before retrying.

## Scope limits

This workflow can create useful native BIM content from a text brief, but it cannot infer code-compliant design, complete construction documents, consultant coordination, or project standards from a prompt alone. The current adapter does not create dimensions, detailed linework, elevations/sections, custom parametric families, stairs, curtain panels, MEP systems, or linked-model coordination. Tags, title blocks, family content, templates and types must already be available in the project or supplied from a local library. A qualified architect/BIM professional must review geometry, dimensions, annotations, schedules, warnings and exported deliverables before issue.

Autodesk's API provides the underlying host features for [family placement](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/168d4c67-73dd-d7c8-4969-12846eeaddfa.htm), [floors](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Revit_Geometric_Elements/Walls_Floors_Ceilings_Roofs_and_Openings/Revit_API_Revit_API_Developers_Guide_Revit_Geometric_Elements_Walls_Floors_Ceilings_Roofs_and_Openings_Floors_Ceilings_and_Foundations_html.html), [ceilings](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/63030aa3-d58d-b830-db18-0882c5990507.htm), [footprint roofs](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/110741bf-b041-9f78-0832-ec9f5892cebc.htm), [tags](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/b8e8eec2-8e3b-08f2-a9a5-89f24465c8b9.htm), [schedules](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/647440c1-dafc-61ba-0856-826fa408de9d.htm), and [PDF export](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/93d66d57-c20e-a103-39a1-77bc2ea05183.htm). BIM-agent implements a bounded subset of these operations.
