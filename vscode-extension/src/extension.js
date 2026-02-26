const vscode = require("vscode");
const http = require("http");
const { spawn } = require("child_process");
const path = require("path");

let bridgeProcess = null;
let panel = null;
let statusItem = null;
let output = null;

function activate(context) {
  output = vscode.window.createOutputChannel("Harness Agent");
  statusItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 100);
  statusItem.command = "harnessAgent.openPanel";
  statusItem.tooltip = "Open Harness Agent Panel";
  updateStatus("stopped");
  statusItem.show();

  context.subscriptions.push(output, statusItem);

  const register = (id, fn) => context.subscriptions.push(vscode.commands.registerCommand(id, fn));

  register("harnessAgent.startBridge", () => startBridge());
  register("harnessAgent.stopBridge", () => stopBridge());
  register("harnessAgent.openPanel", () => openPanel(context));
  register("harnessAgent.checkHealth", () => checkHealth());

  register("harnessAgent.runBuild", () => runGuardrailCommand("buildCommand", "Harness Build"));
  register("harnessAgent.runLint", () => runGuardrailCommand("lintCommand", "Harness Lint"));
  register("harnessAgent.runTests", () => runGuardrailCommand("testCommand", "Harness Test"));
  register("harnessAgent.runGuardrails", () => runAllGuardrails());

  register("harnessAgent.runtimePing", () => runRuntimeAction("ping"));
  register("harnessAgent.runtimeValidateAll", () => runRuntimeAction("validate_all_scenes", {}, 60));
  register("harnessAgent.runtimePerformance", () => runRuntimeAction("performance"));
  register("harnessAgent.runtimeSceneTree", () => runRuntimeAction("scene_tree", { depth: 10 }));
  register("harnessAgent.runtimeClearInput", () => runRuntimeAction("input_clear"));
  register("harnessAgent.runtimeQuit", () => runRuntimeAction("quit", { exit_code: 0 }, 5));
  register("harnessAgent.runtimeScreenshot", async () => {
    const filename = await vscode.window.showInputBox({
      prompt: "Screenshot filename",
      value: "verification.png",
      placeHolder: "verification.png",
      ignoreFocusOut: true,
    });
    if (typeof filename === "undefined") {
      return;
    }
    const trimmed = filename.trim();
    const args = trimmed ? { filename: trimmed } : {};
    await runRuntimeAction("screenshot", args, 30);
  });
}

async function startBridge() {
  if (bridgeProcess && !bridgeProcess.killed) {
    vscode.window.showInformationMessage("Harness bridge is already running.");
    return;
  }

  const workspace = getWorkspaceRoot();
  if (!workspace) {
    vscode.window.showErrorMessage("Open a workspace folder before starting Harness bridge.");
    return;
  }

  const config = getConfig();
  const pythonPath = config.get("pythonPath", "python");
  const host = config.get("host", "127.0.0.1");
  const port = String(config.get("port", 8765));
  const projectPath = resolveWorkspacePath(workspace, config.get("projectPath", "."));
  const bridgeScript = resolveWorkspacePath(workspace, config.get("bridgeScript", "tools/devtools_web.py"));

  const args = [bridgeScript, "--project", projectPath, "--host", host, "--port", port];
  output.appendLine(`Starting bridge: ${pythonPath} ${args.join(" ")}`);
  bridgeProcess = spawn(pythonPath, args, { cwd: workspace, windowsHide: true });

  bridgeProcess.stdout.on("data", (data) => output.append(data.toString()));
  bridgeProcess.stderr.on("data", (data) => output.append(data.toString()));

  bridgeProcess.on("error", (err) => {
    output.appendLine(`Bridge error: ${err.message}`);
    vscode.window.showErrorMessage(`Harness bridge failed to start: ${err.message}`);
    bridgeProcess = null;
    updateStatus("stopped");
  });

  bridgeProcess.on("exit", (code, signal) => {
    output.appendLine(`Bridge exited (code=${code}, signal=${signal || "none"})`);
    bridgeProcess = null;
    updateStatus("stopped");
  });

  updateStatus("running");
  vscode.window.showInformationMessage(`Harness bridge started on http://${host}:${port}`);
}

