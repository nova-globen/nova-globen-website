/* Elementals - browser client. The server owns the match (Lek.Server.Elementals); this file sends inputs at
   30 Hz with sequence numbers, predicts the player's own movement with physics.js and replays unacknowledged inputs
   on every snapshot, interpolates everyone else 100 ms in the past, and draws it all with art.js. */
(() => {
  'use strict';

  const A = window.ElementalsArt, P = window.ElementalsPhysics, S = window.ElementalsAudio;
  const T = 16, COLS = 40, ROWS = 22, W = 640, H = 352;
  const TEAM_COLORS = ['#ffc23a', '#b48cff'];
  const TEAM_NAMES = ['Sun', 'Moon'];
  // the order of a player's fields in a snapshot (ElementalsRoom.Snapshot); the roster carries the rest
  const PLAYER_FIELDS = ['id', 'x', 'y', 'vx', 'vy', 'g', 'gk', 'co', 'pj', 'ju', 'dt', 'dv', 'fl', 'lt', 'sp', 'w', 'f', 'a', 'sw', 'cd', 'hp', 'al', 'st', 'seq', 'ab0', 'ab1', 'ab2', 'im', 'mk'];
  const roster = new Map();
  const $ = (id) => document.getElementById(id);
  const canvas = $('game');
  const g = canvas.getContext('2d');
  g.imageSmoothingEnabled = false;
  const params = new URLSearchParams(location.search);
  const store = {
    get(k, d) { try { const v = localStorage.getItem(LekSite.key(k)); return v == null ? d : v; } catch { return d; } },
    set(k, v) { try { localStorage.setItem(LekSite.key(k), v); } catch { } }
  };

  // ------------------------------------------------------------------ state
  let me = null, kinds = [], kindById = {}, mapsById = {}, state = null, snaps = [], clockOffset = null;
  let mapId = null, cells = null, dynMap = new Map(), tiles = null, bg = null, theme = 'desert';
  let lastPhase = 'lobby', lobbySig = '', myKind = store.get('elementals-kind', 'cactus');
  function loadoutOf(kindId) {
    const k = kindById[kindId];
    if (!k) return [];
    try {
      const saved = JSON.parse(store.get('elementals-loadout-' + kindId, 'null'));
      if (Array.isArray(saved) && saved.length === 3 && saved.every((id) => k.pool.some((a) => a.id === id))) return saved;
    } catch { }
    return k.loadout.slice();
  }
  let draft = [], armsDraft = [], common = [];
  // hand-finished pixel portraits; the rest draw their in-game sprite (the classic-art page shows none)
  const PORTRAITS = window.ELEMENTALS_CLASSIC ? [] : ['cactus', 'ice'];
  function weaponDef(kindId, id) {
    const k = kindById[kindId];
    return (k && k.weapons.find((w) => w.id === id)) || common.find((w) => w.id === id) || null;
  }
  function armsOf(kindId) {
    const k = kindById[kindId];
    if (!k) return [];
    try {
      const saved = JSON.parse(store.get('elementals-arms-' + kindId, 'null'));
      if (Array.isArray(saved) && saved.length === 2 && saved[0] !== saved[1] && saved.every((id) => weaponDef(kindId, id))) return saved;
    } catch { }
    return k.arms.slice();
  }
  async function pickHero(kindId) {
    myKind = kindId;
    store.set('elementals-kind', kindId);
    draft = loadoutOf(kindId);
    armsDraft = armsOf(kindId);
    await hub.invoke('SetKind', kindId);
    await hub.invoke('SetLoadout', draft);
    await hub.invoke('SetArms', armsDraft);
    renderLobbyDetail();
  }
  let seq = 0, pending = [], myBody = null, myPrev = null, stepAt = 0, offset = { x: 0, y: 0 };
  let localSwingAt = -10, localCd = 0;
  const flashes = new Map();
  let particles = [], texts = [], zaps = [], shake = 0, banner = null;
  const thumbs = {};

  // ------------------------------------------------------------------ keys
  const ACTIONS = [
    ['left', 'Move left'], ['right', 'Move right'], ['jump', 'Jump'], ['down', 'Drop down'],
    ['attack', 'Attack'], ['swap', 'Swap weapon'], ['ab0', 'Ability 1'], ['ab1', 'Ability 2'], ['ab2', 'Ability 3'],
    ['aimUp', 'Aim up'], ['aimDown', 'Aim down'], ['aimLeft', 'Aim left'], ['aimRight', 'Aim right']
  ];
  const DEFAULT_KEYS = {
    left: ['KeyA'], right: ['KeyD'], jump: ['KeyW', 'Space'], down: ['KeyS'], attack: ['KeyF'], swap: ['KeyQ'],
    ab0: ['KeyR'], ab1: ['KeyT'], ab2: ['KeyY'], aimUp: ['ArrowUp'], aimDown: ['ArrowDown'], aimLeft: ['ArrowLeft'], aimRight: ['ArrowRight']
  };
  let binds = loadBinds();
  function loadBinds() {
    try { const b = JSON.parse(store.get('elementals-keys', 'null')); if (b && typeof b === 'object') return Object.assign({}, DEFAULT_KEYS, b); } catch { }
    return JSON.parse(JSON.stringify(DEFAULT_KEYS));
  }
  const held = new Set();
  const virtual = new Set();      // touch buttons
  const isDown = (action) => virtual.has(action) || (binds[action] || []).some((k) => held.has(k));
  let mouse = { x: W / 2, y: H / 2, down: false, movedAt: -1e9 };
  let aimMode = 'mouse', aimElev = 0, aimFacing = 1, rebinding = null;
  // the mouse aims while it is in use (moved in the last two seconds or held); otherwise you aim where you face
  const mouseInUse = () => mouse.down || performance.now() - mouse.movedAt < 2000;

  function keyAction(code) {
    for (const [action] of ACTIONS) if ((binds[action] || []).includes(code)) return action;
    return null;
  }
  function onKeyDown(e) {
    if (rebinding) { e.preventDefault(); binds[rebinding] = [e.code]; store.set('elementals-keys', JSON.stringify(binds)); rebinding = null; renderBinds(); return; }
    if (e.target && e.target.tagName === 'INPUT') return;
    const action = keyAction(e.code);
    if (!action && !e.code.startsWith('Digit')) return;
    e.preventDefault();
    if (held.has(e.code)) return;
    held.add(e.code);
    pressAction(action, e.code);
  }
  function onKeyUp(e) { held.delete(e.code); }
  function pressAction(action, code) {
    if (!inFight()) return;
    if (action === 'ab0' || action === 'ab1' || action === 'ab2') useAbility(Number(action[2]));
    else if (action === 'swap') swapWeapon();
    else if (code === 'Digit1' || code === 'Digit2') swapWeapon(code === 'Digit1' ? 0 : 1);
    else if (action && action.startsWith('aim')) aimMode = 'keys';
  }
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('keyup', onKeyUp);
  window.addEventListener('blur', () => held.clear());
  try {
    // after alt-tab the browser focuses the site page around the frame, not the frame
    window.parent.addEventListener('keydown', onKeyDown);
    window.parent.addEventListener('keyup', onKeyUp);
  } catch { }

  canvas.addEventListener('mousemove', (e) => {
    const r = canvas.getBoundingClientRect();
    mouse.x = (e.clientX - r.left) / r.width * W;
    mouse.y = (e.clientY - r.top) / r.height * H;
    mouse.movedAt = performance.now();
    aimMode = 'mouse';
  });
  canvas.addEventListener('mousedown', (e) => { if (e.button === 0) mouse.down = true; if (e.button === 2) swapWeapon(); S.unlock(); });
  window.addEventListener('mouseup', (e) => { if (e.button === 0) mouse.down = false; });
  canvas.addEventListener('contextmenu', (e) => e.preventDefault());
  canvas.addEventListener('wheel', (e) => { if (inFight()) { swapWeapon(); e.preventDefault(); } }, { passive: false });

  // ------------------------------------------------------------------ connection
  const hub = new signalR.HubConnectionBuilder().withUrl(LekSite.url('/hubs/elementals')).withAutomaticReconnect().configureLogging(signalR.LogLevel.Warning).build();
  hub.on('state', onState);
  hub.onreconnected(() => { me = null; state = null; showMenu('Connection was lost. Join again.'); });
  hub.onclose(() => showMenu('Disconnected.'));

  async function connect() {
    if (hub.state === signalR.HubConnectionState.Connected) return true;
    try { await hub.start(); return true; } catch (err) { setMsg('menu-msg', 'Cannot reach the server.'); console.error(err); return false; }
  }
  function myName() { const n = $('name').value.trim().slice(0, 14); store.set('elementals-name', n); return n; }
  $('name').value = params.get('name') || store.get('elementals-name', '');
  if (params.get('room')) $('code').value = params.get('room').toUpperCase();

  async function enter(result) {
    if (!result || result.error) { setMsg('menu-msg', result ? result.error : 'Something went wrong.'); return; }
    me = result.playerId;
    kinds = result.kinds;
    kindById = Object.fromEntries(kinds.map((k) => [k.id, k]));
    common = result.common || [];
    mapsById = Object.fromEntries(result.maps.map((m) => [m.id, m]));
    $('room-code').textContent = result.code;
    $('menu').classList.add('hidden');
    $('lobby').classList.remove('hidden');
    buildLobby();
    if (!kindById[myKind]) myKind = kinds[0].id;
    pickHero(myKind);
    const url = new URL(location.href);
    url.searchParams.set('room', result.code);
    history.replaceState(null, '', url);
    S.unlock();
    S.playMusic('menu');
  }

  $('create').onclick = async () => { S.unlock(); if (await connect()) enter(await hub.invoke('CreateRoom', myName())); };
  $('join').onclick = async () => {
    S.unlock();
    const code = $('code').value.trim().toUpperCase();
    if (code.length !== 4) { setMsg('menu-msg', 'A room code has 4 letters.'); return; }
    if (await connect()) enter(await hub.invoke('JoinRoom', code, myName()));
  };
  $('code').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('join').click(); });
  $('name').addEventListener('keydown', (e) => { if (e.key === 'Enter') ($('code').value ? $('join') : $('create')).click(); });
  $('start').onclick = async () => { S.unlock(); const r = await hub.invoke('Start'); if (r && r.error) setMsg('lobby-msg', r.error); };
  $('leave').onclick = async () => { await hub.invoke('Leave'); me = null; state = null; showMenu(''); };
  $('share').onclick = async () => {
    const url = location.origin + LekSite.url(window.ELEMENTALS_PAGE || '/elementals') + '?room=' + $('room-code').textContent;
    try { await navigator.clipboard.writeText(url); setMsg('lobby-msg', 'Invite link copied.'); } catch { setMsg('lobby-msg', url); }
  };
  document.querySelectorAll('#modes button').forEach((b) => { b.onclick = async () => { const r = await hub.invoke('SetMode', Number(b.dataset.mode)); if (r && r.error) setMsg('lobby-msg', r.error); }; });
  document.querySelectorAll('.teams button').forEach((b) => { b.onclick = () => hub.invoke('SetTeam', Number(b.dataset.team)); });

  function showMenu(msg) {
    $('lobby').classList.add('hidden');
    $('menu').classList.remove('hidden');
    setMsg('menu-msg', msg);
    myBody = null; pending = [];
    const url = new URL(location.href);
    url.searchParams.delete('room');
    history.replaceState(null, '', url);
  }
  function setMsg(id, text) { $(id).textContent = text || ''; }

  setInterval(async () => {
    if (hub.state !== signalR.HubConnectionState.Connected) { $('ping').textContent = ''; return; }
    const t0 = performance.now();
    try { await hub.invoke('Ping', t0); $('ping').textContent = Math.round(performance.now() - t0) + ' ms'; } catch { $('ping').textContent = ''; }
  }, 2000);

  $('btn-full').onclick = async () => {
    try {
      if (document.fullscreenElement) { await document.exitFullscreen(); return; }
      await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
      if (screen.orientation && screen.orientation.lock) screen.orientation.lock('landscape').catch(() => { });
    } catch { }
  };
  const soundIcon = () => { $('btn-sound').innerHTML = S.isMuted() ? '&#128263;' : '&#128266;'; };
  soundIcon();
  $('btn-sound').onclick = () => { S.unlock(); S.setMuted(!S.isMuted()); soundIcon(); };

  // ------------------------------------------------------------------ key settings
  $('btn-keys').onclick = () => { $('keys').classList.remove('hidden'); renderBinds(); };
  $('keys-close').onclick = () => { rebinding = null; $('keys').classList.add('hidden'); renderLobbyDetail(); };
  $('keys-reset').onclick = () => { binds = JSON.parse(JSON.stringify(DEFAULT_KEYS)); store.set('elementals-keys', JSON.stringify(binds)); renderBinds(); };
  function keyLabel(code) {
    if (!code) return '-';
    return code.replace(/^Key/, '').replace(/^Digit/, '').replace('Arrow', '').replace('Space', 'Space');
  }
  function renderBinds() {
    const box = $('binds');
    box.innerHTML = '';
    for (const [action, label] of ACTIONS) {
      const l = document.createElement('span');
      l.textContent = label;
      const b = document.createElement('button');
      b.className = 'small' + (rebinding === action ? ' wait' : '');
      b.textContent = rebinding === action ? 'press a key' : (binds[action] || []).map(keyLabel).join(' / ');
      b.onclick = () => { rebinding = action; renderBinds(); };
      box.append(l, b);
    }
  }

  // ------------------------------------------------------------------ lobby
  function buildLobby() {
    const maps = $('maps');
    maps.innerHTML = '';
    const choices = [{ id: 'random', name: 'Random' }].concat(Object.values(mapsById));
    for (const m of choices) {
      const b = document.createElement('button');
      b.dataset.map = m.id;
      if (m.id !== 'random') b.appendChild(thumbOf(m.id));
      else { const c = A.makeCanvas(160, 88); const cg = c.getContext('2d'); cg.fillStyle = '#1f1a3d'; cg.fillRect(0, 0, 160, 88); cg.fillStyle = '#ffb02e'; cg.font = '40px "Pixelify Sans"'; cg.textAlign = 'center'; cg.fillText('?', 80, 58); b.appendChild(c); }
      const span = document.createElement('span');
      span.textContent = m.name;
      b.appendChild(span);
      b.onclick = async () => { const r = await hub.invoke('SetMap', m.id); if (r && r.error) setMsg('lobby-msg', r.error); };
      maps.appendChild(b);
    }
    const heroes = $('heroes');
    heroes.innerHTML = '';
    for (const k of kinds) {
      const b = document.createElement('button');
      b.dataset.kind = k.id;
      const c = A.makeCanvas(36, 40);
      c.className = 'portrait';
      if (PORTRAITS.includes(k.id)) {
        const img = new Image();
        img.src = 'portraits/' + k.id + '.png?v=1';
        img.className = 'portrait-img';
        b.appendChild(img);
      } else b.appendChild(c);
      const span = document.createElement('span');
      span.textContent = k.name;
      b.appendChild(span);
      b.onclick = () => { pickHero(k.id); S.play('click'); };
      heroes.appendChild(b);
    }
    renderLobbyDetail();
  }

  function thumbOf(id) {
    if (!thumbs[id]) {
      const m = mapsById[id];
      const full = A.makeCanvas(W, H), fg = full.getContext('2d');
      fg.drawImage(A.renderBackground(m.theme), 0, 0);
      fg.drawImage(A.renderTiles(m.theme, cellsOf(m)), 0, 0);
      A.drawLiveTiles(fg, m.theme, cellsOf(m), 0, null);
      thumbs[id] = full;
    }
    const c = A.makeCanvas(160, 88);
    const cg = c.getContext('2d');
    cg.imageSmoothingEnabled = true;
    cg.drawImage(thumbs[id], 0, 0, 160, 88);
    return c;
  }
  function cellsOf(m) {
    const out = [];
    for (const row of m.rows) for (const c of row) out.push(c === 'a' || c === 'b' || c === 'o' ? '.' : c);
    return out;
  }

  function renderLobby() {
    if (!state || !me || state.ph !== 'lobby') return;
    const owner = state.owner === me;
    const sig = JSON.stringify([state.mode, state.map, owner, state.ps.map((p) => [p.id, p.n, p.k, p.tm, p.bot, p.tw])]);
    if (sig === lobbySig) return;
    lobbySig = sig;
    document.querySelectorAll('#modes button').forEach((b) => { b.classList.toggle('on', Number(b.dataset.mode) === state.mode); b.disabled = !owner; });
    document.querySelectorAll('#maps button').forEach((b) => { b.classList.toggle('on', b.dataset.map === state.map); b.disabled = !owner; });
    $('start').disabled = !owner;
    $('start').textContent = owner ? 'Start the match' : 'Waiting for the owner';
    for (const team of [0, 1]) {
      const ul = $('team' + team);
      ul.innerHTML = '';
      const list = state.ps.filter((p) => p.tm === team);
      for (const p of list) {
        const li = document.createElement('li');
        if (p.id === me) li.className = 'me';
        const n = document.createElement('span');
        n.textContent = p.n + (p.id === state.owner ? ' ★' : '') + (p.tw ? ' (' + p.tw + ' wins)' : '');
        const k = document.createElement('span');
        k.className = 'k';
        k.textContent = (kindById[p.k] || {}).name || '';
        li.append(n, k);
        ul.appendChild(li);
      }
      const free = state.mode - list.length;
      for (let i = 0; i < free; i++) { const li = document.createElement('li'); li.innerHTML = '<span class="k">bot fills this seat</span>'; ul.appendChild(li); }
    }
    const mine = state.ps.find((p) => p.id === me);
    if (mine && mine.k !== myKind && kindById[mine.k] && !kindById[myKind]) myKind = mine.k;
    renderLobbyDetail();
  }

  function renderLobbyDetail() {
    document.querySelectorAll('#heroes button').forEach((b) => b.classList.toggle('on', b.dataset.kind === myKind));
    const k = kindById[myKind];
    const box = $('hero-detail');
    if (!k) { box.innerHTML = ''; return; }
    box.innerHTML = '';
    if (PORTRAITS.includes(k.id)) {
      const big = new Image();
      big.src = 'portraits/' + k.id + '.png?v=1';
      big.className = 'portrait-big';
      box.appendChild(big);
    }
    const h = document.createElement('h3');
    h.textContent = k.name;
    h.style.color = (A.CHARS[k.id] || {}).color || '#fff';
    const tr = document.createElement('p');
    tr.className = 'trade';
    tr.textContent = k.hearts + ' hearts. ' + k.tradeoff + '.';
    const hearts = document.createElement('div');
    hearts.className = 'hearts';
    hearts.textContent = '❤'.repeat(k.hearts);
    const kit = document.createElement('div');
    kit.className = 'kit';
    kit.className = 'kit pool';
    const armsTitle = document.createElement('p');
    armsTitle.className = 'pick-title';
    armsTitle.textContent = 'Pick 2 weapons (' + armsDraft.length + '/2): its own, or a common one anyone can carry.';
    k.weapons.concat(common).forEach((w) => {
      const slot = armsDraft.indexOf(w.id);
      const c = A.makeCanvas(24, 24), cg = c.getContext('2d');
      const img = A.weaponOf(w.id);
      if (img) { const sc = Math.min(1, 22 / img.width); cg.save(); cg.translate(12, 12); cg.rotate(-0.7); cg.drawImage(img, -img.width * sc / 2, -img.height * sc / 2, img.width * sc, img.height * sc); cg.restore(); }
      const d = document.createElement('div');
      d.className = 'pick' + (slot >= 0 ? ' on' : '');
      d.innerHTML = '<span class="key"></span><b></b><small></small>';
      d.querySelector('.key').textContent = slot >= 0 ? String(slot + 1) : '+';
      d.querySelector('b').textContent = w.name + (w.melee ? ' (close)' : ' (ranged)');
      d.querySelector('small').textContent = w.note;
      const toggle = async () => {
        if (slot >= 0) armsDraft.splice(slot, 1);
        else if (armsDraft.length < 2) armsDraft.push(w.id);
        else { armsDraft.shift(); armsDraft.push(w.id); }
        if (armsDraft.length === 2) {
          store.set('elementals-arms-' + k.id, JSON.stringify(armsDraft));
          const r = await hub.invoke('SetArms', armsDraft);
          if (r && r.error) setMsg('lobby-msg', r.error);
        }
        S.play('click');
        renderLobbyDetail();
      };
      c.onclick = toggle;
      d.onclick = toggle;
      c.className = slot >= 0 ? 'on' : '';
      kit.append(c, d);
    });
    const pickTitle = document.createElement('p');
    pickTitle.className = 'pick-title';
    pickTitle.textContent = 'Pick 3 abilities (' + draft.length + '/3). They go on ' + [0, 1, 2].map((i) => keyLabel((binds['ab' + i] || [])[0])).join(', ') + ' in the order you pick them.';
    const pool = document.createElement('div');
    pool.className = 'kit pool';
    k.pool.forEach((a) => {
      const slot = draft.indexOf(a.id);
      const c = A.makeCanvas(14, 14);
      c.getContext('2d').drawImage(A.iconOf(a.id), 0, 0);
      const d = document.createElement('div');
      d.className = 'pick' + (slot >= 0 ? ' on' : '');
      d.innerHTML = '<span class="key"></span><b></b><small></small>';
      d.querySelector('.key').textContent = slot >= 0 ? keyLabel((binds['ab' + slot] || [])[0]) : '+';
      d.querySelector('b').textContent = a.name + (a.passive ? ' · passive' : ' · ' + a.cooldown + ' s');
      d.querySelector('small').textContent = a.note;
      const toggle = async () => {
        if (slot >= 0) draft.splice(slot, 1);
        else if (draft.length < 3) draft.push(a.id);
        else { draft.shift(); draft.push(a.id); }
        if (draft.length === 3) {
          store.set('elementals-loadout-' + k.id, JSON.stringify(draft));
          const r = await hub.invoke('SetLoadout', draft);
          if (r && r.error) setMsg('lobby-msg', r.error);
        }
        S.play('click');
        renderLobbyDetail();
      };
      c.onclick = toggle;
      d.onclick = toggle;
      c.className = slot >= 0 ? 'on' : '';
      pool.append(c, d);
    });
    box.append(h, tr, hearts, armsTitle, kit, pickTitle, pool);
  }

  function drawPortraits(t) {
    document.querySelectorAll('#heroes button').forEach((b) => {
      const c = b.querySelector('canvas');
      if (!c) return;
      const cg = c.getContext('2d');
      cg.clearRect(0, 0, 36, 40);
      const sel = b.dataset.kind === myKind;
      A.drawCharacter(cg, b.dataset.kind, 18, 38, { facing: 1, aim: -0.1, t: t, weapon: 0, run: 0, swing: sel ? ((t * 0.8) % 1.6 < 1 ? (t * 0.8) % 1.6 : -1) : -1 });
    });
  }

  // ------------------------------------------------------------------ snapshots
  function onState(s) {
    const arrival = performance.now() / 1000;
    const off = s.now - arrival;
    if (clockOffset == null || off > clockOffset || Math.abs(off - clockOffset) > 1) clockOffset = off;
    else clockOffset -= 0.0005;
    if (s.ro) { roster.clear(); for (const r of s.ro) roster.set(r.id, r); }
    s.ps = s.ps.map((arr) => {
      const o = {};
      PLAYER_FIELDS.forEach((k, i) => { o[k] = arr[i]; });
      o.ab = [o.ab0, o.ab1, o.ab2];
      o.al = !!o.al;
      o.im = !!o.im;
      return Object.assign(o, roster.get(o.id) || {});
    }).filter((o) => o.n !== undefined);
    state = s;
    snaps.push(s);
    while (snaps.length > 2 && snaps[0].now < s.now - 1.5) snaps.shift();
    if (s.ph !== 'lobby' && s.map !== mapId) loadMap(s.map);
    dynMap = new Map((s.dy || []).map(([i, k]) => [i, k]));
    for (const e of s.ev || []) onEvent(e, s);
    if (s.ph !== lastPhase) onPhase(s.ph, s);
    lastPhase = s.ph;
    reconcile(s);
    renderLobby();
  }

  function loadMap(id) {
    const m = mapsById[id];
    if (!m) return;
    mapId = id;
    theme = m.theme;
    cells = cellsOf(m);
    bg = A.renderBackground(theme);
    tiles = A.renderTiles(theme, cells);
    myBody = null;
  }

  function cell(x, y) {
    if (!cells || x < 0 || y < 0 || x >= COLS || y >= ROWS) return '#';
    const i = y * COLS + x;
    const d = dynMap.get(i);
    const b = cells[i];
    if (d === 'i') return P.isSolid(b) ? '~' : b;
    return d || b;
  }

  const inFight = () => state && (state.ph === 'playing' || state.ph === 'countdown' || state.ph === 'roundend') && myBody;
  const maskFor = (ph) => (ph === 'countdown' ? 0 : ~0);

  function reconcile(s) {
    const p = s.ps.find((q) => q.id === me);
    if (!p || !p.im || !p.al || s.ph === 'lobby' || s.ph === 'over' || !cells) { myBody = null; pending = []; return; }
    const k = kindById[p.k] || { w: 14, h: 30 };
    const before = myBody ? { x: myBody.x + offset.x, y: myBody.y + offset.y } : null;
    myBody = P.body({ x: p.x, y: p.y, vx: p.vx, vy: p.vy, w: k.w, h: k.h, grounded: !!p.g, ground: p.gk, coyote: p.co, prevJump: !!p.pj, jumping: !!p.ju, dashT: p.dt, dashVX: p.dv, flyT: p.fl || 0, lockT: p.lt, speed: p.sp });
    pending = pending.filter((i) => i.seq > p.seq);
    for (const i of pending) P.step(myBody, i.bits & maskFor(s.ph), cell);
    if (before) {
      const dx = before.x - myBody.x, dy = before.y - myBody.y;
      if (Math.hypot(dx, dy) < 48) { offset.x = dx; offset.y = dy; } else { offset.x = 0; offset.y = 0; }
    }
    myPrev = { x: myBody.x, y: myBody.y };
  }

  function onPhase(ph, s) {
    if (ph === 'lobby') {
      $('lobby').classList.remove('hidden');
      lobbySig = '';
      banner = null;
      S.playMusic('menu');
      particles = []; texts = []; zaps = [];
    } else {
      $('lobby').classList.add('hidden');
      $('keys').classList.add('hidden');
      S.playMusic(ph === 'over' ? 'victory' : theme);
    }
    if (ph === 'over') {
      const mine = s.ps.find((p) => p.id === me);
      const won = mine && s.win === mine.tm;
      banner = { text: s.win < 0 ? 'DRAW' : TEAM_NAMES[s.win].toUpperCase() + ' WINS', sub: s.wins[0] + ' - ' + s.wins[1], color: s.win < 0 ? '#ffffff' : TEAM_COLORS[s.win], until: Infinity };
      S.play(won ? 'win' : 'lose');
    }
  }

  // ------------------------------------------------------------------ events
  function onEvent(e, s) {
    const who = (id) => s.ps.find((p) => p.id === id);
    const myTeam = (who(me) || {}).tm;
    switch (e.k) {
      case 'round':
        banner = { text: 'ROUND ' + e.v, sub: '', color: '#ffb02e', until: performance.now() + 2000 };
        S.play('beep');
        break;
      case 'go':
        banner = { text: 'FIGHT!', sub: '', color: '#ffffff', until: performance.now() + 900 };
        S.play('go');
        break;
      case 'roundend': {
        const text = e.v < 0 ? 'DRAW' : TEAM_NAMES[e.v].toUpperCase() + ' TAKES THE ROUND';
        banner = { text, sub: e.s === 'time' ? 'Time: compared hearts' : '', color: e.v < 0 ? '#ffffff' : TEAM_COLORS[e.v], until: performance.now() + 3500 };
        S.play(e.v === myTeam ? 'win' : e.v < 0 ? 'beep' : 'lose');
        break;
      }
      case 'hit': {
        const tick = TICK_SOURCES.has(e.s);
        flashes.set(e.b, performance.now());
        const color = e.b === me ? '#ff5a5a' : tick ? '#ffb27a' : '#ffffff';
        texts.push({ x: e.x + (Math.random() - 0.5) * 6, y: e.y - 14, text: '-' + hearts(e.v), color, t: 0, life: tick ? 0.6 : 0.9, small: tick });
        burst(e.x, e.y, sourceColor(e.s), tick ? 3 : 8, tick ? 50 : 90);
        if (!tick || e.b === me) S.play(e.b === me ? 'hurt' : 'hit');
        if (e.b === me && !tick) shake = Math.max(shake, 4);
        break;
      }
      case 'death':
        burst(e.x, e.y, '#ffffff', 30, 160);
        burst(e.x, e.y, '#ff3040', 20, 120);
        shake = Math.max(shake, 7);
        S.play('death');
        texts.push({ x: e.x, y: e.y - 24, text: (who(e.b) || {}).n + ' is down', color: '#ffb02e', t: 0, life: 1.6 });
        break;
      case 'heal':
        texts.push({ x: e.x, y: e.y - 6, text: '+' + hearts(e.v), color: '#6fff8a', t: 0, life: 0.9 });
        if (e.a === me) S.play('heal');
        break;
      case 'swing': {
        const p = who(e.a);
        if (e.a !== me) S.play(swingSound(e.s));
        break;
      }
      case 'shoot':
        if (e.a !== me) S.play(e.s === 'fireball' ? 'fire' : e.s === 'shard' ? 'ice' : 'shoot');
        break;
      case 'zap':
        zaps.push({ x1: e.x, y1: e.y, x2: e.x2, y2: e.y2, t: 0, life: e.s === 'chain' || e.s === 'drain' ? 0.35 : 0.15, seed: Math.random() * 1000 | 0, color: e.s === 'drain' ? '#ff2a3a' : null });
        S.play('zap');
        break;
      case 'pop':
        burst(e.x, e.y, sourceColor(e.s), 6, 60);
        break;
      case 'boom':
        boom(e);
        break;
      case 'cast': {
        const p = who(e.a);
        if (e.s === 'thorns') S.play('thorns');
        else if (e.s === 'slide' || e.s === 'wall' || e.s === 'freeze') S.play('ice');
        else if (e.s === 'pillar' || e.s === 'meteor') S.play('fire');
        else if (e.s === 'judgement' || e.s === 'chain') S.play('zap');
        else if (e.s === 'garden' || e.s === 'cage' || e.s === 'regrow' || e.s === 'shield' || e.s === 'thornskin') S.play('grow');
        else if (e.s === 'armor' || e.s === 'blizzard') S.play('ice');
        else if (e.s === 'firedash' || e.s === 'inferno') S.play('fire');
        else if (e.s === 'clap') S.play('thunder');
        else if (e.s === 'bat' || e.s === 'thirst' || e.s === 'bloodpool' || e.s === 'drain' || e.s === 'decoy' || e.s === 'window' || e.s === 'rewind') S.play('blink');
        if (p) burst(p.x, p.y - 16, (A.CHARS[p.k] || {}).color || '#fff', 10, 70);
        break;
      }
      case 'grow': case 'wall':
        burst(e.x, e.y, e.k === 'wall' ? '#bdeaff' : '#6fd04f', 14, 80);
        break;
      case 'blink':
        burst(e.x, e.y, '#ffd84a', 14, 90);
        burst(e.x2, e.y2, '#8ff0ff', 14, 90);
        zaps.push({ x1: e.x, y1: e.y, x2: e.x2, y2: e.y2, t: 0, life: 0.12, seed: 7 });
        S.play('blink');
        break;
      case 'pot':
        burst(e.x, e.y, '#c8763c', 14, 100);
        texts.push({ x: e.x, y: e.y - 10, text: e.s.toUpperCase(), color: BUFF_COLORS[e.s] || '#fff', t: 0, life: 1.4 });
        S.play('pot');
        if (e.a === me) S.play('pickup', 0.1);
        break;
      case 'mark':
        burst(e.x, e.y - 15, '#5afff0', 10, 60);
        if (e.a === me) texts.push({ x: e.x, y: e.y - 40, text: 'MARKED', color: '#5afff0', t: 0, life: 0.9 });
        S.play('blink');
        break;
      case 'poof':
        burst(e.x, e.y, '#5afff0', 18, 90);
        S.play('blink');
        break;
      case 'block':
        texts.push({ x: e.x, y: e.y - 8, text: 'BLOCK', color: '#8fd463', t: 0, life: 0.8 });
        burst(e.x, e.y, '#8fd463', 10, 80);
        S.play('block');
        break;
      case 'absorb':
        texts.push({ x: e.x, y: e.y - 8, text: 'ABSORBED', color: '#bdeaff', t: 0, life: 0.8 });
        burst(e.x, e.y, '#bdeaff', 10, 80);
        S.play('ice');
        break;
      case 'fizzle':
        if (e.a === me) texts.push({ x: (who(me) || {}).x || 0, y: ((who(me) || {}).y || 0) - 40, text: 'nobody in reach', color: '#aaa', t: 0, life: 1 });
        break;
    }
  }
  function swingSound(weapon) { return weapon === 'hammer' || weapon === 'flameaxe' || weapon === 'pixelstick' ? 'heavy' : weapon === 'claws' ? 'claw' : 'swing'; }
  const TICK_SOURCES = new Set(['burn', 'pillar', 'inferno', 'blizzard', 'spine']);
  function hearts(halves) { const whole = Math.floor(halves / 2); return (whole ? String(whole) : '') + (halves % 2 ? '½' : '') || '0'; }
  const BUFF_COLORS = { speed: '#4dc3ff', regen: '#4dff7a', power: '#ff4d4d', haste: '#c77dff', heal: '#4dff7a' };
  function sourceColor(s) {
    return { nail: '#8fd463', thrower: '#8fd463', spine: '#f4ecc2', cactusbomb: '#8fd463', thorns: '#f4ecc2', cactus: '#6fd04f', frostblade: '#bdeaff', shard: '#9fe6ff', freeze: '#bdeaff', flameaxe: '#ff8a1f', fireball: '#ff8a1f', rocket: '#ffb02e', pillar: '#ff8a1f', meteor: '#ff6a1f', burn: '#ff6a1f', hammer: '#ffd84a', stormrod: '#8ff0ff', claws: '#ffffff', sword: '#ffffff', arrow: '#e6eaf5', pixelstick: '#5afff0', bubble: '#bff7ff', magnet: '#ff4d6a', cube: '#b06bff', blood: '#ff2a3a', drain: '#ff2a3a', firedash: '#ff8a1f', inferno: '#ff8a1f', blizzard: '#ffffff', clap: '#ffd84a', thornskin: '#f4ecc2', chain: '#8ff0ff', judgement: '#ffffff', spikes: '#cccccc', icicle: '#bdeaff' }[s] || '#ffffff';
  }
  function burst(x, y, color, n, speed) {
    for (let i = 0; i < n; i++) {
      const a = Math.random() * Math.PI * 2, v = speed * (0.3 + Math.random() * 0.7);
      particles.push({ x, y, vx: Math.cos(a) * v, vy: Math.sin(a) * v - 40, t: 0, life: 0.35 + Math.random() * 0.4, color, size: Math.random() < 0.3 ? 2 : 1, grav: 300 });
    }
  }
  function boom(e) {
    if (e.s === 'meteor') { burst(e.x, e.y - 6, '#ff6a1f', 50, 220); burst(e.x, e.y - 6, '#ffd23f', 30, 160); burst(e.x, e.y, '#4d3a35', 20, 120); shake = Math.max(shake, 9); S.play('boom'); }
    else if (e.s === 'judgement') { zaps.push({ x1: e.x + 6, y1: 0, x2: e.x, y2: e.y, t: 0, life: 0.4, seed: 3, wide: true }); burst(e.x, e.y - 4, '#ffffff', 30, 180); burst(e.x, e.y, '#8ff0ff', 20, 140); shake = Math.max(shake, 8); S.play('thunder'); }
    else if (e.s === 'freeze') { ring(e.x, e.y, e.v, '#bdeaff'); burst(e.x, e.y, '#ffffff', 30, 160); S.play('freeze'); }
    else if (e.s === 'rocket') { burst(e.x, e.y, '#ff8a1f', 30, 140); burst(e.x, e.y, '#ffd23f', 16, 100); shake = Math.max(shake, 4); S.play('boom'); }
    else if (e.s === 'thorns') { burst(e.x, e.y, '#8fd463', 14, 80); S.play('thorns'); }
    else if (e.s === 'clap') { ring(e.x, e.y, e.v, '#ffd84a'); burst(e.x, e.y, '#ffffff', 16, 120); shake = Math.max(shake, 5); }
  }
  function ring(x, y, r, color) {
    for (let i = 0; i < 40; i++) {
      const a = i / 40 * Math.PI * 2;
      particles.push({ x: x + Math.cos(a) * 6, y: y + Math.sin(a) * 6, vx: Math.cos(a) * r * 3, vy: Math.sin(a) * r * 3, t: 0, life: 0.33, color, size: 2, grav: 0 });
    }
  }

  // ------------------------------------------------------------------ input tick (30 Hz)
  function myAim() {
    if (!myBody) return 0;
    const cx = myBody.x + offset.x, cy = myBody.y + offset.y - myBody.h * 0.6;
    if (isTouch && !mouse.down) {
      const target = nearestEnemy(cx, cy);
      if (target) return Math.atan2(target.y - target.h / 2 - cy, target.x - cx);
    }
    if (aimMode === 'mouse' && mouseInUse()) return Math.atan2(mouse.y - cy, mouse.x - cx);
    return aimFacing > 0 ? aimElev : Math.PI - aimElev;
  }
  /** The same rule as the server: face the way you move, unless you are attacking or aiming with the mouse. */
  function myFacing(aim) {
    const left = isDown('left'), right = isDown('right');
    const attacking = isDown('attack') || mouse.down;
    if (!attacking && left !== right) return right ? 1 : -1;
    if (attacking || (aimMode === 'mouse' && mouseInUse())) return Math.cos(aim) >= 0 ? 1 : -1;
    return aimFacing;
  }
  function nearestEnemy(x, y) {
    if (!state) return null;
    const mine = state.ps.find((p) => p.id === me);
    let best = null, bd = 1e9;
    for (const p of state.ps) {
      if (!p.im || !p.al || !mine || p.tm === mine.tm) continue;
      const d = Math.hypot(p.x - x, p.y - y);
      const k = kindById[p.k] || { h: 30 };
      if (d < bd) { bd = d; best = { x: p.x, y: p.y, h: k.h }; }
    }
    return best;
  }

  function inputTick() {
    if (!state || !myBody || hub.state !== signalR.HubConnectionState.Connected) return;
    if (aimMode === 'keys') {
      if (isDown('aimUp')) aimElev = Math.max(-1.45, aimElev - 3 * P.Dt);
      if (isDown('aimDown')) aimElev = Math.min(1.45, aimElev + 3 * P.Dt);
      if (isDown('aimLeft')) aimFacing = -1;
      if (isDown('aimRight')) aimFacing = 1;
    }
    let bits = 0;
    if (isDown('left')) { bits |= P.Left; if (!isDown('right')) aimFacing = -1; }
    if (isDown('right')) { bits |= P.Right; if (!isDown('left')) aimFacing = 1; }
    if (isDown('jump')) bits |= P.Jump;
    if (isDown('down')) bits |= P.Down;
    const attacking = isDown('attack') || mouse.down;
    if (attacking) bits |= P.Attack;
    if (document.hidden) bits = 0;
    const aim = myAim();
    seq++;
    pending.push({ seq, bits });
    if (pending.length > 90) pending.shift();
    myPrev = { x: myBody.x, y: myBody.y };
    const wasGrounded = myBody.grounded;
    P.step(myBody, bits & maskFor(state.ph), cell);
    if (!wasGrounded && myBody.grounded && myBody.landV > 250) S.play('land');
    stepAt = performance.now();
    hub.send('Input', seq, bits, aim).catch(() => { });
    // predicted swing for instant feedback
    const mine = state.ps.find((p) => p.id === me);
    localCd = Math.max(0, localCd - P.Dt);
    if (attacking && localCd <= 0 && mine && state.ph === 'playing' && myBody.lockT <= 0) {
      const w = weaponDef(mine.k, (mine.wp || [])[mine.w]);
      if (w && !(mine.st & 8192)) {
        localCd = w.cooldown;
        if (w.melee) { localSwingAt = performance.now(); S.play(swingSound(w.id)); }
        else S.play(w.id === 'firewand' ? 'fire' : w.id === 'icestaff' || w.id === 'bubble' ? 'ice' : w.id === 'stormrod' ? 'zap' : 'shoot');
      }
    }
  }
  function useAbility(slot) {
    if (!state || state.ph !== 'playing') return;
    hub.send('Ability', slot).catch(() => { });
  }
  function swapWeapon(to) {
    if (!state) return;
    const mine = state.ps.find((p) => p.id === me);
    if (!mine) return;
    const next = to == null ? 1 - mine.w : to;
    hub.send('Swap', next).catch(() => { });
  }

  // ------------------------------------------------------------------ render
  let last = performance.now(), acc = 0;
  function frame(ms) {
    const dt = Math.min(0.1, (ms - last) / 1000);
    last = ms;
    acc += dt;
    let n = 0;
    while (acc >= P.Dt && n < 4) { inputTick(); acc -= P.Dt; n++; }
    if (acc > P.Dt * 4) acc = 0;
    const t = ms / 1000;
    offset.x *= Math.pow(0.0005, dt);
    offset.y *= Math.pow(0.0005, dt);

    g.setTransform(1, 0, 0, 1, 0, 0);
    if (state && state.ph !== 'lobby' && cells) drawGame(t, dt);
    else drawBackdrop(t);
    if (!$('lobby').classList.contains('hidden')) drawPortraits(t);
    requestAnimationFrame(frame);
  }

  function drawBackdrop(t) {
    const id = state && state.map && mapsById[state.map] ? state.map : (Object.keys(mapsById)[0] || null);
    if (id && mapsById[id]) {
      const m = mapsById[id];
      if (!drawBackdrop.cache || drawBackdrop.cache.id !== id) drawBackdrop.cache = { id, bg: A.renderBackground(m.theme), tiles: A.renderTiles(m.theme, cellsOf(m)), cells: cellsOf(m), theme: m.theme };
      const c = drawBackdrop.cache;
      g.drawImage(c.bg, 0, 0);
      A.drawAmbient(g, c.theme, t);
      g.drawImage(c.tiles, 0, 0);
      A.drawLiveTiles(g, c.theme, c.cells, t, null);
    } else {
      const grd = g.createLinearGradient(0, 0, 0, H);
      grd.addColorStop(0, '#1d1838'); grd.addColorStop(1, '#07060f');
      g.fillStyle = grd; g.fillRect(0, 0, W, H);
      A.drawAmbient(g, 'castle', t);
    }
    g.fillStyle = 'rgba(5,4,12,0.45)';
    g.fillRect(0, 0, W, H);
  }

  // others are drawn 150 ms in the past: two 15 Hz snapshots of margin smooth over a jittery connection
  function renderTime() { return performance.now() / 1000 + (clockOffset || 0) - 0.15; }
  function sampleAt(rt) {
    let a = snaps[0], b = snaps[snaps.length - 1];
    for (let i = 0; i < snaps.length - 1; i++) if (snaps[i].now <= rt && snaps[i + 1].now >= rt) { a = snaps[i]; b = snaps[i + 1]; break; }
    if (rt > b.now) a = b;
    const k = b.now > a.now ? Math.min(1, Math.max(0, (rt - a.now) / (b.now - a.now))) : 1;
    return { a, b, k };
  }

  function drawGame(t, dt) {
    const s = state;
    const sx = shake > 0 ? (Math.random() - 0.5) * shake : 0, sy = shake > 0 ? (Math.random() - 0.5) * shake : 0;
    shake = Math.max(0, shake - dt * 30);
    g.setTransform(1, 0, 0, 1, Math.round(sx), Math.round(sy));
    g.drawImage(bg, 0, 0);
    A.drawAmbient(g, theme, t);
    g.drawImage(tiles, 0, 0);
    const pads = s.pads && s.pads.length ? padList().map((cellsOfPad, i) => ({ cells: cellsOfPad, effect: s.pads[i] })) : null;
    A.drawLiveTiles(g, theme, cells, t, pads);
    drawDyn(t);
    drawThings(s, t);

    const { a, b, k } = sampleAt(renderTime());
    // shots
    for (const shot of b.sh) {
      const prev = a.sh.find((x) => x.i === shot.i);
      const x = prev ? prev.x + (shot.x - prev.x) * k : shot.x, y = prev ? prev.y + (shot.y - prev.y) * k : shot.y;
      A.drawProjectile(g, shot.k, x, y, shot.vx, shot.vy, t);
    }
    // players
    const latest = s.ps;
    for (const p of latest) {
      if (!p.im || !p.al) continue;
      let x, y, vx, vy, air;
      if (p.id === me && myBody) {
        const alpha = Math.min(1, (performance.now() - stepAt) / (P.Dt * 1000));
        x = (myPrev ? myPrev.x + (myBody.x - myPrev.x) * alpha : myBody.x) + offset.x;
        y = (myPrev ? myPrev.y + (myBody.y - myPrev.y) * alpha : myBody.y) + offset.y;
        vx = myBody.vx; vy = myBody.vy; air = !myBody.grounded;
      } else {
        const pa = a.ps.find((q) => q.id === p.id), pb = b.ps.find((q) => q.id === p.id) || p;
        x = pa ? pa.x + (pb.x - pa.x) * k : pb.x;
        y = pa ? pa.y + (pb.y - pa.y) * k : pb.y;
        vx = pb.vx; vy = pb.vy; air = !pb.g;
      }
      const flash = flashes.get(p.id);
      const hurt = flash ? Math.max(0, 1 - (performance.now() - flash) / 250) : 0;
      const isMe = p.id === me;
      const aim = isMe ? myAim() : p.a;
      const facing = isMe ? myFacing(aim) : p.f;
      let swing = p.sw < 0.28 ? p.sw / 0.28 : -1;
      if (isMe) { const ls = (performance.now() - localSwingAt) / 280; swing = ls >= 0 && ls < 1 ? ls : -1; }
      // team marker under the feet
      g.fillStyle = TEAM_COLORS[p.tm];
      g.globalAlpha = 0.5;
      g.fillRect(Math.round(x) - 6, Math.round(y), 12, 1);
      g.globalAlpha = 1;
      A.drawCharacter(g, p.k, x, y, {
        bat: (p.st & 8192) !== 0,
        weaponId: (p.wp || [])[p.w], melee: (weaponDef(p.k, (p.wp || [])[p.w]) || {}).melee,
        facing, aim, t: t + p.id.charCodeAt(0), run: air ? 0 : Math.min(1, Math.abs(vx) / 120), air, vy, weapon: p.w, swing,
        hurt, frozen: (p.st & 64) !== 0, cast: (p.st & 256) ? 0.6 + Math.sin(t * 30) * 0.3 : 0
      });
      drawStatus(p, x, y, t);
      drawTag(p, x, y);
      if (p.id === me) drawMark(p, t);
    }
    // effects
    drawZaps(dt);
    drawParticles(dt);
    drawTexts(dt);
    g.setTransform(1, 0, 0, 1, 0, 0);
    drawHud(s, t);
  }

  let padCache = null;
  function padList() {
    if (padCache && padCache.map === mapId) return padCache.list;
    const list = [], seen = new Set();
    cells.forEach((c, i) => {
      if (c !== 'P' || seen.has(i)) return;
      const pad = [];
      for (let j = i; cells[j] === 'P' && Math.floor(j / COLS) === Math.floor(i / COLS); j++) { seen.add(j); pad.push(j); }
      list.push(pad);
    });
    padCache = { map: mapId, list };
    return list;
  }

  function drawDyn(t) {
    for (const [i, k] of dynMap) {
      const x = (i % COLS) * T, y = Math.floor(i / COLS) * T;
      if (k === 'c') {
        g.fillStyle = '#183014'; g.fillRect(x + 2, y, 12, T);
        g.fillStyle = '#4fa03c'; g.fillRect(x + 3, y, 10, T);
        g.fillStyle = '#8fd463'; g.fillRect(x + 4, y, 2, T);
        g.fillStyle = '#2e6e27'; g.fillRect(x + 10, y, 2, T);
        g.fillStyle = '#f4ecc2';
        for (let s = 0; s < 4; s++) { g.fillRect(x + 1 + (s % 2) * 13, y + 2 + s * 4, 1, 1); }
      } else if (k === 'w') {
        g.fillStyle = '#2f6f9a'; g.fillRect(x, y, T, T);
        g.fillStyle = '#9fe6ff'; g.fillRect(x + 1, y + 1, T - 2, T - 2);
        g.fillStyle = '#d8f3ff'; g.fillRect(x + 2, y + 2, 3, T - 4);
        g.fillStyle = '#ffffff'; g.fillRect(x + 3, y + 3, 1, 4);
        A.glow(g, x + 8, y + 8, 12, '#7fe3ff', 0.25);
      } else if (k === '=') {
        g.fillStyle = 'rgba(90,255,240,0.35)'; g.fillRect(x, y, T, 5);
        g.fillStyle = '#5afff0'; g.fillRect(x, y, T, 1);
        g.fillStyle = 'rgba(90,255,240,0.6)'; g.fillRect(x + ((t * 40 + i * 3) % 14 | 0), y + 1, 2, 3);
        A.glow(g, x + 8, y + 2, 12, '#5afff0', 0.2);
      } else if (k === 'i') {
        g.fillStyle = 'rgba(190,240,255,0.75)'; g.fillRect(x, y, T, 3);
        g.fillStyle = '#ffffff'; g.fillRect(x + ((t * 30 + i) % 14 | 0), y, 2, 1);
      }
    }
  }

  function drawThings(s, t) {
    for (const th of s.th) {
      if (th.k === 'pot') drawPot(th.x, th.y);
      else if (th.k === 'plant') drawPlant(th.x, th.y, th.age, th.tm);
      else if (th.k === 'pillar') drawPillar(th, t);
      else if (th.k === 'meteor') drawMeteorMark(th, t);
      else if (th.k === 'judgement') drawJudgementMark(th, t);
      else if (th.k === 'blizzard') drawBlizzard(th, t);
      else if (th.k === 'inferno') drawInferno(th, t);
      else if (th.k === 'bloodpool') drawBloodPool(th, t);
      else if (th.k === 'decoy') drawDecoy(th, t);
    }
  }
  const potImgs = {};
  function drawPot(x, y) {
    const rows = ['..oooo..', '.obbbbo.', '..obbo..', '.obbbbo.', 'obwbbbbo', 'obbbbbBo', 'obbbbBBo', '.oBBBBo.', '..oooo..'];
    const pal = theme === 'antarctica' ? { o: '#1b2a3a', b: '#7a9cbf', B: '#4a6a8f', w: '#d6eef9' } : theme === 'volcano' ? { o: '#140a08', b: '#7a3a22', B: '#4a2214', w: '#ffb02e' } : { o: '#3a1f10', b: '#c8763c', B: '#8e4f22', w: '#f2c08a' };
    potImgs[theme] = potImgs[theme] || A.compile(rows, pal);
    g.drawImage(potImgs[theme], Math.round(x) - 4, Math.round(y) - 9);
  }
  let plantImg = null;
  function drawPlant(x, y, age, team) {
    if (!plantImg) plantImg = A.shadeSprite(['..g...', '..g..g', 'g.g..g', 'g.gggg', 'gggg..', '..g...', '..g...', '..g...', '..g...', '..g...', '.ggg..'], { g: ['#a6e46f', '#56a83f', '#2f6e27'] }, '#14260f');
    const grow = Math.min(1, age / 0.25);
    const h = Math.round(plantImg.height * grow);
    g.drawImage(plantImg, 0, plantImg.height - h, plantImg.width, h, Math.round(x) - 4, Math.round(y) - h, plantImg.width, h);
    g.fillStyle = TEAM_COLORS[team];
    g.fillRect(Math.round(x) - 1, Math.round(y) - h - 2, 2, 1);
  }
  function drawPillar(th, t) {
    const x = Math.round(th.x), y = Math.round(th.y);
    if (th.age < 0.3) {
      g.fillStyle = 'rgba(255,120,30,' + (0.3 + Math.sin(t * 40) * 0.2) + ')';
      g.fillRect(x - 12, y - 3, 24, 3);
      return;
    }
    A.glow(g, x, y - 30, 40, '#ff6a1f', 0.6);
    for (let i = 0; i < 60; i++) {
      const life = (t * 1.8 + A.hash(i, 1, 33)) % 1;
      const px = x + (A.hash(i, 2, 33) - 0.5) * 22 * (1 - life * 0.5) + Math.sin(t * 9 + i) * 2;
      const py = y - life * 64;
      g.fillStyle = life < 0.3 ? '#fff6a8' : life < 0.6 ? '#ffb02e' : life < 0.85 ? '#ff6a1f' : '#8e2a0c';
      g.fillRect(Math.round(px), Math.round(py), life < 0.5 ? 3 : 2, life < 0.5 ? 3 : 2);
    }
  }
  function drawMeteorMark(th, t) {
    const x = Math.round(th.x), y = Math.round(th.y), k = Math.min(1, th.age / 1.0);
    g.strokeStyle = 'rgba(255,90,31,' + (0.5 + Math.sin(t * 20) * 0.3) + ')';
    g.lineWidth = 1;
    g.beginPath(); g.ellipse(x, y - 1, 42 * (0.4 + k * 0.6), 6, 0, 0, Math.PI * 2); g.stroke();
    g.fillStyle = 'rgba(255,90,31,0.15)'; g.fill();
    const my = y - 260 * (1 - k * k);
    const mx = x - 120 * (1 - k * k);
    A.glow(g, mx, my, 26, '#ff6a1f', 0.9);
    g.fillStyle = '#4d3a35'; g.fillRect(mx - 6, my - 6, 12, 12);
    g.fillStyle = '#ff8a1f'; g.fillRect(mx - 4, my - 4, 8, 8);
    g.fillStyle = '#ffd23f'; g.fillRect(mx - 2, my - 2, 4, 4);
  }
  function drawJudgementMark(th, t) {
    const x = Math.round(th.x), w = th.w;
    g.fillStyle = 'rgba(143,240,255,' + (0.1 + Math.abs(Math.sin(t * 18)) * 0.15) + ')';
    g.fillRect(x - w / 2, 0, w, th.y);
    g.fillStyle = '#ffffff';
    g.fillRect(x - w / 2, th.y - 1, w, 1);
    g.fillStyle = 'rgba(30,30,50,0.6)';
    g.fillRect(x - w, 0, w * 2, 8);
  }

  function drawDecoy(th, t) {
    const owner = state.ps.find((p) => p.id === th.o);
    if (!owner) return;
    // the decoy looks like its owner; a faint flicker gives it away to a careful eye
    const flicker = Math.sin(t * 40) > 0.85 ? 0.75 : 1;
    A.drawCharacter(g, owner.k, th.x, th.y, { facing: th.f, aim: th.f > 0 ? 0 : Math.PI, t: t + 3, run: th.g ? 1 : 0, air: !th.g, vy: 0, weapon: owner.w, weaponId: (owner.wp || [])[owner.w], swing: -1, alpha: flicker });
    drawTag(owner, th.x, th.y);
  }
  function drawMark(p, t) {
    if (!p.mk) return;
    const [x, y] = p.mk;
    g.strokeStyle = 'rgba(90,255,240,' + (0.4 + Math.sin(t * 6) * 0.2) + ')';
    g.strokeRect(Math.round(x) - 7.5, Math.round(y) - 30.5, 15, 30);
    g.fillStyle = '#5afff0';
    g.fillRect(Math.round(x) - 1, Math.round(y) - 36, 2, 3);
  }
  function drawBlizzard(th, t) {
    const x0 = th.x - th.w / 2, y0 = th.y - th.h;
    g.fillStyle = 'rgba(200,235,255,0.12)';
    g.fillRect(Math.round(x0), Math.round(y0), th.w, th.h);
    for (let i = 0; i < 50; i++) {
      const px = x0 + ((A.hash(i, 1, 44) * th.w + t * 70 + i * 3) % th.w);
      const py = y0 + ((A.hash(i, 2, 44) * th.h + t * 50) % th.h);
      g.fillStyle = i % 4 ? '#ffffff' : '#bdeaff';
      g.fillRect(Math.round(px), Math.round(py), i % 5 ? 1 : 2, 1);
    }
    g.fillStyle = 'rgba(230,240,255,0.5)';
    g.fillRect(Math.round(x0) - 4, Math.round(y0) - 6, th.w + 8, 7);
  }
  function drawInferno(th, t) {
    for (let i = 0; i < 36; i++) {
      const a = i / 36 * Math.PI * 2 + t * 2;
      const life = (t * 2 + A.hash(i, 3, 55)) % 1;
      const r = 40 + Math.sin(t * 10 + i) * 2;
      g.fillStyle = life < 0.4 ? '#ffd23f' : life < 0.75 ? '#ff8a1f' : '#a8300f';
      g.fillRect(Math.round(th.x + Math.cos(a) * r), Math.round(th.y - 14 + Math.sin(a) * r * 0.6 - life * 6), 2, 2);
    }
    A.glow(g, th.x, th.y - 14, 46, '#ff6a1f', 0.25);
  }
  function drawBloodPool(th, t) {
    const x0 = Math.round(th.x - th.w / 2), y = Math.round(th.y);
    g.fillStyle = '#4a0410'; g.fillRect(x0 + 2, y - 2, th.w - 4, 2);
    g.fillStyle = '#c8102e'; g.fillRect(x0 + 4, y - 2, th.w - 8, 1);
    for (let i = 0; i < 6; i++) { g.fillStyle = '#ff5a6a'; g.fillRect(x0 + 6 + ((i * 11 + t * 8) % (th.w - 12) | 0), y - 2, 2, 1); }
    if (A.hash(Math.floor(t * 4), th.i, 2) > 0.6) { g.fillStyle = '#c8102e'; g.fillRect(x0 + (A.hash(Math.floor(t * 4), th.i, 3) * th.w | 0), y - 4, 1, 2); }
  }

  function drawStatus(p, x, y, t) {
    if (p.st & 32) for (let i = 0; i < 3; i++) { g.fillStyle = i ? '#ff8a1f' : '#ffd23f'; g.fillRect(Math.round(x - 6 + A.hash(i, Math.floor(t * 12), 3) * 12), Math.round(y - 4 - A.hash(i, Math.floor(t * 12), 4) * 26), 2, 2); }
    if (p.st & 1) { g.fillStyle = '#9fe6ff'; g.fillRect(Math.round(x) - 7, Math.round(y) - 2, 14, 2); }
    if (p.st & 128) for (let i = 0; i < 3; i++) { const a = t * 8 + i * 2.1; g.fillStyle = '#ffd84a'; g.fillRect(Math.round(x + Math.cos(a) * 8), Math.round(y - 38 + Math.sin(a) * 2), 2, 2); }
    if (p.st & 512) {
      const fx = Math.round(x + p.f * 9), fy = Math.round(y - 26);
      g.fillStyle = '#14260f'; g.fillRect(fx - 2, fy, 5, 18);
      g.fillStyle = '#56a83f'; g.fillRect(fx - 1, fy + 1, 3, 16);
      g.fillStyle = '#f4ecc2'; g.fillRect(fx + p.f * 2, fy + 4, 1, 1); g.fillRect(fx + p.f * 2, fy + 11, 1, 1);
    }
    if (p.st & 1024) { g.strokeStyle = 'rgba(190,240,255,0.8)'; g.strokeRect(Math.round(x) - 9.5, Math.round(y) - 33.5, 19, 33); }
    if (p.st & 2048) { g.fillStyle = '#f4ecc2'; for (let i = 0; i < 8; i++) { const a = i / 8 * Math.PI * 2 + t; g.fillRect(Math.round(x + Math.cos(a) * 11), Math.round(y - 15 + Math.sin(a) * 17), 1, 1); } }
    if (p.st & 4096) A.glow(g, x, y - 16, 18, '#ff2a3a', 0.35 + Math.sin(t * 8) * 0.1);
    if (p.st & 2) A.glow(g, x, y - 4, 10, '#4dc3ff', 0.4);
    if (p.st & 4) A.glow(g, x, y - 16, 16, '#ff4d4d', 0.3);
    if (p.st & 8) A.glow(g, x, y - 16, 16, '#c77dff', 0.3);
  }
  function drawTag(p, x, y) {
    const k = kindById[p.k] || { h: 30 };
    const top = Math.round(y - k.h - 12);
    g.font = '8px "Pixelify Sans"';
    g.textAlign = 'center';
    const text = p.n;
    const w = Math.ceil(g.measureText(text).width) + 4;
    g.fillStyle = 'rgba(0,0,0,0.55)';
    g.fillRect(Math.round(x - w / 2), top - 8, w, 9);
    g.fillStyle = p.id === me ? '#ffb02e' : TEAM_COLORS[p.tm];
    g.fillText(text, Math.round(x), top - 1);
    // health bar: one segment per heart
    const hearts = p.mh / 2, bw = hearts * 3 + 1;
    const bx = Math.round(x - bw / 2), by = top + 2;
    g.fillStyle = '#000';
    g.fillRect(bx, by, bw, 3);
    for (let i = 0; i < hearts; i++) {
      const v = p.hp - i * 2;
      g.fillStyle = v >= 2 ? '#e8283c' : v === 1 ? '#8a1a24' : '#2a1014';
      g.fillRect(bx + 1 + i * 3, by + 1, 2, 1);
    }
  }

  function drawZaps(dt) {
    zaps = zaps.filter((z) => (z.t += dt) < z.life);
    for (const z of zaps) A.drawLightning(g, z.x1, z.y1, z.x2, z.y2, z.seed + Math.floor(z.t * 30), z.color || (z.wide ? '#ffffff' : '#8ff0ff'), z.wide ? 4 : 2);
  }
  function drawParticles(dt) {
    particles = particles.filter((p) => (p.t += dt) < p.life);
    for (const p of particles) {
      p.vy += p.grav * dt;
      p.x += p.vx * dt; p.y += p.vy * dt;
      g.globalAlpha = Math.max(0, 1 - p.t / p.life);
      g.fillStyle = p.color;
      g.fillRect(Math.round(p.x), Math.round(p.y), p.size, p.size);
    }
    g.globalAlpha = 1;
    if (particles.length > 900) particles.splice(0, particles.length - 900);
  }
  function drawTexts(dt) {
    texts = texts.filter((x) => (x.t += dt) < x.life);
    g.textAlign = 'center';
    for (const x of texts) {
      g.font = x.small ? '8px "Pixelify Sans"' : '10px "Pixelify Sans"';
      const y = Math.round(x.y - x.t * 22);
      g.globalAlpha = Math.max(0, 1 - Math.pow(x.t / x.life, 3));
      g.fillStyle = '#000';
      g.fillText(x.text, Math.round(x.x) + 1, y + 1);
      g.fillStyle = x.color;
      g.fillText(x.text, Math.round(x.x), y);
    }
    g.globalAlpha = 1;
  }

  function drawHud(s, t) {
    // score and clock
    g.fillStyle = 'rgba(0,0,0,0.6)';
    g.fillRect(262, 3, 116, 22);
    A.drawDigits(g, String(s.wins[0]), 280, 8, 2, TEAM_COLORS[0], 'center');
    A.drawDigits(g, String(s.wins[1]), 360, 8, 2, TEAM_COLORS[1], 'center');
    const tl = s.ph === 'playing' ? Math.ceil(s.tl) : s.ph === 'countdown' ? 90 : 0;
    A.drawDigits(g, Math.floor(tl / 60) + ':' + String(tl % 60).padStart(2, '0'), 320, 8, 2, s.ph === 'playing' && s.tl < 10 ? '#ff5a5a' : '#ffffff', 'center');
    g.font = '8px "Pixelify Sans"';
    g.fillStyle = '#a39bbd';
    g.fillText('ROUND ' + s.rd, 320, 33);

    // team lists in the corners
    for (const team of [0, 1]) {
      const list = s.ps.filter((p) => p.im && p.tm === team);
      list.forEach((p, i) => {
        const x = team === 0 ? 22 : W - 22 - 70, y = 8 + i * 16;
        g.globalAlpha = p.al ? 1 : 0.4;
        g.fillStyle = 'rgba(0,0,0,0.5)';
        g.fillRect(x - 2, y - 1, 74, 14);
        g.font = '8px "Pixelify Sans"';
        g.textAlign = 'left';
        g.fillStyle = TEAM_COLORS[team];
        g.fillText(p.n.slice(0, 12), x, y + 6);
        A.drawHearts(g, x, y + 7, p.hp, p.mh, 0.6);
        g.globalAlpha = 1;
      });
    }

    const mine = s.ps.find((p) => p.id === me);
    if (mine && mine.im) {
      const k = kindById[mine.k];
      if (mine.al && k) {
        // hearts
        A.drawHearts(g, 22, H - 44, mine.hp, mine.mh, 1);
        // weapons
        (mine.wp || []).map((id) => weaponDef(mine.k, id)).forEach((w, i) => {
          if (!w) return;
          const x = 200 + i * 34, y = H - 38;
          g.fillStyle = i === mine.w ? 'rgba(255,176,46,0.35)' : 'rgba(0,0,0,0.55)';
          g.fillRect(x, y, 30, 30);
          g.strokeStyle = i === mine.w ? '#ffb02e' : 'rgba(255,255,255,0.25)';
          g.strokeRect(x + 0.5, y + 0.5, 29, 29);
          const img = A.weaponOf(w.id);
          g.save(); g.translate(x + 15, y + 15); g.rotate(-0.75);
          const sc = Math.min(1, 26 / img.width);
          g.drawImage(img, -img.width * sc / 2, -img.height * sc / 2, img.width * sc, img.height * sc);
          g.restore();
          if (i === mine.w && mine.cd > 0) { g.fillStyle = 'rgba(0,0,0,0.55)'; g.fillRect(x + 1, y + 1, 28, 28 * Math.min(1, mine.cd / w.cooldown)); }
          g.fillStyle = '#ffffff'; g.font = '8px "Pixelify Sans"'; g.textAlign = 'left'; g.fillText(String(i + 1), x + 2, y + 9);
        });
        // abilities
        (mine.lo || []).forEach((id, i) => {
          const a = k.pool.find((q) => q.id === id) || { cooldown: 1, passive: false };
          const x = 282 + i * 36, y = H - 40;
          g.fillStyle = 'rgba(0,0,0,0.6)'; g.fillRect(x, y, 32, 32);
          g.drawImage(A.iconOf(id), x + 2, y + 2, 28, 28);
          if (a.passive) {
            g.strokeStyle = 'rgba(255,255,255,0.35)'; g.strokeRect(x + 0.5, y + 0.5, 31, 31);
            g.fillStyle = '#a39bbd'; g.font = '8px "Pixelify Sans"'; g.textAlign = 'left'; g.fillText('P', x + 2, y + 9);
            return;
          }
          const cd = mine.ab[i];
          if (cd > 0) {
            g.fillStyle = 'rgba(0,0,0,0.65)'; g.fillRect(x + 1, y + 1, 30, 30 * (cd / a.cooldown));
            A.drawDigits(g, String(Math.ceil(cd)), x + 16, y + 11, 2, '#ffffff', 'center');
          } else {
            g.strokeStyle = (A.CHARS[k.id] || {}).color || '#fff'; g.strokeRect(x + 0.5, y + 0.5, 31, 31);
          }
          g.fillStyle = '#ffb02e'; g.font = '8px "Pixelify Sans"'; g.textAlign = 'left'; g.fillText(keyLabel((binds['ab' + i] || [])[0]), x + 2, y + 9);
        });
        // buffs
        const buffs = [[2, 'SPEED', '#4dc3ff'], [4, 'POWER', '#ff4d4d'], [8, 'HASTE', '#c77dff'], [16, 'REGEN', '#4dff7a'], [1, 'SLOWED', '#9fe6ff'], [32, 'BURNING', '#ff8a1f']];
        let bx = 400;
        for (const [bit, label, color] of buffs) {
          if (!(mine.st & bit)) continue;
          g.fillStyle = color; g.font = '8px "Pixelify Sans"'; g.textAlign = 'left';
          g.fillText(label, bx, H - 30);
          bx += g.measureText(label).width + 8;
        }
      } else if (s.ph === 'playing') {
        g.font = '12px "Pixelify Sans"'; g.textAlign = 'center'; g.fillStyle = '#ffffff';
        g.fillText('You are down. Your team fights on.', W / 2, H - 20);
      }
    } else if (s.ph !== 'over') {
      g.font = '12px "Pixelify Sans"'; g.textAlign = 'center'; g.fillStyle = '#ffffff';
      g.fillText('Watching. You join at the next match.', W / 2, H - 20);
    }

    // countdown
    if (s.ph === 'countdown') {
      const n = Math.ceil(s.tl);
      A.drawDigits(g, String(n), W / 2, H / 2 - 44, 7, '#ffffff', 'center');
      if (s.rd === 1) {
        g.font = '10px "Pixelify Sans"'; g.textAlign = 'center'; g.fillStyle = '#e0dcf0';
        g.fillText('A/D move · W jump · mouse aims · click attacks · Q swaps · R T Y abilities', W / 2, H / 2 + 24);
      }
    }
    if (banner && (banner.until === Infinity || performance.now() < banner.until)) {
      if (!(s.ph === 'countdown' && banner.text.startsWith('ROUND') === false)) {
        bigText(banner.text, banner.color, s.ph === 'countdown' ? H / 2 - 60 : H / 2 - 30, s.ph === 'countdown' ? 22 : 30);
        if (banner.sub) { g.font = '12px "Pixelify Sans"'; g.fillStyle = '#ffffff'; g.textAlign = 'center'; g.fillText(banner.sub, W / 2, H / 2); }
      }
    }
    if (s.ph === 'over') {
      g.font = '10px "Pixelify Sans"'; g.fillStyle = '#a39bbd'; g.textAlign = 'center';
      g.fillText('Back to the lobby in ' + Math.ceil(s.tl), W / 2, H / 2 + 18);
    }
  }
  function bigText(text, color, y, size) {
    g.font = '700 ' + size + 'px "Pixelify Sans"';
    g.textAlign = 'center';
    g.fillStyle = '#000';
    g.fillText(text, W / 2 + 2, y + 2);
    g.fillStyle = color;
    g.fillText(text, W / 2, y);
  }

  // ------------------------------------------------------------------ touch
  const isTouch = matchMedia('(pointer: coarse)').matches;
  if (isTouch) {
    $('touch').classList.remove('hidden');
    document.querySelectorAll('#touch [data-hold]').forEach((b) => {
      const a = b.dataset.hold;
      b.addEventListener('pointerdown', (e) => { e.preventDefault(); virtual.add(a); S.unlock(); });
      for (const ev of ['pointerup', 'pointercancel', 'pointerleave']) b.addEventListener(ev, () => virtual.delete(a));
    });
    document.querySelectorAll('#touch [data-press]').forEach((b) => {
      b.addEventListener('pointerdown', (e) => { e.preventDefault(); pressAction(b.dataset.press); });
    });
  }

  Promise.all([document.fonts.load('12px "Pixelify Sans"'), document.fonts.load('700 30px "Pixelify Sans"')]).catch(() => { }).then(() => { });
  requestAnimationFrame(frame);
})();
