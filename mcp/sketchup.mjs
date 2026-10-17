import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import net from 'node:net';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';

const configPath = path.join(process.env.LOCALAPPDATA ? path.join(process.env.LOCALAPPDATA, 'BIM-agent') : path.join(os.homedir(), '.bim-agent'), 'sketchup-bridge.json');
const xy = { type: 'array', prefixItems: [{ type: 'number' }, { type: 'number' }], minItems: 2, maxItems: 2 };
const tools = [
  { name: 'sketchup_status', description: 'Get the active SketchUp model and version.', inputSchema: { type: 'object', properties: {}, additionalProperties: false } },
  { name: 'sketchup_list_groups', description: 'List up to 200 root-level SketchUp groups.', inputSchema: { type: 'object', properties: { limit: { type: 'integer', minimum: 1, maximum: 200 } }, additionalProperties: false } },
  { name: 'sketchup_create_extrusion', description: 'Create a named group by extruding a planar footprint. Coordinates and height are millimetres. SketchUp asks for approval.', inputSchema: { type: 'object', properties: { name: { type: 'string' }, pointsMm: { type: 'array', minItems: 3, maxItems: 50, items: xy }, zMm: { type: 'number' }, heightMm: { type: 'number', minimum: 1 } }, required: ['pointsMm', 'heightMm'], additionalProperties: false } },
  { name: 'sketchup_delete_group', description: 'Delete one group by persistent ID after SketchUp approval.', inputSchema: { type: 'object', properties: { persistentId: { type: 'integer' } }, required: ['persistentId'], additionalProperties: false } }
];
const actions = { sketchup_status: 'status', sketchup_list_groups: 'groups', sketchup_create_extrusion: 'create_extrusion', sketchup_delete_group: 'delete_group' };

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
      case 'initialize': result(message.id, { protocolVersion: '2025-11-25', capabilities: { tools: { listChanged: false } }, serverInfo: { name: 'bim-agent-sketchup', version: '0.2.0' } }); break;
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
