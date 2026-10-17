import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import net from 'node:net';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';

const configPath = path.join(process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'BIM-agent') : path.join(os.homedir(), '.bim-agent'), 'sketchup-bridge.json');
const xy = { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 };
const xyz = { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 };
const tools = [
  { name: 'sketchup_status', description: 'Get the active SketchUp model and version.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'sketchup_list_groups', description: 'List up to 200 root-level SketchUp groups.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'sketchup_list_components', description: 'List root-level SketchUp component instances.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'sketchup_list_materials', description: 'List model materials with color and alpha values.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'sketchup_list_tags', description: 'List SketchUp tags and their visibility/lock state.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'sketchup_list_scenes', description: 'List saved SketchUp scenes and persistent IDs.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'sketchup_create_face', description: 'Create a named planar surface grouped for editing from 3–100 XYZ points in millimetres.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, pointsMm: { type: 'array', minItems: 3, maxItems: 100, items: xyz } }, required: ['pointsMm'], additionalProperties: false } },
  { name: 'sketchup_create_dimension', description: 'Create a linear dimension between two XYZ points with an XYZ offset vector in millimetres.', inputSchema: { type: 'object', properties: { startMm: xyz, endMm: xyz, offsetMm: xyz }, required: ['startMm', 'endMm', 'offsetMm'], additionalProperties: false } },
  { name: 'sketchup_create_scene', description: 'Save the current camera and display state as a named SketchUp scene.', inputSchema: { type: 'object', properties: { name: { type: 'string', minLength: 1, maxLength: 100 } }, required: ['name'], additionalProperties: false } },
  { name: 'sketchup_create_section_plane', description: 'Create and activate a section plane from an XYZ point in millimetres and a normal vector.', inputSchema: { type: 'object', properties: { pointMm: xyz, normal: xyz }, required: ['pointMm', 'normal'], additionalProperties: false } },
  { name: 'sketchup_delete_section_plane', description: 'Delete a section plane by persistent ID after confirmation; removal deactivates that cut plane.', inputSchema: { type: 'object', properties: { persistentId: { type: 'integer' } }, required: ['persistentId'], additionalProperties: false } },
  { name: 'sketchup_set_camera', description: 'Set the active view camera from XYZ eye, target and up vectors; eye and target use millimetres.', inputSchema: { type: 'object', properties: { eyeMm: xyz, targetMm: xyz, up: xyz, perspective: { type: 'boolean' } }, required: ['eyeMm', 'targetMm', 'up'], additionalProperties: false } },
  { name: 'sketchup_create_edges', description: 'Create 1–50 connected 3D edge paths, each 2–200 XYZ points in millimetres.', inputSchema: { type: 'object', properties: { runs: { type: 'array', minItems: 1, maxItems: 50, items: { type: 'array', minItems: 2, maxItems: 200, items: xyz } } }, required: ['runs'], additionalProperties: false } },
  { name: 'sketchup_create_circle', description: 'Create a circle with a 3D center and normal, millimetre radius, and optional segment count.', inputSchema: { type: 'object', properties: { centerMm: xyz, normal: xyz, radiusMm: { type: 'number', exclusiveMinimum: 0 }, segments: { type: 'integer', minimum: 8, maximum: 256 } }, required: ['centerMm', 'normal', 'radiusMm'], additionalProperties: false } },
  { name: 'sketchup_create_arc', description: 'Create an arc with center, axes, radius in millimetres and increasing radian angles.', inputSchema: { type: 'object', properties: { centerMm: xyz, normal: xyz, xAxis: xyz, radiusMm: { type: 'number', exclusiveMinimum: 0 }, startRadians: { type: 'number' }, endRadians: { type: 'number' }, segments: { type: 'integer', minimum: 2, maximum: 256 } }, required: ['centerMm', 'normal', 'xAxis', 'radiusMm', 'startRadians', 'endRadians'], additionalProperties: false } },
  { name: 'sketchup_create_cylinder', description: 'Create a grouped vertical cylinder from a base-center point with millimetre radius and height.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, baseCenterMm: xyz, radiusMm: { type: 'number', exclusiveMinimum: 0 }, heightMm: { type: 'number', exclusiveMinimum: 0 }, segments: { type: 'integer', minimum: 8, maximum: 256 } }, required: ['baseCenterMm', 'radiusMm', 'heightMm'], additionalProperties: false } },
  { name: 'sketchup_create_extrusion', description: 'Create a named group by extruding a planar footprint. Coordinates and height are millimetres. SketchUp asks for approval.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, pointsMm: { type: 'array', minItems: 3, maxItems: 50, items: xy }, zMm: { type: 'number' }, heightMm: { type: 'number', minimum: 1 } }, required: ['pointsMm', 'heightMm'], additionalProperties: false } },
  { name: 'sketchup_create_component', description: 'Create a reusable component definition by extruding an XY footprint in millimetres.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, pointsMm: { type: 'array', minItems: 3, maxItems: 50, items: xy }, zMm: { type: 'number' }, heightMm: { type: 'number', minimum: 1 } }, required: ['pointsMm', 'heightMm'], additionalProperties: false } },
  { name: 'sketchup_create_box', description: 'Create a named grouped box with millimetre origin and dimensions.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, originMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 }, widthMm: { type: 'number', minimum: 1 }, depthMm: { type: 'number', minimum: 1 }, heightMm: { type: 'number', minimum: 1 } }, required: ['originMm', 'widthMm', 'depthMm', 'heightMm'], additionalProperties: false } },
  { name: 'sketchup_create_material', description: 'Create or update a SketchUp material using RGB and optional alpha values.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, rgb: { type: 'array', prefixItems: [{ type: 'integer', minimum: 0, maximum: 255 }, { type: 'integer', minimum: 0, maximum: 255 }, { type: 'integer', minimum: 0, maximum: 255 }], minItems: 3, maxItems: 3 }, alpha: { type: 'integer', minimum: 0, maximum: 255 } }, required: ['name', 'rgb'], additionalProperties: false } },
  { name: 'sketchup_assign_material', description: 'Assign an existing material to a group by persistent ID.', inputSchema: { type: 'object', properties: { persistentId: { type: 'integer' }, materialName: { type: 'string' } }, required: ['persistentId', 'materialName'], additionalProperties: false } },
  { name: 'sketchup_create_tag', description: 'Create a named SketchUp tag.', inputSchema: { type: 'object', properties: { name: { type: 'string' } }, required: ['name'], additionalProperties: false } },
  { name: 'sketchup_assign_tag', description: 'Assign a named existing tag to a group by persistent ID.', inputSchema: { type: 'object', properties: { persistentId: { type: 'integer' }, tagName: { type: 'string' } }, required: ['persistentId', 'tagName'], additionalProperties: false } },
  { name: 'sketchup_move_group', description: 'Move a group by an XYZ vector in millimetres.', inputSchema: { type: 'object', properties: { persistentId: { type: 'integer' }, deltaMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 } }, required: ['persistentId', 'deltaMm'], additionalProperties: false } },
  { name: 'sketchup_rotate_group', description: 'Rotate a group around a vertical axis by degrees at an XYZ pivot in millimetres.', inputSchema: { type: 'object', properties: { persistentId: { type: 'integer' }, pivotMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 }, degrees: { type: 'number' } }, required: ['persistentId', 'pivotMm', 'degrees'], additionalProperties: false } },
  { name: 'sketchup_copy_group', description: 'Copy a group by an XYZ displacement in millimetres.', inputSchema: { type: 'object', properties: { persistentId: { type: 'integer' }, deltaMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 } }, required: ['persistentId', 'deltaMm'], additionalProperties: false } },
  { name: 'sketchup_delete_group', description: 'Delete one group by persistent ID after SketchUp approval.', inputSchema: { type: 'object', properties: { persistentId: { type: 'integer' } }, required: ['persistentId'], additionalProperties: false } }
];
const actions = { sketchup_status: 'status', sketchup_list_groups: 'groups', sketchup_list_components: 'components', sketchup_list_materials: 'materials', sketchup_list_tags: 'tags', sketchup_list_scenes: 'scenes', sketchup_create_face: 'create_face', sketchup_create_dimension: 'create_dimension', sketchup_create_scene: 'create_scene', sketchup_create_section_plane: 'create_section_plane', sketchup_delete_section_plane: 'delete_section_plane', sketchup_set_camera: 'set_camera', sketchup_create_edges: 'create_edges', sketchup_create_circle: 'create_circle', sketchup_create_arc: 'create_arc', sketchup_create_cylinder: 'create_cylinder', sketchup_create_extrusion: 'create_extrusion', sketchup_create_component: 'create_component', sketchup_create_box: 'create_box', sketchup_create_material: 'create_material', sketchup_assign_material: 'assign_material', sketchup_create_tag: 'create_tag', sketchup_assign_tag: 'assign_tag', sketchup_move_group: 'move_group', sketchup_rotate_group: 'rotate_group', sketchup_copy_group: 'copy_group', sketchup_delete_group: 'delete_group' };

async function request(args) {
  const config = JSON.parse(await fs.readFile(configPath, 'utf8'));
  if (!Number.isInteger(config.port) || config.port < 1 || config.port > 65535 || typeof config.token !== 'string')
    throw new Error('Invalid SketchUp bridge configuration.');
  return new Promise((resolve, reject) => {
    const socket = net.createConnection({ host: '127.0.0.1', port: config.port });
    let buffer = '';
    const timer = setTimeout(() => socket.destroy(new Error('SketchUp did not respond within 120 seconds.')), 125000);
    socket.setEncoding('utf8');
    socket.on('connect', () => socket.write(JSON.stringify({ ...args, token: config.token }) + '\n'));
    socket.on('data', chunk => {
      buffer += chunk;
      if (buffer.length > 4_000_000) { socket.destroy(new Error('SketchUp response too large.')); return; }
      const newline = buffer.indexOf('\n');
      if (newline >= 0) {
        clearTimeout(timer);
        socket.end();
        try { resolve(JSON.parse(buffer.slice(0, newline))); } catch (error) { reject(error); }
      }
    });
    socket.on('error', error => { clearTimeout(timer); reject(error); });
    socket.on('end', () => { clearTimeout(timer); if (!buffer.includes('\n')) reject(new Error('SketchUp disconnected.')); });
  });
}

const send = message => process.stdout.write(JSON.stringify(message) + '\n');
const result = (id, value) => send({ jsonrpc: '2.0', id, result: value });
const error = (id, code, message) => send({ jsonrpc: '2.0', id, error: { code, message } });

export async function handle(message) {
  if (!message || message.jsonrpc !== '2.0' || typeof message.method !== 'string' || message.id === undefined) return;
  try {
    switch (message.method) {
      case 'initialize': result(message.id, { protocolVersion: '2025-11-25', capabilities: { tools: { listChanged: false } }, serverInfo: { name: 'bim-agent-sketchup', version: '0.4.0' } }); break;
      case 'ping': result(message.id, {}); break;
      case 'tools/list': result(message.id, { tools }); break;
      case 'tools/call': {
        const action = actions[message.params?.name];
        if (!action) { error(message.id, -32602, 'Unknown tool.'); break; }
        const args = message.params.arguments ?? {};
        if (typeof args !== 'object' || Array.isArray(args)) { error(message.id, -32602, 'Tool arguments must be an object.'); break; }
        const response = await request({ ...args, action });
        result(message.id, { content: [{ type: 'text', text: JSON.stringify(response.ok ? response.result : { error: response.error }) }], isError: !response.ok });
        break;
      }
      default: error(message.id, -32601, 'Unknown method.');
    }
  } catch (cause) { result(message.id, { content: [{ type: 'text', text: String(cause.message ?? cause) }], isError: true }); }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const input = readline.createInterface({ input: process.stdin, crlfDelay: Infinity });
  input.on('line', line => { try { void handle(JSON.parse(line)); } catch { error(null, -32700, 'Invalid JSON'); } });
}
