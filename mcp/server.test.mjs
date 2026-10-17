import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { test } from 'node:test';
import readline from 'node:readline';
import { fileURLToPath } from 'node:url';

test('MCP initializes, lists tools, and rejects unknown methods', async () => {
  const child = spawn(process.execPath, [fileURLToPath(new URL('./server.mjs', import.meta.url))], { stdio: ['pipe', 'pipe', 'pipe'] });
  const lines = readline.createInterface({ input: child.stdout });
  const responses = [];
  const done = new Promise((resolve, reject) => {
    lines.on('line', line => { responses.push(JSON.parse(line)); if (responses.length === 3) resolve(); });
    child.on('error', reject);
    child.on('exit', code => { if (responses.length < 3) reject(new Error(`MCP process exited early: ${code}`)); });
  });
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 1, method: 'initialize', params: { protocolVersion: '2025-11-25', capabilities: {}, clientInfo: { name: 'test', version: '1' } } }) + '\n');
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 2, method: 'tools/list' }) + '\n');
  child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 3, method: 'missing' }) + '\n');
  await done;
  child.kill();
  assert.equal(responses[0].result.serverInfo.name, 'bim-agent');
  assert.ok(responses[1].result.tools.some(tool => tool.name === 'revit_create_walls'));
  assert.ok(responses[1].result.tools.some(tool => tool.name === 'revit_inspect_element'));
  assert.equal(responses[2].error.code, -32601);
});
