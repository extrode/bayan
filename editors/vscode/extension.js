// Bayan VS Code extension: starts the language server and maps `bayan` debug
// configs onto a native debugger (the C backend writes #line, so .byn
// breakpoints and stepping work through lldb/gdb/vsdbg).
'use strict';
const vscode = require('vscode');
const cp = require('child_process');
const path = require('path');
const { LanguageClient, TransportKind } = require('vscode-languageclient/node');

let client;
let watcher;

function startClient(context) {
  const cfg = vscode.workspace.getConfiguration('bayan');
  const command = cfg.get('server.path') || 'bayan-lsp';
  const args = cfg.get('server.args') || [];
  const run = { command, args, transport: TransportKind.stdio };
  client = new LanguageClient(
    'bayan',
    'Bayan Language Server',
    { run, debug: run },
    {
      documentSelector: [{ scheme: 'file', language: 'bayan' }, { scheme: 'untitled', language: 'bayan' }],
      synchronize: { fileEvents: watcher }
    }
  );
  return client.start().catch(err => {
    vscode.window.showWarningMessage(
      `Bayan language server not started (${command}): ${err.message}. Highlighting and snippets still work; set "bayan.server.path".`);
  });
}

async function stopClient() {
  if (!client) return;
  const c = client;
  client = undefined;
  try { if (c.isRunning()) await c.stop(); } catch { /* already stopped or never started */ }
}

function buildDebug(folder) {
  const bayan = vscode.workspace.getConfiguration('bayan').get('compiler.path') || 'bayan';
  return new Promise((resolve, reject) => {
    // Contract (see HANDOFF.md): prints the built executable's absolute path as the last stdout line.
    cp.execFile(bayan, ['build', '--debug', '--print-output'], { cwd: folder, maxBuffer: 16 * 1024 * 1024 }, (err, stdout, stderr) => {
      if (err) return reject(new Error((stderr || err.message).trim()));
      const last = stdout.trim().split(/\r?\n/).pop();
      if (!last) return reject(new Error('the build printed no executable path'));
      resolve(last.trim());
    });
  });
}

const debugProvider = {
  // F5 with no launch.json: synthesise a config when a Bayan file is active.
  resolveDebugConfiguration(folder, config) {
    if (!config.type && !config.request) {
      const editor = vscode.window.activeTextEditor;
      if (!editor || editor.document.languageId !== 'bayan') return config;
      return { type: 'bayan', request: 'launch', name: 'Debug Bayan', build: true };
    }
    return config;
  },

  // Runs after VS Code has substituted ${workspaceFolder} and friends.
  async resolveDebugConfigurationWithSubstitutedVariables(folder, config) {
    const editor = vscode.window.activeTextEditor;
    const fileDir = editor && editor.document.uri.scheme === 'file' ? path.dirname(editor.document.uri.fsPath) : undefined;
    const cwd = config.cwd || (folder && folder.uri.fsPath) || fileDir;
    if (!cwd) { vscode.window.showErrorMessage('Bayan: open a folder or a saved .byn file to debug.'); return undefined; }
    let program = config.program;
    if (config.build !== false || !program) {
      try {
        program = await vscode.window.withProgress(
          { location: vscode.ProgressLocation.Window, title: 'bayan build --debug' }, () => buildDebug(cwd));
      } catch (e) { vscode.window.showErrorMessage(`bayan build failed: ${e.message}`); return undefined; }
    }
    const backend = vscode.workspace.getConfiguration('bayan').get('debug.backend') || 'lldb';
    const base = { name: config.name, request: 'launch', program: path.resolve(cwd, program), args: config.args || [], cwd };
    if (backend === 'lldb') return { ...base, type: 'lldb', sourceLanguages: ['c'] };
    if (backend === 'cppvsdbg') return { ...base, type: 'cppvsdbg' };
    return { ...base, type: 'cppdbg', MIMode: process.platform === 'darwin' ? 'lldb' : 'gdb' };
  }
};

function activate(context) {
  watcher = vscode.workspace.createFileSystemWatcher('**/*.byn');
  context.subscriptions.push(
    watcher,
    vscode.commands.registerCommand('bayan.restartServer', async () => {
      await stopClient();
      await startClient(context);
    }),
    vscode.workspace.onDidChangeConfiguration(async e => {
      if (e.affectsConfiguration('bayan.server')) { await stopClient(); await startClient(context); }
    }),
    vscode.debug.registerDebugConfigurationProvider('bayan', debugProvider)
  );
  startClient(context);
}

function deactivate() { return stopClient(); }

module.exports = { activate, deactivate };
