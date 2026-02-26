const state = {
  lastResult: null,
};

const llmDefaults = {
  openai: { model: 'gpt-4.1-mini', baseUrl: 'https://api.openai.com', keyName: 'openai-main' },
  anthropic: { model: 'claude-3-5-sonnet-latest', baseUrl: 'https://api.anthropic.com', keyName: 'anthropic-main' },
  gemini: { model: 'gemini-2.0-flash', baseUrl: 'https://generativelanguage.googleapis.com', keyName: 'gemini-main' },
  lmstudio: { model: 'local-model', baseUrl: 'http://127.0.0.1:1234', keyName: 'lmstudio-local' },
  ollama: { model: 'llama3.1', baseUrl: 'http://127.0.0.1:11434', keyName: 'ollama-local' },
};

const el = {
  apiBase: document.getElementById('apiBase'),
  status: document.getElementById('status'),
  output: document.getElementById('output'),
  screenshotName: document.getElementById('screenshotName'),
  customAction: document.getElementById('customAction'),
  customArgs: document.getElementById('customArgs'),
  customTimeout: document.getElementById('customTimeout'),
  healthBtn: document.getElementById('healthBtn'),
  pingBtn: document.getElementById('pingBtn'),
  customRunBtn: document.getElementById('customRunBtn'),
  shotBtn: document.getElementById('shotBtn'),
  validateBtn: document.getElementById('validateBtn'),
  perfBtn: document.getElementById('perfBtn'),
  treeBtn: document.getElementById('treeBtn'),
  clearInputBtn: document.getElementById('clearInputBtn'),
  quitBtn: document.getElementById('quitBtn'),
  llmProvider: document.getElementById('llmProvider'),
  llmModel: document.getElementById('llmModel'),
  llmBaseUrl: document.getElementById('llmBaseUrl'),
  llmApiKey: document.getElementById('llmApiKey'),
  llmKeyName: document.getElementById('llmKeyName'),
  secretStatus: document.getElementById('secretStatus'),
  secretStatusBtn: document.getElementById('secretStatusBtn'),
  secretListBtn: document.getElementById('secretListBtn'),
  secretSaveBtn: document.getElementById('secretSaveBtn'),
  secretLoadBtn: document.getElementById('secretLoadBtn'),
  secretDeleteBtn: document.getElementById('secretDeleteBtn'),
  llmUseSavedBtn: document.getElementById('llmUseSavedBtn'),
  llmTemperature: document.getElementById('llmTemperature'),
  llmMaxTokens: document.getElementById('llmMaxTokens'),
  llmPrompt: document.getElementById('llmPrompt'),
  llmProvidersBtn: document.getElementById('llmProvidersBtn'),
  llmRunBtn: document.getElementById('llmRunBtn'),
};

function getApiBase() {
  return (el.apiBase.value || '').trim().replace(/\/+$/, '');
}

function setStatus(text, type = 'neutral') {
  el.status.textContent = text;
  el.status.dataset.type = type;
}

function showOutput(data) {
  state.lastResult = data;
  el.output.textContent = typeof data === 'string' ? data : JSON.stringify(data, null, 2);
}

async function callApi(path, method = 'GET', body = null) {
  const url = `${getApiBase()}${path}`;
  const options = { method, headers: {} };

  if (body !== null) {
    options.headers['Content-Type'] = 'application/json';
    options.body = JSON.stringify(body);
  }

  const res = await fetch(url, options);
  const data = await res.json().catch(() => ({ ok: false, error: 'Invalid JSON response' }));
  if (!res.ok) {
    const err = new Error(data.error || `Request failed (${res.status})`);
    err.data = data;
    throw err;
  }
  return data;
}