async function stopBridge() {
  if (!bridgeProcess) {
    vscode.window.showInformationMessage("Harness bridge is not running.");
    return;
  }

  bridgeProcess.kill();
  bridgeProcess = null;
  updateStatus("stopped");
  vscode.window.showInformationMessage("Harness bridge stopped.");
}

async function checkHealth() {
  try {
    const data = await apiGet("/api/health");
    output.appendLine(`Health: ${JSON.stringify(data)}`);
    vscode.window.showInformationMessage("Harness bridge is reachable.");
  } catch (err) {
    vscode.window.showErrorMessage(`Harness bridge health check failed: ${err.message}`);
  }
}

async function runRuntimeAction(action, args = {}, timeout = 30) {
  try {
    const data = await apiPost("/api/command", { action, args, timeout });
    output.appendLine(`[runtime:${action}] ${JSON.stringify(data.result ?? data, null, 2)}`);
    vscode.window.showInformationMessage(`Runtime action '${action}' completed.`);
    return data;
  } catch (err) {
    output.appendLine(`[runtime:${action}:error] ${err.message}`);
    vscode.window.showErrorMessage(`Runtime action '${action}' failed: ${err.message}`);
    throw err;
  }
}

function runGuardrailCommand(settingKey, terminalName, terminal) {
  const config = getConfig();
  const cmd = config.get(`guardrails.${settingKey}`, "");
  if (!cmd || !cmd.trim()) {
    vscode.window.showErrorMessage(`No command configured for ${settingKey}.`);
    return;
  }

  const activeTerminal =
    terminal ||
    vscode.window.createTerminal({
      name: terminalName,
      cwd: getWorkspaceRoot() || undefined,
    });
  activeTerminal.show(true);
  activeTerminal.sendText(cmd, true);
}

function runAllGuardrails() {
  const terminal = vscode.window.createTerminal({
    name: "Harness Guardrails",
    cwd: getWorkspaceRoot() || undefined,
  });
  runGuardrailCommand("buildCommand", "Harness Guardrails", terminal);
  runGuardrailCommand("lintCommand", "Harness Guardrails", terminal);
  runGuardrailCommand("testCommand", "Harness Guardrails", terminal);
}

function openPanel(context) {
  if (panel) {
    panel.reveal(vscode.ViewColumn.Beside);
    panel.webview.html = getPanelHtml(context);
    return;
  }

  panel = vscode.window.createWebviewPanel(
    "harnessAgentPanel",
    "Harness Agent",
    vscode.ViewColumn.Beside,
    {
      enableScripts: true,
      retainContextWhenHidden: true,
    }
  );

  panel.webview.onDidReceiveMessage(async (msg) => {
    if (!msg || typeof msg !== "object") {
      return;
    }
    if (msg.type === "startBridge") {
      await startBridge();
      panel.webview.html = getPanelHtml(context);
      return;
    }
    if (msg.type === "stopBridge") {
      await stopBridge();
      panel.webview.html = getPanelHtml(context);
      return;
    }
    if (msg.type === "refreshPanel") {
      panel.webview.html = getPanelHtml(context);
      return;
    }
    if (msg.type === "openExternal") {
      const base = getBaseUrl();
      vscode.env.openExternal(vscode.Uri.parse(base));
    }
  });

  panel.onDidDispose(() => {
    panel = null;
  });

  panel.webview.html = getPanelHtml(context);
}

