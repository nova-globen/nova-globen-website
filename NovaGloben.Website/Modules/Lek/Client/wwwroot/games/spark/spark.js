/* Spark - browser client. The server owns the fight (Lek.Server.Spark); this file draws its snapshots, sends
   movement, aim and presses, predicts the player's own movement, and draws every telegraph so nothing is a surprise. */
(() => {
  'use strict';

  const TILE = 16;
  const RADIUS = 6;
  const COLORS = ['#ff5d3b', '#3bd1ff', '#7cff5d', '#ffd23b', '#d45bff', '#ff8ac6', '#5d7cff', '#ffa43b', '#3bffc8', '#f2f2f2'];
  const ELEMENT = { blaze: '#ff5d3b', frost: '#3bd1ff', bolt: '#ffd23b' };
  const $ = (id) => document.getElementById(id);
  const canvas = $('game');
  const ctx = canvas.getContext('2d');
  ctx.imageSmoothingEnabled = false;

  const params = new URLSearchParams(location.search);
  const ui = {
    lobby: $('lobby'), connectForm: $('connect-form'), roomForm: $('room-form'), name: $('name'), code: $('code'),
    create: $('create'), join: $('join'), connectMsg: $('connect-msg'), roomCode: $('room-code'), players: $('players'),
    addbot: $('addbot'), removebot: $('removebot'), start: $('start'), leave: $('leave'), share: $('share'), roomMsg: $('room-msg'),
    kinds: $('kinds'), hud: $('hud'), hudLeft: $('hud-left'), hudCenter: $('hud-center'), hudRight: $('hud-right'),
    bars: $('bars'), hp: $('hp-bar'), cd1: $('cd1'), cd2: $('cd2'), score: $('score'), banner: $('banner'), ping: $('ping'),
    fullscreen: $('fullscreen'), touch: $('touch'), btnFire: $('btn-fire'), btnAbility: $('btn-ability')
  };

  let map = null, kinds = [], me = null, state = null;
  let shown = new Map();
  let fx = [];            // {kind, x, y, x2, y2, t, color}
  let bannerUntil = 0, lastPhase = 'lobby', floorCanvas = null, lastFrame = 0;
  let aim = 0;            // radians, from the mouse
  let mouse = null;       // last mouse position in canvas pixels

  try { ui.name.value = params.get('name') || localStorage.getItem(LekSite.key('spark-name')) || ''; } catch { ui.name.value = params.get('name') || ''; }
  if (params.get('room')) ui.code.value = params.get('room').toUpperCase();

  const mine = () => (state && me) ? state.players.find((p) => p.id === me) : null;
  const nameOf = (id) => ((state && state.players.find((p) => p.id === id)) || {}).name || '?';

  // ---------------------------------------------------------------- connection
  const hub = new signalR.HubConnectionBuilder().withUrl(LekSite.url('/hubs/spark')).withAutomaticReconnect().configureLogging(signalR.LogLevel.Warning).build();

  hub.on('state', (s) => {
    state = s;
    for (const e of s.events || []) onEvent(e);
    if (s.phase !== lastPhase) onPhase(s.phase, s);
    lastPhase = s.phase;
    renderLobby();
  });
  hub.onreconnected(() => { me = null; state = null; showConnect('Connection was lost. Join again.'); });
  hub.onclose(() => showConnect('Disconnected.'));

  async function connect() {
    if (hub.state === signalR.HubConnectionState.Connected) return true;
    try { await hub.start(); return true; }
    catch (err) { setMsg(ui.connectMsg, 'Cannot reach the server.'); console.error(err); return false; }
  }
  function myName() { const n = ui.name.value.trim().slice(0, 14); try { localStorage.setItem(LekSite.key('spark-name'), n); } catch { } return n; }

  async function enter(result) {
    if (!result || result.error) { setMsg(ui.connectMsg, result ? result.error : 'Something went wrong.'); return; }
    me = result.playerId;
    map = result.map;
    kinds = result.kinds || [];
    floorCanvas = null;
    shown.clear();
    ui.kinds.innerHTML = '';
    for (const k of kinds) {
      const b = document.createElement('button');
      b.className = 'kind ' + k.element;
      b.innerHTML = '<b></b><small></small><small></small>';
      b.querySelector('b').textContent = k.name;
      b.querySelectorAll('small')[0].textContent = k.primary;
      b.querySelectorAll('small')[1].textContent = k.ability;
      b.onclick = async () => { const r = await hub.invoke('SetKind', k.id); if (r && r.error) setMsg(ui.roomMsg, r.error); };
      b.dataset.kind = k.id;
      ui.kinds.appendChild(b);
    }
    ui.roomCode.textContent = result.code;
    ui.connectForm.classList.add('hidden');
    ui.roomForm.classList.remove('hidden');
    ui.fullscreen.classList.remove('hidden');
    ui.ping.classList.remove('hidden');
    setMsg(ui.connectMsg, '');
    const url = new URL(location.href);
    url.searchParams.set('room', result.code);
    history.replaceState(null, '', url);
    audio.unlock();
  }

  ui.create.onclick = async () => { if (await connect()) enter(await hub.invoke('CreateRoom', myName())); };
  ui.join.onclick = async () => {
    const code = ui.code.value.trim().toUpperCase();
    if (code.length !== 4) { setMsg(ui.connectMsg, 'A room code has 4 letters.'); return; }
    if (await connect()) enter(await hub.invoke('JoinRoom', code, myName()));
  };
  ui.code.addEventListener('keydown', (e) => { if (e.key === 'Enter') ui.join.click(); });
  ui.name.addEventListener('keydown', (e) => { if (e.key === 'Enter') (ui.code.value ? ui.join : ui.create).click(); });
  ui.start.onclick = async () => { audio.unlock(); const r = await hub.invoke('Start'); if (r && r.error) setMsg(ui.roomMsg, r.error); };
  ui.addbot.onclick = async () => { const r = await hub.invoke('AddBot'); if (r && r.error) setMsg(ui.roomMsg, r.error); };
  ui.removebot.onclick = async () => { const r = await hub.invoke('RemoveBot'); if (r && r.error) setMsg(ui.roomMsg, r.error); };
  ui.leave.onclick = async () => { await hub.invoke('Leave'); me = null; state = null; showConnect(''); };
  ui.share.onclick = async () => {
    const url = location.origin + LekSite.url('/spark') + '?room=' + ui.roomCode.textContent;
    try { await navigator.clipboard.writeText(url); setMsg(ui.roomMsg, 'Link copied: ' + url); } catch { setMsg(ui.roomMsg, url); }
  };
  ui.fullscreen.onclick = async () => {
    try {
      if (document.fullscreenElement) { await document.exitFullscreen(); return; }
      await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
      if (screen.orientation && screen.orientation.lock) screen.orientation.lock('landscape').catch(() => { });
    } catch { setMsg(ui.roomMsg, 'Full screen is not available here.'); }
  };

  function showConnect(msg) {
    ui.lobby.classList.remove('hidden');
    ui.roomForm.classList.add('hidden');
    ui.connectForm.classList.remove('hidden');
    for (const el of [ui.hud, ui.bars, ui.score, ui.fullscreen, ui.ping, ui.touch]) el.classList.add('hidden');
    setMsg(ui.connectMsg, msg);
    const url = new URL(location.href);
    url.searchParams.delete('room');
    history.replaceState(null, '', url);
  }
  function setMsg(el, text) { el.textContent = text || ''; }

  setInterval(async () => {
    if (hub.state !== signalR.HubConnectionState.Connected) { ui.ping.textContent = ''; return; }
    const t0 = performance.now();
    try { await hub.invoke('Ping', t0); ui.ping.textContent = Math.round(performance.now() - t0) + ' ms'; } catch { ui.ping.textContent = ''; }
  }, 2000);

  // ---------------------------------------------------------------- lobby and hud
  function renderLobby() {
    if (!state || !me) return;
    const my = mine();
    const owner = state.owner === me;
    document.querySelectorAll('.owner-only').forEach((el) => el.classList.toggle('off', !owner));
    const inLobby = state.phase === 'lobby';
    ui.lobby.classList.toggle('hidden', !inLobby);
    for (const el of [ui.hud, ui.bars, ui.score]) el.classList.toggle('hidden', inLobby);
    ui.touch.classList.toggle('hidden', inLobby);
    if (inLobby) {
      for (const b of ui.kinds.children) b.classList.toggle('picked', my && b.dataset.kind === my.kind);
      ui.players.innerHTML = '';
      for (const p of state.players) {
        const li = document.createElement('li');
        const sw = document.createElement('span'); sw.className = 'swatch'; sw.style.background = COLORS[p.color % COLORS.length]; li.appendChild(sw);
        const name = document.createElement('span'); name.textContent = p.name + (p.bot ? ' (bot)' : '') + (p.id === state.owner ? ' *' : ''); if (p.id === me) name.className = 'you'; li.appendChild(name);
        const k = document.createElement('span'); k.className = 'kind-tag'; k.textContent = (kinds.find((x) => x.id === p.kind) || {}).name || p.kind; li.appendChild(k);
        ui.players.appendChild(li);
      }
      ui.start.textContent = state.players.length < 2 ? 'Start (a bot joins)' : 'Start';
      setMsg(ui.roomMsg, owner ? '' : 'Waiting for ' + nameOf(state.owner) + ' to start...');
    } else {
      const t = Math.ceil(state.timeLeft || 0);
      ui.hudLeft.textContent = state.phase === 'playing' ? 'FIRST TO ' + state.target + '   ' + Math.floor(t / 60) + ':' + String(t % 60).padStart(2, '0') : '';
      ui.hudRight.textContent = 'ROOM ' + state.code;
      if (my && !my.inMatch) ui.hudCenter.textContent = 'WATCHING - NEXT MATCH SOON';
      else if (my && !my.alive) ui.hudCenter.textContent = 'RESPAWN IN ' + Math.ceil(my.respawn || 0);
      else if (my && my.cast) ui.hudCenter.textContent = 'WINDING UP...';
      else ui.hudCenter.textContent = '';
      if (my) {
        ui.hp.style.width = Math.max(0, my.hp) + '%';
        ui.hp.style.background = my.hp > 50 ? '#7cff5d' : my.hp > 25 ? '#ffd23b' : '#ff5d3b';
        ui.cd1.classList.toggle('ready', !my.cd1); ui.cd1.textContent = my.cd1 ? 'FIRE ' + my.cd1.toFixed(1) : 'FIRE';
        ui.cd2.classList.toggle('ready', !my.cd2); ui.cd2.textContent = my.cd2 ? 'ABILITY ' + my.cd2.toFixed(1) : 'ABILITY READY';
      }
      const rows = [...state.players].filter((p) => p.inMatch).sort((a, b) => b.kills - a.kills || a.deaths - b.deaths);
      ui.score.innerHTML = '';
      for (const p of rows) {
        const d = document.createElement('div');
        d.textContent = p.name + '  ' + p.kills + ' / ' + p.deaths;
        if (p.id === me) d.className = 'me';
        ui.score.appendChild(d);
      }
    }
  }

  function banner(text, sub, ms) {
    ui.banner.innerHTML = '';
    ui.banner.appendChild(document.createTextNode(text));
    if (sub) { const s = document.createElement('small'); s.textContent = sub; ui.banner.appendChild(s); }
    ui.banner.classList.remove('hidden');
    bannerUntil = performance.now() + ms;
  }

  function onPhase(phase, s) {
    if (phase === 'countdown') banner('FIGHT IN', 'first to ' + s.target + ' kills', 3000);
    else if (phase === 'over') {
      const w = s.players.find((p) => p.id === s.winner);
      banner(w ? (w.id === me ? 'YOU WIN!' : w.name.toUpperCase() + ' WINS') : 'DRAW', 'back to the lobby in a moment', 7000);
    }
  }

  function onEvent(e) {
    const col = (id) => ELEMENT[((state && state.players.find((p) => p.id === id)) || {}).kind] || '#fff';
    if (e.kind === 'go') { banner('GO!', '', 700); audio.go(); }
    else if (e.kind === 'zap') { fx.push({ kind: 'line', x: e.x, y: e.y, x2: e.x2, y2: e.y2, t: performance.now(), color: '#ffd23b', ms: 120 }); audio.zap(); }
    else if (e.kind === 'hit') { fx.push({ kind: 'hit', x: e.x, y: e.y, t: performance.now(), color: col(e.a), ms: 250 }); if (e.b === me) audio.hurt(); else audio.hit(); }
    else if (e.kind === 'kill') {
      fx.push({ kind: 'burst', x: e.x, y: e.y, t: performance.now(), color: col(e.b), ms: 600 });
      audio.kill();
      if (e.a === me) banner('KILL', nameOf(e.b), 900); else if (e.b === me) banner('YOU DIED', 'by ' + nameOf(e.a), 1500);
    }
    else if (e.kind === 'nova') { fx.push({ kind: 'ring', x: e.x, y: e.y, t: performance.now(), color: '#3bd1ff', ms: 400, r: 60 }); audio.nova(); }
    else if (e.kind === 'blink') { fx.push({ kind: 'line', x: e.x, y: e.y, x2: e.x2, y2: e.y2, t: performance.now(), color: '#ffd23b', ms: 200 }); audio.blink(); }
    else if (e.kind === 'dash') { audio.dash(); }
    else if (e.kind === 'cast') { audio.cast(); }
  }

  // ---------------------------------------------------------------- input
  const keys = new Set();
  let lastSent = { dx: 0, dy: 0, aim: 0 };
  let touchOrigin = null, touchVec = { dx: 0, dy: 0 };

  function keyVec() {
    let dx = 0, dy = 0;
    if (keys.has('ArrowLeft') || keys.has('a') || keys.has('A')) dx -= 1;
    if (keys.has('ArrowRight') || keys.has('d') || keys.has('D')) dx += 1;
    if (keys.has('ArrowUp') || keys.has('w') || keys.has('W')) dy -= 1;
    if (keys.has('ArrowDown') || keys.has('s') || keys.has('S')) dy += 1;
    return { dx, dy };
  }
  function currentInput() {
    let v = keyVec();
    if (v.dx === 0 && v.dy === 0) v = touchVec;
    const len = Math.hypot(v.dx, v.dy);
    return len > 1 ? { dx: v.dx / len, dy: v.dy / len } : v;
  }
  function updateAim() {
    const d = shown.get(me);
    if (mouse && d) aim = Math.atan2(mouse.y - d.y, mouse.x - d.x);
    else { const v = currentInput(); if (v.dx || v.dy) aim = Math.atan2(v.dy, v.dx); }
  }
  function sendInput(force) {
    if (!me || hub.state !== signalR.HubConnectionState.Connected) return;
    updateAim();
    const v = currentInput();
    if (!force && Math.abs(v.dx - lastSent.dx) < 0.02 && Math.abs(v.dy - lastSent.dy) < 0.02 && Math.abs(aim - lastSent.aim) < 0.03) return;
    lastSent = { dx: v.dx, dy: v.dy, aim };
    hub.send('Input', v.dx, v.dy, aim).catch(() => { });
  }
  const fire = () => { if (!me) return; updateAim(); audio.unlock(); hub.send('Fire', aim).catch(() => { }); };
  const ability = () => { if (!me) return; updateAim(); audio.unlock(); hub.send('Ability', aim).catch(() => { }); };

  const onKeyDown = (e) => {
    if (e.target && ['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'].includes(e.target.tagName)) return;
    if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', ' '].includes(e.key)) e.preventDefault();
    if (e.repeat) return;
    if (e.key === ' ') { fire(); return; }
    if (e.key === 'Shift' || e.key === 'e' || e.key === 'E') { ability(); return; }
    keys.add(e.key);
    sendInput();
  };
  const onKeyUp = (e) => { keys.delete(e.key); sendInput(); };
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('keyup', onKeyUp);
  window.addEventListener('blur', () => { keys.clear(); touchVec = { dx: 0, dy: 0 }; sendInput(true); });
  try {
    if (window.parent !== window) {
      window.parent.addEventListener('keydown', onKeyDown);
      window.parent.addEventListener('keyup', onKeyUp);
      window.parent.addEventListener('focus', () => { if (me) window.focus(); });
    }
  } catch { }

  const toCanvas = (e) => { const r = canvas.getBoundingClientRect(); return { x: (e.clientX - r.left) / r.width * canvas.width, y: (e.clientY - r.top) / r.height * canvas.height }; };
  canvas.addEventListener('contextmenu', (e) => e.preventDefault());
  canvas.addEventListener('pointermove', (e) => {
    if (e.pointerType === 'mouse') { mouse = toCanvas(e); sendInput(); return; }
    if (!touchOrigin || e.pointerId !== touchOrigin.id) return;
    const dx = e.clientX - touchOrigin.x, dy = e.clientY - touchOrigin.y;
    const len = Math.hypot(dx, dy), dead = 8, full = 40;
    touchVec = len < dead ? { dx: 0, dy: 0 } : { dx: dx / len * Math.min(1, (len - dead) / (full - dead)), dy: dy / len * Math.min(1, (len - dead) / (full - dead)) };
    mouse = null;
    sendInput();
  });
  canvas.addEventListener('pointerdown', (e) => {
    window.focus();
    audio.unlock();
    if (e.pointerType === 'mouse') { mouse = toCanvas(e); if (e.button === 2) ability(); else if (e.button === 0) fire(); return; }
    canvas.setPointerCapture(e.pointerId);
    touchOrigin = { x: e.clientX, y: e.clientY, id: e.pointerId };
  });
  const endTouch = (e) => { if (touchOrigin && e.pointerId === touchOrigin.id) { touchOrigin = null; touchVec = { dx: 0, dy: 0 }; sendInput(); } };
  canvas.addEventListener('pointerup', endTouch);
  canvas.addEventListener('pointercancel', endTouch);
  ui.btnFire.addEventListener('pointerdown', (e) => { e.preventDefault(); fire(); });
  ui.btnAbility.addEventListener('pointerdown', (e) => { e.preventDefault(); ability(); });
  setInterval(() => sendInput(true), 1000);

  // ---------------------------------------------------------------- prediction
  function blocked(x, y, r) {
    if (!map) return false;
    const x0 = Math.floor((x - r) / TILE), x1 = Math.floor((x + r) / TILE), y0 = Math.floor((y - r) / TILE), y1 = Math.floor((y + r) / TILE);
    for (let ty = y0; ty <= y1; ty++) for (let tx = x0; tx <= x1; tx++) if (tx < 0 || ty < 0 || ty >= map.length || tx >= map[0].length || map[ty][tx] === '#') return true;
    return false;
  }
  function predictMine(d, p, dt) {
    const canMove = p.alive && !p.cast && !p.dash && (state.phase === 'lobby' || (state.phase === 'playing' && p.inMatch));
    const v = canMove ? currentInput() : { dx: 0, dy: 0 };
    const speed = p.slow ? 60 : 100;
    if (v.dx || v.dy) {
      const nx = d.x + v.dx * speed * dt, ny = d.y + v.dy * speed * dt;
      if (!blocked(nx, d.y, RADIUS)) d.x = nx;
      if (!blocked(d.x, ny, RADIUS)) d.y = ny;
      d.moving = true;
    } else d.moving = false;
    const ex = p.x - d.x, ey = p.y - d.y, err = Math.hypot(ex, ey);
    if (err > 28) { d.x = p.x; d.y = p.y; }
    else if (err > 0.5) { const k = Math.min(1, dt * (v.dx || v.dy ? 2.5 : 8)); d.x += ex * k; d.y += ey * k; }
  }

  // ---------------------------------------------------------------- drawing
  function drawFloor() {
    if (floorCanvas || !map) return;
    floorCanvas = document.createElement('canvas');
    floorCanvas.width = canvas.width; floorCanvas.height = canvas.height;
    const g = floorCanvas.getContext('2d');
    for (let y = 0; y < map.length; y++) {
      for (let x = 0; x < map[0].length; x++) {
        const px = x * TILE, py = y * TILE;
        if (map[y][x] === '#') {
          g.fillStyle = '#2a2550'; g.fillRect(px, py, TILE, TILE);
          g.fillStyle = '#4a4180'; g.fillRect(px, py, TILE, 3);
          g.fillStyle = '#1a1636'; g.fillRect(px, py + TILE - 2, TILE, 2);
        } else {
          g.fillStyle = ((x + y) & 1) ? '#3b3660' : '#403a68'; g.fillRect(px, py, TILE, TILE);
          g.fillStyle = 'rgba(255,255,255,.04)'; g.fillRect(px, py, TILE, 1); g.fillRect(px, py, 1, TILE);
        }
      }
    }
  }

  function drawPlayer(p, pos, now) {
    const col = COLORS[p.color % COLORS.length];
    const el = ELEMENT[p.kind] || '#fff';
    const x = Math.round(pos.x), y = Math.round(pos.y);
    const a = p.id === me ? aim : p.aim;
    const step = pos.moving ? Math.floor(now / 110) % 2 : 0;
    const left = Math.cos(a) < 0;
    if (p.inv) { ctx.strokeStyle = 'rgba(255,255,255,' + (0.4 + 0.4 * Math.sin(now / 80)) + ')'; ctx.lineWidth = 1; ctx.beginPath(); ctx.arc(x, y - 3, 11, 0, Math.PI * 2); ctx.stroke(); }
    if (p.cast) {
      // the telegraph: a growing ring in the element's colour; the ability lands when it closes
      const k = Math.pow(p.cast, 0.5);
      ctx.strokeStyle = el; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(x, y - 3, 8 + k * 18, 0, Math.PI * 2); ctx.stroke();
      if (p.kind === 'frost') { ctx.strokeStyle = 'rgba(59,209,255,.35)'; ctx.setLineDash([3, 3]); ctx.beginPath(); ctx.arc(x, y, 60, 0, Math.PI * 2); ctx.stroke(); ctx.setLineDash([]); }
      if (p.kind === 'bolt') { ctx.strokeStyle = 'rgba(255,210,59,.5)'; ctx.setLineDash([2, 4]); ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + Math.cos(a) * 64, y + Math.sin(a) * 64); ctx.stroke(); ctx.setLineDash([]); }
      if (p.kind === 'blaze') { ctx.strokeStyle = 'rgba(255,93,59,.5)'; ctx.setLineDash([2, 4]); ctx.beginPath(); ctx.moveTo(x, y); ctx.lineTo(x + Math.cos(a) * 70, y + Math.sin(a) * 70); ctx.stroke(); ctx.setLineDash([]); }
    }
    ctx.fillStyle = 'rgba(0,0,0,.35)'; ctx.fillRect(x - 5, y + 4, 10, 3);
    ctx.fillStyle = '#1a1626'; ctx.fillRect(x - 4, y + 1 + (step ? 1 : 0), 3, 4); ctx.fillRect(x + 1, y + 1 + (step ? 0 : 1), 3, 4);
    ctx.fillStyle = col; ctx.fillRect(x - 5, y - 5, 10, 7);
    ctx.fillStyle = el; ctx.fillRect(x - 5, y - 5, 10, 2);
    ctx.fillStyle = '#f1c9a5'; ctx.fillRect(x - 4, y - 12, 8, 7);
    ctx.fillStyle = el; ctx.fillRect(x - 4, y - 12, 8, 2);
    ctx.fillStyle = '#111'; if (left) ctx.fillRect(x - 3, y - 9, 2, 2); else ctx.fillRect(x + 1, y - 9, 2, 2);
    // the weapon arm points where they aim
    ctx.strokeStyle = el; ctx.lineWidth = 2;
    ctx.beginPath(); ctx.moveTo(x, y - 2); ctx.lineTo(x + Math.cos(a) * 9, y - 2 + Math.sin(a) * 9); ctx.stroke();
    if (p.slow) { ctx.fillStyle = 'rgba(59,209,255,.7)'; ctx.font = '6px "Press Start 2P", monospace'; ctx.textAlign = 'center'; ctx.fillText('*', x + 8, y - 12); }
    // hp bar + name
    ctx.fillStyle = 'rgba(0,0,0,.6)'; ctx.fillRect(x - 8, y - 17, 16, 3);
    ctx.fillStyle = p.hp > 50 ? '#7cff5d' : p.hp > 25 ? '#ffd23b' : '#ff5d3b'; ctx.fillRect(x - 8, y - 17, Math.max(0, 16 * p.hp / 100), 3);
    ctx.font = '6px "Press Start 2P", monospace'; ctx.textAlign = 'center';
    ctx.fillStyle = 'rgba(0,0,0,.7)'; ctx.fillText(p.name, x + 1, y - 19);
    ctx.fillStyle = p.id === me ? '#ffd23b' : '#fff'; ctx.fillText(p.name, x, y - 20);
  }

  function drawShot(s, now) {
    if (s.kind === 'fireball') {
      ctx.fillStyle = '#ff5d3b'; ctx.beginPath(); ctx.arc(s.x, s.y, 4, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = '#ffd23b'; ctx.beginPath(); ctx.arc(s.x - Math.cos(s.a) * 2, s.y - Math.sin(s.a) * 2, 2, 0, Math.PI * 2); ctx.fill();
    } else {
      ctx.strokeStyle = '#3bd1ff'; ctx.lineWidth = 2;
      ctx.beginPath(); ctx.moveTo(s.x - Math.cos(s.a) * 5, s.y - Math.sin(s.a) * 5); ctx.lineTo(s.x + Math.cos(s.a) * 3, s.y + Math.sin(s.a) * 3); ctx.stroke();
    }
  }

  function frame(now) {
    requestAnimationFrame(frame);
    const dt = Math.min(0.05, (now - lastFrame) / 1000 || 0);
    lastFrame = now;
    ctx.fillStyle = '#0b0a16'; ctx.fillRect(0, 0, canvas.width, canvas.height);
    if (!map || !state) { drawIdle(now); return; }
    drawFloor();
    ctx.drawImage(floorCanvas, 0, 0);
    const alive = new Set();
    for (const p of state.players) {
      alive.add(p.id);
      let d = shown.get(p.id);
      if (!d) { d = { x: p.x, y: p.y, moving: false }; shown.set(p.id, d); }
      if (p.id === me) { predictMine(d, p, dt); continue; }
      const dx = p.x - d.x, dy = p.y - d.y, dist = Math.hypot(dx, dy);
      if (dist > 40) { d.x = p.x; d.y = p.y; } else { d.x += dx * 0.35; d.y += dy * 0.35; }
      d.moving = dist > 0.6;
    }
    for (const id of [...shown.keys()]) if (!alive.has(id)) shown.delete(id);
    updateAim();
    for (const s of state.shots || []) drawShot(s, now);
    const order = [...state.players].filter((p) => p.alive && (p.inMatch || state.phase === 'lobby')).sort((a, b) => a.y - b.y);
    for (const p of order) drawPlayer(p, shown.get(p.id), now);
    fx = fx.filter((e) => now - e.t < e.ms);
    for (const e of fx) {
      const k = Math.min(1, Math.max(0, (now - e.t) / e.ms)); // an event can be stamped a hair after this frame's clock
      ctx.strokeStyle = e.color; ctx.fillStyle = e.color; ctx.globalAlpha = 1 - k; ctx.lineWidth = 2;
      if (e.kind === 'line') { ctx.beginPath(); ctx.moveTo(e.x, e.y); ctx.lineTo(e.x2, e.y2); ctx.stroke(); }
      else if (e.kind === 'hit') { ctx.beginPath(); ctx.arc(e.x, e.y, 3 + k * 8, 0, Math.PI * 2); ctx.stroke(); }
      else if (e.kind === 'burst') { for (let i = 0; i < 8; i++) { const a = i * Math.PI / 4; ctx.fillRect(e.x + Math.cos(a) * k * 28 - 1, e.y + Math.sin(a) * k * 28 - 1, 3, 3); } }
      else if (e.kind === 'ring') { ctx.beginPath(); ctx.arc(e.x, e.y, e.r * Math.min(1, k * 1.5), 0, Math.PI * 2); ctx.stroke(); }
      ctx.globalAlpha = 1;
    }
    // the crosshair
    if (mouse && state.phase !== 'lobby') { ctx.strokeStyle = 'rgba(255,255,255,.7)'; ctx.lineWidth = 1; ctx.strokeRect(mouse.x - 3, mouse.y - 3, 6, 6); }
    if (state.phase === 'countdown') {
      ctx.font = '28px "Press Start 2P", monospace'; ctx.textAlign = 'center';
      const n = Math.ceil(state.countdown);
      ctx.fillStyle = '#000'; ctx.fillText(n, canvas.width / 2 + 3, canvas.height / 2 + 13);
      ctx.fillStyle = '#ffd23b'; ctx.fillText(n, canvas.width / 2, canvas.height / 2 + 10);
    }
    if (bannerUntil && now > bannerUntil) { ui.banner.classList.add('hidden'); bannerUntil = 0; }
  }
  function drawIdle(now) {
    const x = canvas.width / 2 + Math.sin(now / 900) * 120, y = canvas.height / 2 + 40;
    ctx.strokeStyle = '#ffd23b'; ctx.lineWidth = 2; ctx.beginPath();
    for (let i = 0; i < 6; i++) ctx.lineTo(x + (i % 2 ? 6 : -6), y - 20 + i * 7);
    ctx.stroke();
  }
  requestAnimationFrame(frame);

  // ---------------------------------------------------------------- audio
  const audio = (() => {
    let ac = null, master = null;
    function unlock() {
      if (!ac) { try { ac = new (window.AudioContext || window.webkitAudioContext)(); master = ac.createGain(); master.gain.value = 0.18; master.connect(ac.destination); } catch { return; } }
      if (ac.state === 'suspended') ac.resume();
    }
    function tone(freq, start, dur, type, gain, slide) {
      const o = ac.createOscillator(); const g = ac.createGain();
      o.type = type; o.frequency.setValueAtTime(freq, start); if (slide) o.frequency.exponentialRampToValueAtTime(slide, start + dur);
      g.gain.setValueAtTime(gain, start); g.gain.exponentialRampToValueAtTime(0.001, start + dur);
      o.connect(g); g.connect(master); o.start(start); o.stop(start + dur + 0.02);
    }
    function noise(start, dur, gain) {
      const len = Math.floor(ac.sampleRate * dur); const buf = ac.createBuffer(1, len, ac.sampleRate); const d = buf.getChannelData(0);
      for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
      const s = ac.createBufferSource(); s.buffer = buf; const g = ac.createGain(); g.gain.value = gain; s.connect(g); g.connect(master); s.start(start);
    }
    const t = () => ac.currentTime;
    return {
      unlock,
      go() { unlock(); if (!ac) return; tone(523, t(), 0.1, 'square', 0.4); tone(784, t() + 0.1, 0.2, 'square', 0.4); },
      zap() { unlock(); if (!ac) return; tone(1200, t(), 0.06, 'sawtooth', 0.3, 300); },
      hit() { unlock(); if (!ac) return; tone(220, t(), 0.06, 'square', 0.25); },
      hurt() { unlock(); if (!ac) return; tone(160, t(), 0.12, 'sawtooth', 0.4, 80); },
      kill() { unlock(); if (!ac) return; noise(t(), 0.3, 0.6); tone(90, t(), 0.3, 'sawtooth', 0.5, 40); },
      nova() { unlock(); if (!ac) return; tone(400, t(), 0.3, 'triangle', 0.5, 1200); },
      blink() { unlock(); if (!ac) return; tone(900, t(), 0.12, 'square', 0.3, 1800); },
      dash() { unlock(); if (!ac) return; noise(t(), 0.15, 0.4); },
      cast() { unlock(); if (!ac) return; tone(300, t(), 0.25, 'triangle', 0.3, 600); }
    };
  })();

  if (params.get('room')) {
    if (ui.name.value) ui.join.click();
    else { setMsg(ui.connectMsg, 'Pick a name, then press Join to enter room ' + ui.code.value + '.'); ui.name.focus(); }
  }
})();
