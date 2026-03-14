const dndState = {
  phase: null,
  character: null,
  campaign: null,
  busy: false,
};

const el = {
  serverUrl:     document.getElementById('serverUrl'),
  serverStatus:  document.getElementById('serverStatus'),
  healthBtn:     document.getElementById('healthBtn'),
  campaignName:  document.getElementById('campaignName'),
  startBtn:      document.getElementById('startBtn'),
  playerInput:   document.getElementById('playerInput'),
  sendBtn:       document.getElementById('sendBtn'),
  messages:      document.getElementById('messages'),
  phaseBar:      document.getElementById('phaseBar'),
  campaignInfo:  document.getElementById('campaignInfo'),
  characterSheet: document.getElementById('characterSheet'),
};

function getServerUrl() {
  return (el.serverUrl.value || '').trim().replace(/\/+$/, '');
}

function setStatus(text, type) {
  el.serverStatus.textContent = text;
  el.serverStatus.dataset.type = type;
}

async function callDndApi(path, method, body) {
  const url = `${getServerUrl()}${path}`;
  const options = { method, headers: {} };
  if (body !== undefined) {
    options.headers['Content-Type'] = 'application/json';
    options.body = JSON.stringify(body);
  }
  const res = await fetch(url, options);
  const data = await res.json();
  if (!res.ok || !data.ok) {
    throw new Error(data.error || `Request failed (${res.status})`);
  }
  return data;
}

function addMessage(role, text) {
  const div = document.createElement('div');
  div.className = `dnd-msg dnd-msg--${role}`;
  div.innerHTML = renderMarkdown(text);
  el.messages.appendChild(div);
  autoScroll();
}

function addThinking() {
  const div = document.createElement('div');
  div.className = 'dnd-msg dnd-msg--system dnd-thinking';
  div.id = 'thinkingMsg';
  div.textContent = 'The DM is thinking';
  const dots = document.createElement('span');
  dots.className = 'dots';
  div.appendChild(dots);
  el.messages.appendChild(div);
  autoScroll();
}

function removeThinking() {
  const msg = document.getElementById('thinkingMsg');
  if (msg) msg.remove();
}

function autoScroll() {
  const m = el.messages;
  const isNearBottom = m.scrollHeight - m.scrollTop - m.clientHeight < 80;
  if (isNearBottom) m.scrollTop = m.scrollHeight;
}

function renderMarkdown(text) {
  return text
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/\*(.+?)\*/g, '<em>$1</em>')
    .replace(/^---$/gm, '<hr/>')
    .replace(/\n/g, '<br/>');
}

function setBusy(busy) {
  dndState.busy = busy;
  el.playerInput.disabled = busy || !dndState.phase;
  el.sendBtn.disabled = busy || !dndState.phase;
  el.startBtn.disabled = busy;
}

function updatePhaseBar(phase) {
  dndState.phase = phase;
  el.phaseBar.querySelectorAll('.dnd-phase').forEach(span => {
    span.classList.toggle('active', span.dataset.phase === phase);
  });
}

function updateCampaignInfo(campaign) {
  dndState.campaign = campaign;
  if (!campaign) {
    el.campaignInfo.textContent = 'No campaign active.';
    return;
  }
  const quests = (campaign.activeQuests || []).map(q => `<li>${esc(q)}</li>`).join('');
  el.campaignInfo.innerHTML = `
    <div class="dnd-stat-line"><strong>${esc(campaign.campaignName)}</strong></div>
    <div class="dnd-stat-line">Location: ${esc(campaign.currentLocation)}</div>
    ${campaign.currentLocationDescription ? `<div class="dnd-stat-line dnd-muted">${esc(campaign.currentLocationDescription)}</div>` : ''}
    ${quests ? `<div class="dnd-stat-line">Quests:</div><ul class="dnd-quest-list">${quests}</ul>` : ''}
    <div class="dnd-stat-line dnd-muted">Session ${campaign.sessionNumber || 1} &middot; ${(campaign.visitedLocations || []).length} places visited</div>
  `;
}