async function runAction(action, args = {}, timeout = 30) {
  setStatus(`Running ${action}...`, 'pending');
  try {
    const response = await callApi('/api/command', 'POST', { action, args, timeout });
    setStatus(`${action} completed`, 'ok');
    showOutput(response);
  } catch (err) {
    setStatus(`${action} failed`, 'error');
    showOutput({ ok: false, message: err.message, details: err.data || null });
  }
}

function applyLlmDefaults(provider) {
  const preset = llmDefaults[provider];
  if (!preset) {
    return;
  }
  el.llmModel.value = preset.model;
  el.llmBaseUrl.value = preset.baseUrl;
  el.llmKeyName.value = preset.keyName;
}

function buildLlmPayload(useSavedKey) {
  const provider = (el.llmProvider.value || '').trim();
  const model = (el.llmModel.value || '').trim();
  const prompt = (el.llmPrompt.value || '').trim();
  const baseUrl = (el.llmBaseUrl.value || '').trim();
  const apiKey = (el.llmApiKey.value || '').trim();
  const apiKeyName = (el.llmKeyName.value || '').trim();

  if (!provider || !model || !prompt) {
    throw new Error('Provider, model, and prompt are required');
  }

  const payload = { provider, model, prompt, timeout: 60 };
  if (baseUrl) {
    payload.base_url = baseUrl;
  }

  if (useSavedKey) {
    if (!apiKeyName) {
      throw new Error('Saved key name is required');
    }
    payload.api_key_name = apiKeyName;
  } else if (apiKey) {
    payload.api_key = apiKey;
  }

  const temperatureRaw = (el.llmTemperature.value || '').trim();
  if (temperatureRaw) {
    const temp = Number(temperatureRaw);
    if (!Number.isFinite(temp)) {
      throw new Error('Temperature must be numeric');
    }
    payload.temperature = temp;
  }

  const maxTokensRaw = (el.llmMaxTokens.value || '').trim();
  if (maxTokensRaw) {
    const maxTokens = Number(maxTokensRaw);
    if (!Number.isFinite(maxTokens)) {
      throw new Error('Max tokens must be numeric');
    }
    payload.max_tokens = Math.trunc(maxTokens);
  }

  return payload;
}

async function runLlmChat(useSavedKey = false) {
  let payload;
  try {
    payload = buildLlmPayload(useSavedKey);
  } catch (err) {
    setStatus(err.message, 'error');
    return;
  }

  setStatus(`Running ${payload.provider} chat...`, 'pending');
  try {
    const response = await callApi('/api/llm/chat', 'POST', payload);
    setStatus(`${payload.provider} chat completed`, 'ok');
    showOutput(response);
  } catch (err) {
    setStatus(`${payload.provider} chat failed`, 'error');
    showOutput({ ok: false, message: err.message, details: err.data || null });
  }
}

el.healthBtn.addEventListener('click', async () => {
  setStatus('Checking API health...', 'pending');
  try {
    const response = await callApi('/api/health');
    setStatus('API reachable', 'ok');
    showOutput(response);
  } catch (err) {
    setStatus('API unavailable', 'error');
    showOutput({ ok: false, message: err.message });
  }
});

el.pingBtn.addEventListener('click', () => runAction('ping'));

el.shotBtn.addEventListener('click', () => {
  const filename = (el.screenshotName.value || '').trim();
  const args = filename ? { filename } : {};
  runAction('screenshot', args, 30);
});

el.validateBtn.addEventListener('click', () => runAction('validate_all_scenes', {}, 60));
el.perfBtn.addEventListener('click', () => runAction('performance'));
el.treeBtn.addEventListener('click', () => runAction('scene_tree', { depth: 10 }, 30));
el.clearInputBtn.addEventListener('click', () => runAction('input_clear'));
el.quitBtn.addEventListener('click', () => runAction('quit', { exit_code: 0 }, 5));

