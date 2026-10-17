import net from 'node:net';
import readline from 'node:readline';

const PIPE = '\\\\.\\pipe\\BimAgent-2026';
const VERSION = '2025-11-25';
const tools = [
  { name: 'revit_status', description: 'Get the active Revit document and connection status.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_levels', description: 'List levels with IDs and elevations in millimetres.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_wall_types', description: 'List wall types with IDs; use a basic wall type for creation.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'revit_list_views', description: 'List up to 200 non-template views in the active project.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_sheets', description: 'List up to 200 project sheets.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_family_types', description: 'List up to 200 loadable family types, optionally filtered by category display name.', inputSchema: { type: 'object', properties: { category: { type: 'string' }, limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_walls', description: 'List up to 200 existing walls in the active document.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_list_elements', description: 'Find up to 200 placed model elements, optionally by exact category display name.', inputSchema: { type: 'object', properties: { category: { type: 'string' }, limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'revit_inspect_element', description: 'Inspect a placed element and up to 300 of its parameters.', inputSchema: { type: 'object', properties: { elementId: { type: 'integer' } }, required: ['elementId'], additionalProperties: false } },
  { name: 'revit_create_walls', description: 'Create 1–50 straight walls in the active Revit model. Coordinates and heights are millimetres. The Revit user must approve a dialog before the transaction commits.', inputSchema: { type: 'object', properties: { walls: { type: 'array', minItems: 1, maxItems: 50, items: { type: 'object', properties: { startMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 }, endMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 }, levelId: { type: 'integer' }, wallTypeId: { type: 'integer' }, heightMm: { type: 'number', minimum: 100 } }, required: ['startMm', 'endMm', 'levelId', 'wallTypeId', 'heightMm'], additionalProperties: false } } }, required: ['walls'], additionalProperties: false } },
  { name: 'revit_create_level', description: 'Create a level at an elevation in millimetres after Revit user approval.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, elevationMm: { type: 'number' } }, required: ['name', 'elevationMm'], additionalProperties: false } },
  { name: 'revit_create_grid', description: 'Create a straight grid from XY endpoints in millimetres after Revit user approval.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, startMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 }, endMm: { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 } }, required: ['name', 'startMm', 'endMm'], additionalProperties: false } },
  { name: 'revit_create_sheet', description: 'Create a sheet without a title block after Revit user approval.', inputSchema: { type: 'object', properties: { number: { type: 'string' }, name: { type: 'string' } }, required: ['number', 'name'], additionalProperties: false } },
  { name: 'revit_set_parameter', description: 'Set one writable instance parameter after Revit user approval. String parameters take text, integer parameters take an integer, and double parameters take a unit-formatted string in Revit project units (for example "300 mm"). Inspect the element first.', inputSchema: { type: 'object', properties: { elementId: { type: 'integer' }, parameterName: { type: 'string' }, value: { oneOf: [{ type: 'string' }, { type: 'integer' }] } }, required: ['elementId', 'parameterName', 'value'], additionalProperties: false } },
  { name: 'revit_delete_elements', description: 'Delete 1–20 placed elements after Revit user approval. Revit may also delete dependent elements; inspect and verify before and after.', inputSchema: { type: 'object', properties: { elementIds: { type: 'array', minItems: 1, maxItems: 20, items: { type: 'integer' } } }, required: ['elementIds'], additionalProperties: false } },
];

const actionByTool = { revit_status: 'status', revit_list_levels: 'levels', revit_list_wall_types: 'wall_types', revit_list_views: 'views', revit_list_sheets: 'sheets', revit_list_family_types: 'family_types', revit_list_walls: 'walls', revit_list_elements: 'elements', revit_inspect_element: 'element', revit_create_walls: 'create_walls', revit_create_level: 'create_level', revit_create_grid: 'create_grid', revit_create_sheet: 'create_sheet', revit_set_parameter: 'set_parameter', revit_delete_elements: 'delete_elements' };

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
        reply(message.id, { protocolVersion: VERSION, capabilities: { tools: { listChanged: false } }, serverInfo: { name: 'bim-agent', version: '0.1.0' } });
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