function updateCharacterSheet(character) {
  dndState.character = character;
  if (!character) {
    el.characterSheet.textContent = 'No character yet.';
    return;
  }
  const a = character.abilities || {};
  const hpPct = character.maxHitPoints > 0 ? Math.round(character.hitPoints / character.maxHitPoints * 100) : 0;

  el.characterSheet.innerHTML = `
    <div class="dnd-stat-line"><strong>${esc(character.name)}</strong></div>
    <div class="dnd-stat-line">${esc(character.race)} ${esc(character.class)} (Lv ${character.level})</div>
    <div class="dnd-stat-line">HP ${character.hitPoints}/${character.maxHitPoints}</div>
    <div class="dnd-hp-bar"><div class="dnd-hp-fill" style="width:${hpPct}%"></div></div>
    <div class="dnd-stat-line">AC ${character.armorClass} &middot; Speed ${character.speed}ft</div>
    <div class="dnd-abilities">
      ${abilityBox('STR', a.strength)}
      ${abilityBox('DEX', a.dexterity)}
      ${abilityBox('CON', a.constitution)}
      ${abilityBox('INT', a.intelligence)}
      ${abilityBox('WIS', a.wisdom)}
      ${abilityBox('CHA', a.charisma)}
    </div>
    <div class="dnd-stat-line dnd-muted">${character.alignment} &middot; ${character.experiencePoints || 0} XP</div>
    ${character.inventory ? `<details><summary>Inventory (${character.inventory.gold}g, ${(character.inventory.items || []).length} items)</summary><ul class="dnd-quest-list">${(character.inventory.items || []).map(i => `<li>${esc(i.name)}${i.quantity > 1 ? ' x' + i.quantity : ''}</li>`).join('')}</ul></details>` : ''}
  `;
}

function abilityBox(label, score) {
  if (score == null) return '';
  const mod = Math.floor((score - 10) / 2);
  const sign = mod >= 0 ? '+' : '';
  return `<div class="dnd-ability"><div class="dnd-ability-label">${label}</div><div class="dnd-ability-score">${score}</div><div class="dnd-muted">${sign}${mod}</div></div>`;
}

function esc(s) { return String(s || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }

function handleResponse(data) {
  if (data.narrative) addMessage('dm', data.narrative);
  if (data.phase) updatePhaseBar(data.phase);
  updateCampaignInfo(data.campaign);
  updateCharacterSheet(data.character);
  el.playerInput.disabled = false;
  el.sendBtn.disabled = false;
}

// ── Event listeners ──────────────────────────────────────────────

el.healthBtn.addEventListener('click', async () => {
  setStatus('Checking...', 'pending');
  try {
    const data = await callDndApi('/api/dnd/health', 'GET');
    setStatus('Connected', 'ok');
    if (data.phase) updatePhaseBar(data.phase);
  } catch (err) {
    setStatus(err.message, 'error');
  }
});

el.startBtn.addEventListener('click', async () => {
  const name = (el.campaignName.value || '').trim() || 'The Forgotten Keep';
  setBusy(true);
  addMessage('system', `Starting campaign: ${name}...`);
  addThinking();
  try {
    const data = await callDndApi('/api/dnd/start', 'POST', { campaignName: name });
    removeThinking();
    handleResponse(data);
    setStatus('Campaign active', 'ok');
  } catch (err) {
    removeThinking();
    addMessage('system', `Error: ${err.message}`);
    setStatus(err.message, 'error');
  }
  setBusy(false);
});

el.sendBtn.addEventListener('click', sendInput);

el.playerInput.addEventListener('keydown', (e) => {
  if (e.key === 'Enter' && !e.shiftKey) {
    e.preventDefault();
    sendInput();
  }
});

async function sendInput() {
  const text = (el.playerInput.value || '').trim();
  if (!text || dndState.busy) return;

  el.playerInput.value = '';
  addMessage('player', text);
  setBusy(true);
  addThinking();

  try {
    const data = await callDndApi('/api/dnd/input', 'POST', { text });
    removeThinking();
    handleResponse(data);
  } catch (err) {
    removeThinking();
    addMessage('system', `Error: ${err.message}`);
  }
  setBusy(false);
  el.playerInput.focus();
}
