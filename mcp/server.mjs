import net from 'node:net';
import readline from 'node:readline';

const PIPE = '\\\\.\\pipe\\BimAgent-2026';
const VERSION = '2025-11-25';
const tools = [
  { name: 'revit_status', description: 'Get the active Revit document and connection status.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_levels', description: 'List levels with IDs and elevations in millimetres.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_wall_types', description: 'List wall types with IDs; use a basic wall type for creation.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_floor_types', description: 'List floor types with IDs for floor creation.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_ceiling_types', description: 'List ceiling types with IDs for ceiling creation.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_roof_types', description: 'List footprint roof types with IDs for roof creation.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_view_types', description: 'List view family types with IDs and view families for creating plan views.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_titleblock_types', description: 'List title block family types available in the project.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_rooms', description: 'List placed rooms with level, number, area and location.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_schedule_categories', description: 'List categories supported by Revit schedules.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_schedule_fields', description: 'List schedulable parameter names for a category ID.', inputSchema: { type: 'object', properties: { categoryId: { type: 'integer' } }, required: ['categoryId'], additionalProperties: false } },
  { name: 'revit_create_schedule', description: 'Create a Revit schedule for a category and ordered list of schedulable field names.', inputSchema: { type: 'object', properties: { categoryId: { type: 'integer' }, name: { type: 'string' }, fieldNames: { type: 'array', minItems: 1, maxItems: 30, items: { type: 'string' } } }, required: ['categoryId', 'name', 'fieldNames'], additionalProperties: false } },
  { name: 'revit_list_materials', description: 'List project materials and RGB colors.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_model_health', description: 'Read Revit model warnings and affected element IDs.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 100 } }, additionalProperties: false } },
  { name: 'revit_list_views', description: 'List up to 200 non-template views in the active project.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_sheets', description: 'List up to 200 project sheets.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_family_types', description: 'List up to 200 loadable family types, optionally filtered by category display name.', inputSchema: { type: 'object', properties: { category: { type: 'string' }, limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_walls', description: 'List up to 200 existing walls in the active document.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_elements', description: 'Find up to 200 placed model elements, optionally by exact category display name.', inputSchema: { type: 'object', properties: { category: { type: 'string' }, limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_inspect_element', description: 'Inspect a placed element and up to 300 of its parameters.', inputSchema: { type: 'object', properties: { elementId: { type: 'integer' } }, required: ['elementId'], additionalProperties: false } },
  { name: 'revit_create_walls', description: 'Create 1–50 straight walls in the active Revit model. Coordinates and heights are millimetres. The Revit user must approve a dialog before the transaction commits.', inputSchema: { type: 'object', properties: { walls: { type: 'array', minItems: 1, maxItems: 50, items: { type: 'object', properties: { startMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 }, endMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 }, levelId: { type: 'integer' }, wallTypeId: { type: 'integer' }, heightMm: { type: 'number', minimum: 100 } }, required: ['startMm', 'endMm', 'levelId', 'wallTypeId', 'heightMm'], additionalProperties: false } } }, required: ['walls'], additionalProperties: false } },
  { name: 'revit_create_floors', description: 'Create 1–20 typed Revit floors from closed XY polygons. Points and levels use millimetres; boundaries must be planar and non-self-intersecting. Optional offsetMm sets height above the level. One approval precedes the transaction.', inputSchema: { type: 'object', properties: { floors: { type: 'array', minItems: 1, maxItems: 20, items: { type: 'object', properties: { floorTypeId: { type: 'integer' }, levelId: { type: 'integer' }, pointsMm: { type: 'array', minItems: 3, maxItems: 100, items: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 } }, offsetMm: { type: 'number' } }, required: ['floorTypeId', 'levelId', 'pointsMm'], additionalProperties: false } } }, required: ['floors'], additionalProperties: false } },
  { name: 'revit_create_ceilings', description: 'Create 1–20 typed ceilings from closed XY polygons at selected levels with optional heightAboveLevelMm offsets. Revit user approval is required.', inputSchema: { type: 'object', properties: { ceilings: { type: 'array', minItems: 1, maxItems: 20, items: { type: 'object', properties: { ceilingTypeId: { type: 'integer' }, levelId: { type: 'integer' }, heightAboveLevelMm: { type: 'number' }, pointsMm: { type: 'array', minItems: 3, maxItems: 100, items: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 } } }, required: ['ceilingTypeId', 'levelId', 'pointsMm'], additionalProperties: false } } }, required: ['ceilings'], additionalProperties: false } },
  { name: 'revit_create_roof', description: 'Create a footprint roof from an XY polygon at a selected level and type. Slope angle applies to all footprint edges; Revit user approval is required.', inputSchema: { type: 'object', properties: { roofTypeId: { type: 'integer' }, levelId: { type: 'integer' }, pointsMm: { type: 'array', minItems: 3, maxItems: 100, items: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 } }, slopeDegrees: { type: 'number', minimum: 0, maximum: 89 } }, required: ['roofTypeId', 'levelId', 'pointsMm', 'slopeDegrees'], additionalProperties: false } },
  { name: 'revit_place_family_instances', description: 'Place 1–50 compatible family instances using project family type IDs, level IDs and XYZ millimetre coordinates. Optional hostWallId supports wall-hosted doors/windows. Revit user approval is required.', inputSchema: { type: 'object', properties: { instances: { type: 'array', minItems: 1, maxItems: 50, items: { type: 'object', properties: { familyTypeId: { type: 'integer' }, levelId: { type: 'integer' }, pointMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 }, hostWallId: { type: 'integer' } }, required: ['familyTypeId', 'levelId', 'pointMm'], additionalProperties: false } } }, required: ['instances'], additionalProperties: false } },
  { name: 'revit_load_family_type', description: 'Load one named family type from a local .rfa file after Revit approval; returns its type ID for placement.', inputSchema: { type: 'object', properties: { path: { type: 'string' }, typeName: { type: 'string' } }, required: ['path', 'typeName'], additionalProperties: false } },
  { name: 'revit_create_plan_view', description: 'Create a floor, ceiling, or engineering plan view from a view family type and level.', inputSchema: { type: 'object', properties: { viewFamilyTypeId: { type: 'integer' }, levelId: { type: 'integer' }, name: { type: 'string' } }, required: ['viewFamilyTypeId', 'levelId', 'name'], additionalProperties: false } },
  { name: 'revit_create_3d_view', description: 'Create an isometric 3D view using a project 3D view family type.', inputSchema: { type: 'object', properties: { viewFamilyTypeId: { type: 'integer' }, name: { type: 'string' } }, required: ['viewFamilyTypeId', 'name'], additionalProperties: false } },
  { name: 'revit_set_view_scale', description: 'Set a view scale such as 1:100 using the integer 100.', inputSchema: { type: 'object', properties: { viewId: { type: 'integer' }, scale: { type: 'integer', minimum: 1, maximum: 10000 } }, required: ['viewId', 'scale'], additionalProperties: false } },
  { name: 'revit_set_view_visibility', description: 'Hide or unhide 1–100 elements in one view after approval.', inputSchema: { type: 'object', properties: { viewId: { type: 'integer' }, elementIds: { type: 'array', minItems: 1, maxItems: 100, items: { type: 'integer' } }, hidden: { type: 'boolean' } }, required: ['viewId', 'elementIds', 'hidden'], additionalProperties: false } },
  { name: 'revit_tag_elements', description: 'Place 1–50 instances of one loaded annotation tag type in a view at XYZ millimetre head points.', inputSchema: { type: 'object', properties: { viewId: { type: 'integer' }, tagTypeId: { type: 'integer' }, tags: { type: 'array', minItems: 1, maxItems: 50, items: { type: 'object', properties: { elementId: { type: 'integer' }, headMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 } }, required: ['elementId', 'headMm'], additionalProperties: false } } }, required: ['viewId', 'tagTypeId', 'tags'], additionalProperties: false } },
  { name: 'revit_place_view_on_sheet', description: 'Place an unplaced view on a sheet at a sheet-origin XY position in millimetres.', inputSchema: { type: 'object', properties: { sheetId: { type: 'integer' }, viewId: { type: 'integer' }, xMm: { type: 'number' }, yMm: { type: 'number' } }, required: ['sheetId', 'viewId', 'xMm', 'yMm'], additionalProperties: false } },
  { name: 'revit_duplicate_view', description: 'Duplicate a view, optionally including detailing, and assign the requested name.', inputSchema: { type: 'object', properties: { viewId: { type: 'integer' }, name: { type: 'string' }, withDetailing: { type: 'boolean' } }, required: ['viewId', 'name'], additionalProperties: false } },
  { name: 'revit_move_elements', description: 'Move 1–50 elements by an XYZ vector in millimetres after user approval.', inputSchema: { type: 'object', properties: { elementIds: { type: 'array', minItems: 1, maxItems: 50, items: { type: 'integer' } }, deltaMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 } }, required: ['elementIds', 'deltaMm'], additionalProperties: false } },
  { name: 'revit_rotate_elements', description: 'Rotate 1–50 elements about a vertical axis at an XYZ pivot in millimetres.', inputSchema: { type: 'object', properties: { elementIds: { type: 'array', minItems: 1, maxItems: 50, items: { type: 'integer' } }, pivotMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 }, degrees: { type: 'number' } }, required: ['elementIds', 'pivotMm', 'degrees'], additionalProperties: false } },
  { name: 'revit_copy_elements', description: 'Copy 1–50 elements by an XYZ vector in millimetres; Revit returns the new element IDs.', inputSchema: { type: 'object', properties: { elementIds: { type: 'array', minItems: 1, maxItems: 50, items: { type: 'integer' } }, deltaMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 } }, required: ['elementIds', 'deltaMm'], additionalProperties: false } },
  { name: 'revit_change_element_type', description: 'Change one element to an existing element type ID after user approval.', inputSchema: { type: 'object', properties: { elementId: { type: 'integer' }, typeId: { type: 'integer' } }, required: ['elementId', 'typeId'], additionalProperties: false } },
  { name: 'revit_create_room', description: 'Place one room on a level at an XY point in millimetres. Revit must find an enclosed room boundary.', inputSchema: { type: 'object', properties: { levelId: { type: 'integer' }, pointMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 } }, required: ['levelId', 'pointMm'], additionalProperties: false } },
  { name: 'revit_export_sheets_pdf', description: 'Export selected Revit sheets to a combined PDF file after explicit approval. This file write cannot be undone by Revit Undo.', inputSchema: { type: 'object', properties: { sheetIds: { type: 'array', minItems: 1, maxItems: 100, items: { type: 'integer' } }, folder: { type: 'string' }, fileName: { type: 'string' } }, required: ['sheetIds', 'folder', 'fileName'], additionalProperties: false } },
  { name: 'revit_export_views_dwg', description: 'Export selected views/sheets as DWG files after explicit approval. Export file writes cannot be undone by Revit Undo.', inputSchema: { type: 'object', properties: { viewIds: { type: 'array', minItems: 1, maxItems: 100, items: { type: 'integer' } }, folder: { type: 'string' }, filePrefix: { type: 'string' } }, required: ['viewIds', 'folder', 'filePrefix'], additionalProperties: false } },
  { name: 'revit_create_level', description: 'Create a level at an elevation in millimetres after Revit user approval.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, elevationMm: { type: 'number' } }, required: ['name', 'elevationMm'], additionalProperties: false } },
  { name: 'revit_create_grid', description: 'Create a straight grid from XY endpoints in millimetres after Revit user approval.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, startMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 }, endMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 } }, required: ['name', 'startMm', 'endMm'], additionalProperties: false } },
  { name: 'revit_create_sheet', description: 'Create a sheet with an optional loaded title block family type ID after Revit user approval.', inputSchema: { type: 'object', properties: { number: { type: 'string' }, name: { type: 'string' }, titleBlockTypeId: { type: 'integer' } }, required: ['number', 'name'], additionalProperties: false } },
  { name: 'revit_set_parameter', description: 'Set one writable instance parameter after Revit user approval. String parameters take text, integer parameters take an integer, and double parameters take a unit-formatted string in Revit project units (for example "300 mm"). Inspect the element first.', inputSchema: { type: 'object', properties: { elementId: { type: 'integer' }, parameterName: { type: 'string' }, value: { oneOf: [{ type: 'string' }, { type: 'integer' }] } }, required: ['elementId', 'parameterName', 'value'], additionalProperties: false } },
  { name: 'revit_delete_elements', description: 'Delete 1–20 placed elements after Revit user approval. Revit may also delete dependent elements; inspect and verify before and after.', inputSchema: { type: 'object', properties: { elementIds: { type: 'array', minItems: 1, maxItems: 20, items: { type: 'integer' } } }, required: ['elementIds'], additionalProperties: false } },
];

const actionByTool = { revit_status: 'status', revit_list_levels: 'levels', revit_list_wall_types: 'wall_types', revit_list_floor_types: 'floor_types', revit_list_ceiling_types: 'ceiling_types', revit_list_roof_types: 'roof_types', revit_list_view_types: 'view_types', revit_list_titleblock_types: 'titleblock_types', revit_list_rooms: 'rooms', revit_list_schedule_categories: 'schedule_categories', revit_list_schedule_fields: 'schedule_fields', revit_create_schedule: 'create_schedule', revit_list_materials: 'materials', revit_model_health: 'model_health', revit_list_views: 'views', revit_list_sheets: 'sheets', revit_list_family_types: 'family_types', revit_list_walls: 'walls', revit_list_elements: 'elements', revit_inspect_element: 'element', revit_create_walls: 'create_walls', revit_load_family_type: 'load_family_type', revit_create_floors: 'create_floors', revit_create_ceilings: 'create_ceilings', revit_create_roof: 'create_roof', revit_place_family_instances: 'place_family_instances', revit_create_plan_view: 'create_plan_view', revit_create_3d_view: 'create_3d_view', revit_set_view_scale: 'set_view_scale', revit_set_view_visibility: 'set_view_visibility', revit_tag_elements: 'tag_elements', revit_place_view_on_sheet: 'place_view_on_sheet', revit_duplicate_view: 'duplicate_view', revit_move_elements: 'move_elements', revit_rotate_elements: 'rotate_elements', revit_copy_elements: 'copy_elements', revit_change_element_type: 'change_element_type', revit_create_room: 'create_room', revit_export_sheets_pdf: 'export_sheets_pdf', revit_export_views_dwg: 'export_views_dwg', revit_create_level: 'create_level', revit_create_grid: 'create_grid', revit_create_sheet: 'create_sheet', revit_set_parameter: 'set_parameter', revit_delete_elements: 'delete_elements' };

function callRevitOnce(request, timeoutMs) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(PIPE);
    let buffer = '';
    const timer = setTimeout(() => socket.destroy(new Error('Timed out waiting for Revit. Check for an approval dialog.')), timeoutMs);
    socket.setEncoding('utf8');
    socket.on('connect', () => socket.write(JSON.stringify(request) + '\n'));
    socket.on('data', chunk => {
      buffer += chunk;
      if (buffer.length > 4_000_000) { socket.destroy(new Error('Revit response too large.')); return; }
      const newline = buffer.indexOf('\n');
      if (newline >= 0) {
        clearTimeout(timer);
        socket.end();
        try { resolve(JSON.parse(buffer.slice(0, newline))); } catch (error) { reject(error); }
      }
    });
    socket.on('error', error => { clearTimeout(timer); reject(error); });
    socket.on('end', () => { clearTimeout(timer); if (!buffer.includes('\n')) reject(new Error('Revit disconnected before responding.')); });
  });
}

