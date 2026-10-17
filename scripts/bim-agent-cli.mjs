#!/usr/bin/env node
// Small shell entry point for agents whose host does not yet expose MCP clients.
import { spawn } from 'node:child_process';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const servers = { revit: 'server.mjs', autocad: 'autocad.mjs', sketchup: 'sketchup.mjs' };
const [host, toolName, argsFile] = process.argv.slice(2);
if (!servers[host] || !toolName) {
  console.error('Usage: node scripts/bim-agent-cli.mjs <revit|autocad|sketchup> <list|tool_name> [arguments.json]');
  process.exit(2);
}
const server = spawn(process.execPath, [path.join(root, 'mcp', servers[host])], { stdio: ['pipe', 'pipe', 'inherit'] });
let nextId = 1;
let buffer = '';
const pending = new Map();
const fail = error => { console.error(error.message || error); server.kill(); process.exitCode = 1; };
server.stdout.setEncoding('utf8');
server.stdout.on('data', chunk => {
  buffer += chunk;
  let end;
  while ((end = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, end); buffer = buffer.slice(end + 1);
    try {
      const message = JSON.parse(line);
      const done = pending.get(message.id);
      if (done) { pending.delete(message.id); done(message); }
    } catch (error) { fail(error); }
  }
});
server.on('error', fail);
const timer = setTimeout(() => fail(new Error('BIM-agent MCP request timed out.')), 150_000);
function request(method, params) {
  return new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, response => response.error ? reject(new Error(response.error.message)) : resolve(response.result));
    server.stdin.write(JSON.stringify({ jsonrpc: '2.0', id, method, ...(params ? { params } : {}) }) + '\n');
  });
}
try {
  await request('initialize', { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'bim-agent-cli', version: '0.4.0' } });
  server.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
  let result;
  if (toolName === 'list') result = await request('tools/list');
  else {
    const argumentsObject = argsFile ? JSON.parse(await readFile(argsFile, 'utf8')) : {};
    if (typeof argumentsObject !== 'object' || argumentsObject === null || Array.isArray(argumentsObject)) throw new Error('Arguments file must contain a JSON object.');
    result = await request('tools/call', { name: toolName, arguments: argumentsObject });
  }
  console.log(JSON.stringify(result, null, 2));
  if (result?.isError) process.exitCode = 1;
} catch (error) { fail(error); }
finally { clearTimeout(timer); server.stdin.end(); server.kill(); }
