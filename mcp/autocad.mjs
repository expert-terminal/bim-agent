import net from 'node:net';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const PIPE = '\\\\.\\pipe\\BimAgent-AutoCAD-2026';
const point = { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }, { type: 'number' }], minItems: 3, maxItems: 3 };
const tools = [
  { name: 'autocad_status', description: 'Get the active AutoCAD drawing and its drawing unit setting.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'autocad_list_layers', description: 'List drawing layers and their locked/frozen state.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'autocad_list_entities', description: 'List up to 200 model-space entities with handles and layers.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'autocad_create_lines', description: 'Create 1–100 3D-capable line entities after AutoCAD approval. Coordinates use native drawing units; call autocad_status first.', inputSchema: { type: 'object', properties: { lines: { type: 'array', minItems: 1, maxItems: 100, items: { type: 'object', properties: { start: point, end: point, layer: { type: 'string' } }, required: ['start', 'end'], additionalProperties: false } } }, required: ['lines'], additionalProperties: false } },
  { name: 'autocad_create_layer', description: 'Create a named drawing layer after AutoCAD approval.', inputSchema: { type: 'object', properties: { name: { type: 'string' } }, required: ['name'], additionalProperties: false } },
  { name: 'autocad_create_box', description: 'Create a 3D solid box after AutoCAD approval. Origin and dimensions use native drawing units; call autocad_status first.', inputSchema: { type: 'object', properties: { origin: point, width: { type: 'number', exclusiveMinimum: 0 }, depth: { type: 'number', exclusiveMinimum: 0 }, height: { type: 'number', exclusiveMinimum: 0 } }, required: ['origin', 'width', 'depth', 'height'], additionalProperties: false } }
];
const actions = Object.fromEntries(tools.map(tool => [tool.name, tool.name.replace(/^autocad_/, '')]));
const aliases = { list_layers: 'layers', list_entities: 'entities' };

function requestOnce(payload) {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(PIPE);
    let buffer = '';
    const timer = setTimeout(() => socket.destroy(new Error('AutoCAD did not respond within 120 seconds.')), 125000);
    socket.setEncoding('utf8');
    socket.on('connect', () => socket.write(JSON.stringify(payload) + '\n'));
    socket.on('data', chunk => {
      buffer += chunk;
      if (buffer.length > 4_000_000) { socket.destroy(new Error('AutoCAD response too large.')); return; }
      const newline = buffer.indexOf('\n');
      if (newline >= 0) {
        clearTimeout(timer);
        socket.end();
        try { resolve(JSON.parse(buffer.slice(0, newline))); } catch (error) { reject(error); }
      }
    });
    socket.on('error', error => { clearTimeout(timer); reject(error); });
    socket.on('end', () => { clearTimeout(timer); if (!buffer.includes('\n')) reject(new Error('AutoCAD disconnected.')); });
  });
}

async function request(payload) {
  for (let i = 0; i < 10; i++) {
    try { return await requestOnce(payload); }
    catch (error) {
      if (!['ENOENT', 'EBUSY', 'ECONNREFUSED'].includes(error.code) || i === 9) throw error;
      await new Promise(resolve => setTimeout(resolve, 75));
    }
  }
}

const send = message => process.stdout.write(JSON.stringify(message) + '\n');
const result = (id, value) => send({ jsonrpc: '2.0', id, result: value });
const error = (id, code, message) => send({ jsonrpc: '2.0', id, error: { code, message } });

export async function handle(message) {
  if (!message || message.jsonrpc !== '2.0' || typeof message.method !== 'string' || message.id === undefined) return;
  try {
    switch (message.method) {
      case 'initialize': result(message.id, { protocolVersion: '2025-11-25', capabilities: { tools: { listChanged: false } }, serverInfo: { name: 'bim-agent-autocad', version: '0.2.0' } }); break;
      case 'ping': result(message.id, {}); break;
      case 'tools/list': result(message.id, { tools }); break;
      case 'tools/call': {
        const action = actions[message.params?.name];
        if (!action) { error(message.id, -32602, 'Unknown tool.'); break; }
        const args = message.params.arguments ?? {};
        if (typeof args !== 'object' || Array.isArray(args)) { error(message.id, -32602, 'Tool arguments must be an object.'); break; }
        const response = await request({ ...args, action: aliases[action] ?? action });
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