export async function callRevit(request, timeoutMs = 125000) {
  for (let attempt = 0; attempt < 10; attempt++) {
    try { return await callRevitOnce(request, timeoutMs); }
    catch (error) {
      if (!['ENOENT', 'EBUSY', 'ECONNREFUSED'].includes(error.code) || attempt === 9) throw error;
      await new Promise(resolve => setTimeout(resolve, 75));
    }
  }
}

function send(message) { process.stdout.write(JSON.stringify(message) + '\n'); }
function reply(id, result) { send({ jsonrpc: '2.0', id, result }); }
function error(id, code, message) { send({ jsonrpc: '2.0', id, error: { code, message } }); }

export async function handle(message) {
  if (!message || message.jsonrpc !== '2.0' || typeof message.method !== 'string') return;
  if (message.id === undefined) return;
  try {
    switch (message.method) {
      case 'initialize':
        reply(message.id, { protocolVersion: VERSION, capabilities: { tools: { listChanged: false } }, serverInfo: { name: 'bim-agent', version: '0.3.0' } });
        break;
      case 'ping': reply(message.id, {}); break;
      case 'tools/list': reply(message.id, { tools }); break;
      case 'tools/call': {
        const name = message.params?.name;
        const action = actionByTool[name];
        if (!action) { error(message.id, -32602, `Unknown tool: ${name}`); break; }
        const args = message.params.arguments ?? {};
        if (typeof args !== 'object' || Array.isArray(args)) { error(message.id, -32602, 'Tool arguments must be an object.'); break; }
        const result = await callRevit({ ...args, action });
        reply(message.id, { content: [{ type: 'text', text: JSON.stringify(result.ok ? result.result : { error: result.error }) }], isError: !result.ok });
        break;
      }
      default: error(message.id, -32601, `Unknown method: ${message.method}`);
    }
  } catch (cause) {
    if (message.method === 'tools/call') reply(message.id, { content: [{ type: 'text', text: String(cause.message ?? cause) }], isError: true });
    else error(message.id, -32603, String(cause.message ?? cause));
  }
}

if (process.argv[1] && import.meta.url === new URL(`file:///${process.argv[1].replaceAll('\\', '/')}`).href) {
  const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
  input.on('line', line => { try { void handle(JSON.parse(line)); } catch { error(null, -32700, 'Invalid JSON'); } });
}