el.customRunBtn.addEventListener('click', () => {
  const action = (el.customAction.value || '').trim();
  if (!action) {
    setStatus('Custom action is required', 'error');
    return;
  }

  let args = {};
  const rawArgs = (el.customArgs.value || '').trim();
  if (rawArgs) {
    try {
      args = JSON.parse(rawArgs);
    } catch {
      setStatus('Args JSON is invalid', 'error');
      return;
    }
  }

  const timeout = Number(el.customTimeout.value || '30');
  runAction(action, args, Number.isFinite(timeout) ? timeout : 30);
});

el.llmProvider.addEventListener('change', () => {
  applyLlmDefaults(el.llmProvider.value);
});

el.llmProvidersBtn.addEventListener('click', async () => {
  setStatus('Loading provider info...', 'pending');
  try {
    const response = await callApi('/api/llm/providers', 'GET');
    setStatus('Provider info loaded', 'ok');
    showOutput(response);
  } catch (err) {
    setStatus('Provider info failed', 'error');
    showOutput({ ok: false, message: err.message, details: err.data || null });
  }
});

el.llmRunBtn.addEventListener('click', () => runLlmChat(false));
el.llmUseSavedBtn.addEventListener('click', () => runLlmChat(true));

el.secretStatusBtn.addEventListener('click', async () => {
  setStatus('Checking secret store...', 'pending');
  try {
    const response = await callApi('/api/secrets/status', 'GET');
    el.secretStatus.value = `${response.backend} (${response.secure ? 'secure' : 'fallback'})`;
    setStatus('Secret store ready', response.secure ? 'ok' : 'pending');
    showOutput(response);
  } catch (err) {
    setStatus('Secret store check failed', 'error');
    showOutput({ ok: false, message: err.message, details: err.data || null });
  }
});

el.secretListBtn.addEventListener('click', async () => {
  setStatus('Listing saved keys...', 'pending');
  try {
    const response = await callApi('/api/secrets/list', 'GET');
    setStatus('Saved keys listed', 'ok');
    showOutput(response);
  } catch (err) {
    setStatus('List keys failed', 'error');
    showOutput({ ok: false, message: err.message, details: err.data || null });
  }
});

el.secretSaveBtn.addEventListener('click', async () => {
  const name = (el.llmKeyName.value || '').trim();
  const value = (el.llmApiKey.value || '').trim();
  if (!name || !value) {
    setStatus('Saved key name and API key are required', 'error');
    return;
  }

  setStatus('Saving key...', 'pending');
  try {
    const response = await callApi('/api/secrets/set', 'POST', { name, value });
    setStatus('Key saved', 'ok');
    showOutput(response);
  } catch (err) {
    setStatus('Save key failed', 'error');
    showOutput({ ok: false, message: err.message, details: err.data || null });
  }
});

el.secretLoadBtn.addEventListener('click', async () => {
  const name = (el.llmKeyName.value || '').trim();
  if (!name) {
    setStatus('Saved key name is required', 'error');
    return;
  }

  setStatus('Loading key...', 'pending');
  try {
    const response = await callApi('/api/secrets/get', 'POST', { name });
    el.llmApiKey.value = response.value || '';
    setStatus('Key loaded', 'ok');
    showOutput({ ok: true, name: response.name, value: response.value ? '[loaded]' : '' });
  } catch (err) {
    setStatus('Load key failed', 'error');
    showOutput({ ok: false, message: err.message, details: err.data || null });
  }
});

el.secretDeleteBtn.addEventListener('click', async () => {
  const name = (el.llmKeyName.value || '').trim();
  if (!name) {
    setStatus('Saved key name is required', 'error');
    return;
  }

  setStatus('Deleting key...', 'pending');
  try {
    const response = await callApi('/api/secrets/delete', 'POST', { name });
    setStatus('Key deleted', 'ok');
    showOutput(response);
  } catch (err) {
    setStatus('Delete key failed', 'error');
    showOutput({ ok: false, message: err.message, details: err.data || null });
  }
});

applyLlmDefaults(el.llmProvider.value);
setStatus('Ready');