function getPanelHtml(context) {
  const nonce = String(Date.now());
  const baseUrl = getBaseUrl();
  const running = bridgeProcess && !bridgeProcess.killed;
  const csp = [
    "default-src 'none'",
    `script-src 'nonce-${nonce}'`,
    "style-src 'unsafe-inline'",
    `frame-src ${baseUrl}`,
  ].join("; ");

  const state = running ? "Running" : "Stopped";
  return `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta http-equiv="Content-Security-Policy" content="${csp}">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Harness Agent</title>
  <style>
    body { font-family: var(--vscode-font-family); margin: 0; background: var(--vscode-editor-background); color: var(--vscode-editor-foreground); }
    .bar { display: flex; gap: 8px; align-items: center; padding: 10px; border-bottom: 1px solid var(--vscode-panel-border); }
    .pill { padding: 2px 8px; border-radius: 999px; border: 1px solid var(--vscode-panel-border); }
    button { background: var(--vscode-button-background); color: var(--vscode-button-foreground); border: 0; padding: 6px 10px; cursor: pointer; border-radius: 4px; }
    button:hover { background: var(--vscode-button-hoverBackground); }
    iframe { display: block; width: 100%; height: calc(100vh - 50px); border: 0; }
  </style>
</head>
<body>
  <div class="bar">
    <span class="pill">Bridge: ${state}</span>
    <span class="pill">${baseUrl}</span>
    <button id="start">Start</button>
    <button id="stop">Stop</button>
    <button id="refresh">Refresh</button>
    <button id="external">Open in Browser</button>
  </div>
  <iframe src="${baseUrl}" title="Harness DevTools Web UI"></iframe>
  <script nonce="${nonce}">
    const vscode = acquireVsCodeApi();
    document.getElementById('start').addEventListener('click', () => vscode.postMessage({ type: 'startBridge' }));
    document.getElementById('stop').addEventListener('click', () => vscode.postMessage({ type: 'stopBridge' }));
    document.getElementById('refresh').addEventListener('click', () => vscode.postMessage({ type: 'refreshPanel' }));
    document.getElementById('external').addEventListener('click', () => vscode.postMessage({ type: 'openExternal' }));
  </script>
</body>
</html>`;
}

function apiGet(pathname) {
  return apiRequest("GET", pathname);
}

function apiPost(pathname, payload) {
  return apiRequest("POST", pathname, payload);
}

function apiRequest(method, pathname, payload) {
  return new Promise((resolve, reject) => {
    const { hostname, port } = getHostPort();
    const body = payload ? JSON.stringify(payload) : null;
    const req = http.request(
      {
        hostname,
        port,
        path: pathname,
        method,
        headers: body
          ? {
              "Content-Type": "application/json",
              "Content-Length": Buffer.byteLength(body),
            }
          : undefined,
      },
      (res) => {
        let text = "";
        res.on("data", (chunk) => {
          text += chunk.toString("utf8");
        });
        res.on("end", () => {
          let data = null;
          try {
            data = text ? JSON.parse(text) : {};
          } catch {
            reject(new Error(`Invalid JSON response (status ${res.statusCode})`));
            return;
          }
          if ((res.statusCode || 500) >= 400) {
            reject(new Error(data.error || `Request failed (${res.statusCode})`));
            return;
          }
          resolve(data);
        });
      }
    );
    req.on("error", (err) => reject(err));
    if (body) {
      req.write(body);
    }
    req.end();
  });
}

function getHostPort() {
  const config = getConfig();
  return {
    hostname: config.get("host", "127.0.0.1"),
    port: Number(config.get("port", 8765)),
  };
}

function getBaseUrl() {
  const { hostname, port } = getHostPort();
  return `http://${hostname}:${port}`;
}

function getWorkspaceRoot() {
  const folder = vscode.workspace.workspaceFolders && vscode.workspace.workspaceFolders[0];
  return folder ? folder.uri.fsPath : null;
}

function resolveWorkspacePath(workspaceRoot, p) {
  return path.isAbsolute(p) ? p : path.join(workspaceRoot, p);
}

function getConfig() {
  return vscode.workspace.getConfiguration("harnessAgent");
}

function updateStatus(mode) {
  if (!statusItem) {
    return;
  }
  if (mode === "running") {
    statusItem.text = "$(radio-tower) Harness Bridge";
    statusItem.backgroundColor = undefined;
    return;
  }
  statusItem.text = "$(circle-slash) Harness Bridge";
  statusItem.backgroundColor = new vscode.ThemeColor("statusBarItem.warningBackground");
}

function deactivate() {
  if (bridgeProcess) {
    bridgeProcess.kill();
    bridgeProcess = null;
  }
}

module.exports = {
  activate,
  deactivate,
};
