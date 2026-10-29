/* Fuse - browser client. The server owns the game (Lek.Server.Fuse); this file draws its snapshots, sends inputs,
   predicts the player's own movement so it feels immediate, and plays the music whose stop is the whole point. */
(() => {
  'use strict';

  const TILE = 16;
  const RADIUS = 6;
  const COLORS = ['#ff5d3b', '#3bd1ff', '#7cff5d', '#ffd23b', '#d45bff', '#ff8ac6', '#5d7cff', '#ffa43b', '#3bffc8', '#f2f2f2'];
  const $ = (id) => document.getElementById(id);
  const canvas = $('game');
  const ctx = canvas.getContext('2d');
  ctx.imageSmoothingEnabled = false;

  const params = new URLSearchParams(location.search);
  const ui = {
    lobby: $('lobby'), connectForm: $('connect-form'), roomForm: $('room-form'), name: $('name'), code: $('code'),
    create: $('create'), join: $('join'), connectMsg: $('connect-msg'), roomCode: $('room-code'), players: $('players'),
    addbot: $('addbot'), removebot: $('removebot'), start: $('start'), leave: $('leave'), share: $('share'),
    roomMsg: $('room-msg'), mapSelect: $('map'), optBombs: $('opt-bombs'), optMystery: $('opt-mystery'), optJoker: $('opt-joker'),
    hud: $('hud'), hudLeft: $('hud-left'), hudCenter: $('hud-center'), hudRight: $('hud-right'),
    banner: $('banner'), ping: $('ping'), fullscreen: $('fullscreen'), throwBtn: $('throw')
  };

  // ---------------------------------------------------------------- state
  let maps = [];           // {id, name, rows} from the server
  let mapId = null;        // the room's current map
  let map = null;          // its rows
  let me = null;           // my player id
  let state = null;        // last snapshot
  let shown = new Map();   // id -> {x, y, moving} display positions, interpolated (mine: predicted)
  let fx = [];             // explosions
  let bannerUntil = 0;
  let lastPhase = 'lobby';
  let floorCanvas = null;
  let lastFrame = 0;
  let ping = null;

  try { ui.name.value = params.get('name') || localStorage.getItem(LekSite.key('fuse-name')) || ''; } catch { ui.name.value = params.get('name') || ''; }
  if (params.get('room')) ui.code.value = params.get('room').toUpperCase();

  const mine = () => (state && me) ? state.players.find((p) => p.id === me) : null;
  const holdsBomb = (id) => !!(state && state.bombs && state.bombs.includes(id));

  // ---------------------------------------------------------------- connection
  const hub = new signalR.HubConnectionBuilder()
    .withUrl(LekSite.url('/hubs/fuse'))
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  hub.on('state', (s) => {
    state = s;
    if (s.map !== mapId) {
      const m = maps.find((x) => x.id === s.map);
      if (m) { mapId = s.map; map = m.rows; floorCanvas = null; shown.clear(); }
    }
    for (const e of s.events || []) onEvent(e);
    if (s.phase !== lastPhase) onPhase(s.phase, s);
    lastPhase = s.phase;
    renderLobby();
  });
  hub.onreconnecting(() => setMsg(ui.roomMsg, 'reconnecting...'));
  hub.onreconnected(() => { setMsg(ui.roomMsg, ''); me = null; state = null; showConnect('Connection was lost. Join again.'); });
  hub.onclose(() => showConnect('Disconnected.'));

  async function connect() {
    if (hub.state === signalR.HubConnectionState.Connected) return true;
    try { await hub.start(); return true; }
    catch (err) { setMsg(ui.connectMsg, 'Cannot reach the server.'); console.error(err); return false; }
  }

  function myName() {
    const n = ui.name.value.trim().slice(0, 14);
    try { localStorage.setItem(LekSite.key('fuse-name'), n); } catch { }
    return n;
  }

  async function enter(result) {
    if (!result || result.error) { setMsg(ui.connectMsg, result ? result.error : 'Something went wrong.'); return; }
    me = result.playerId;
    maps = result.maps || [];
    ui.mapSelect.innerHTML = '';
    for (const m of maps) {
      const o = document.createElement('option');
      o.value = m.id; o.textContent = m.name;
      ui.mapSelect.appendChild(o);
    }
    mapId = null;
    map = null;
    floorCanvas = null;
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
  ui.mapSelect.onchange = async () => { const r = await hub.invoke('SetMap', ui.mapSelect.value); if (r && r.error) setMsg(ui.roomMsg, r.error); };
  const sendOptions = async () => {
    const r = await hub.invoke('SetOptions', parseInt(ui.optBombs.value, 10), ui.optMystery.checked, ui.optJoker.checked);
    if (r && r.error) setMsg(ui.roomMsg, r.error);
  };
  ui.optBombs.onchange = sendOptions;
  ui.optMystery.onchange = sendOptions;
  ui.optJoker.onchange = sendOptions;
  ui.leave.onclick = async () => { await hub.invoke('Leave'); me = null; state = null; showConnect(''); };
  ui.share.onclick = async () => {
    // the site page, not this frame, so the link opens with the site's own chrome and sign-in
    const url = location.origin + LekSite.url('/fuse') + '?room=' + ui.roomCode.textContent;
    try { await navigator.clipboard.writeText(url); setMsg(ui.roomMsg, 'Link copied: ' + url); }
    catch { setMsg(ui.roomMsg, url); }
  };
  ui.fullscreen.onclick = async () => {
    try {
      if (document.fullscreenElement) { await document.exitFullscreen(); return; }
      await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
      if (screen.orientation && screen.orientation.lock) screen.orientation.lock('landscape').catch(() => { });
    } catch { setMsg(ui.roomMsg, 'Full screen is not available here.'); }
  };
  const doThrow = async () => { if (!me) return; const r = await hub.invoke('Throw'); if (r && r.error) banner(r.error, '', 900); };
  ui.throwBtn.onclick = doThrow;

  function showConnect(msg) {
    ui.lobby.classList.remove('hidden');
    ui.roomForm.classList.add('hidden');
    ui.connectForm.classList.remove('hidden');
    ui.hud.classList.add('hidden');
    ui.fullscreen.classList.add('hidden');
    ui.ping.classList.add('hidden');
    ui.throwBtn.classList.add('hidden');
    setMsg(ui.connectMsg, msg);
    const url = new URL(location.href);
    url.searchParams.delete('room');
    history.replaceState(null, '', url);
    audio.stopMusic();
  }

  function setMsg(el, text) { el.textContent = text || ''; }

  // ping: a round trip every two seconds, shown in the corner so network and code can be told apart
  setInterval(async () => {
    if (hub.state !== signalR.HubConnectionState.Connected) { ui.ping.textContent = ''; return; }
    const t0 = performance.now();
    try { await hub.invoke('Ping', t0); ping = Math.round(performance.now() - t0); ui.ping.textContent = ping + ' ms'; }
    catch { ui.ping.textContent = ''; }
  }, 2000);

  // ---------------------------------------------------------------- lobby rendering
  function renderLobby() {
    if (!state || !me) return;
    const my = mine();
    const owner = state.owner === me;
    document.querySelectorAll('.owner-only').forEach((el) => el.classList.toggle('off', !owner));
    const inLobby = state.phase === 'lobby';
    ui.lobby.classList.toggle('hidden', !inLobby);
    ui.hud.classList.toggle('hidden', inLobby);
    if (inLobby) {
      if (ui.mapSelect.value !== state.map) ui.mapSelect.value = state.map;
      ui.mapSelect.disabled = !owner;
      const o = state.options || { bombs: 1, mystery: false, joker: false };
      if (ui.optBombs.value !== String(o.bombs)) ui.optBombs.value = String(o.bombs);
      ui.optMystery.checked = !!o.mystery;
      ui.optJoker.checked = !!o.joker;
      ui.optBombs.disabled = ui.optMystery.disabled = ui.optJoker.disabled = !owner;
      ui.players.innerHTML = '';
      for (const p of state.players) {
        const li = document.createElement('li');
        const sw = document.createElement('span');
        sw.className = 'swatch';
        sw.style.background = COLORS[p.color % COLORS.length];
        li.appendChild(sw);
        const name = document.createElement('span');
        name.textContent = p.name + (p.bot ? ' (bot)' : '') + (p.id === state.owner ? ' *' : '');
        if (p.id === me) name.className = 'you';
        li.appendChild(name);
        const wins = document.createElement('span');
        wins.className = 'wins';
        // an account shows its all-time wins; a guest the wins in this room
        const n = p.tw >= 0 ? p.tw : p.wins;
        wins.textContent = n ? n + ' win' + (n > 1 ? 's' : '') + (p.tw >= 0 ? ' total' : '') : (p.tw >= 0 ? 'account' : '');
        li.appendChild(wins);
        ui.players.appendChild(li);
      }
      ui.start.textContent = state.players.length < 3 ? 'Start (bots fill to 3)' : 'Start';
      if (!owner) setMsg(ui.roomMsg, 'Waiting for ' + (state.players.find((p) => p.id === state.owner) || {}).name + ' to start...');
      else setMsg(ui.roomMsg, '');
      ui.throwBtn.classList.add('hidden');
    } else {
      const alive = state.players.filter((p) => p.inMatch && p.alive).length;
      ui.hudLeft.textContent = 'ROUND ' + state.round + '  ' + alive + ' LEFT' + (state.joker ? '  JOKER: ' + ((state.players.find((p) => p.id === state.joker) || {}).name || '?').toUpperCase() : '');
      ui.hudRight.textContent = 'ROOM ' + state.code;
      if (my && !my.inMatch) ui.hudCenter.textContent = 'WATCHING - NEXT MATCH SOON';
      else if (my && !my.alive) ui.hudCenter.textContent = 'YOU ARE OUT';
      else if (holdsBomb(me)) ui.hudCenter.textContent = (my && my.arm ? 'THROW IT! (SPACE)' : state.joker === me ? 'YOU HAVE THE BOMB - GET CLOSE TO SOMEONE!' : 'YOU HAVE THE BOMB!') + (my && my.sp ? ' (FAST)' : my && my.sl ? ' (SLOW)' : '');
      else if (my && my.sh) ui.hudCenter.textContent = 'SHIELD ' + Math.ceil(my.sh) + 's';
      else if (my && my.sp) ui.hudCenter.textContent = 'SPEED ' + Math.ceil(my.sp) + 's';
      else if (my && my.sl) ui.hudCenter.textContent = 'SLOWED ' + Math.ceil(my.sl) + 's';
      else if (state.joker === me) ui.hudCenter.textContent = 'YOU ARE THE JOKER: EXPLODE NEXT TO SOMEONE';
      else ui.hudCenter.textContent = '';
      ui.throwBtn.classList.toggle('hidden', !(my && my.arm && holdsBomb(me)));
    }
  }

  function banner(text, sub, ms) {
    ui.banner.innerHTML = '';
    ui.banner.appendChild(document.createTextNode(text));
    if (sub) { const s = document.createElement('small'); s.textContent = sub; ui.banner.appendChild(s); }
    ui.banner.classList.remove('hidden');
    bannerUntil = performance.now() + ms;
  }

  const nameOf = (id) => ((state && state.players.find((p) => p.id === id)) || {}).name || '?';

  function onPhase(phase, s) {
    if (phase === 'countdown') {
      audio.stopMusic();
      const my = s.players.find((p) => p.id === me);
      const jokerNote = s.joker ? (s.joker === me ? ' You are the Joker.' : ' Joker: ' + nameOf(s.joker) + '.') : '';
      banner('ROUND ' + s.round, my && my.inMatch && my.alive ? ((s.bombs && s.bombs.includes(me)) ? 'You start with the bomb. Run!' : 'Get ready') + jokerNote : 'Watching', 3000);
    } else if (phase === 'over') {
      const w = s.players.find((p) => p.id === s.winner);
      banner(w ? (w.id === me ? 'YOU WIN!' : w.name.toUpperCase() + ' WINS') : 'NOBODY WINS', 'back to the lobby in a moment', 6000);
    }
  }

  function onEvent(e) {
    if (e.kind === 'go') { audio.startMusic(); banner('GO!', '', 700); }
    else if (e.kind === 'pass') { audio.blip(e.b === me ? 1 : 0); }
    else if (e.kind === 'throw') { audio.whoosh(); }
    else if (e.kind === 'joker') { if (e.a === me) banner('YOU ARE THE JOKER', 'Blow up next to someone and you win the match', 3500); }
    else if (e.kind === 'pickup') {
      audio.pickup(e.b);
      if (e.a === me) banner(e.b === 'speed' ? 'SPEED!' : e.b === 'shield' ? 'SHIELD!' : 'SLOWED...', '', 900);
    }
    else if (e.kind === 'boom' || e.kind === 'blast') {
      audio.stopMusic();
      audio.explode();
      const v = shown.get(e.a) || (state && state.players.find((p) => p.id === e.a));
      if (v) fx.push({ x: v.x, y: v.y, t: performance.now(), big: e.kind === 'blast' });
      if (e.kind === 'blast') {
        const n = parseInt(e.b || '0', 10);
        banner('KABOOM!', nameOf(e.a) + (n > 0 ? ' took ' + n + ' with ' + (e.a === me ? 'you' : 'him') + '!' : ' blew up alone.'), 2200);
      } else {
        banner('BOOM!', e.a === me ? 'You blew up.' : nameOf(e.a) + ' blew up.', 2200);
      }
    }
  }

  // ---------------------------------------------------------------- input
  const keys = new Set();
  let lastSent = { dx: 0, dy: 0 };
  let touchOrigin = null;
  let touchVec = { dx: 0, dy: 0 };

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

  function sendInput() {
    if (!me || hub.state !== signalR.HubConnectionState.Connected) return;
    const v = currentInput();
    if (Math.abs(v.dx - lastSent.dx) < 0.02 && Math.abs(v.dy - lastSent.dy) < 0.02) return;
    lastSent = v;
    hub.send('Input', v.dx, v.dy).catch(() => { });
  }

  const onKeyDown = (e) => {
    if (e.target && (e.target.tagName === 'INPUT' || e.target.tagName === 'SELECT' || e.target.tagName === 'TEXTAREA')) return;
    if (['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', ' '].includes(e.key)) e.preventDefault();
    if (e.key === ' ' && !e.repeat) { doThrow(); return; }
    keys.add(e.key);
    audio.unlock();
    sendInput();
  };
  const onKeyUp = (e) => { keys.delete(e.key); sendInput(); };
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('keyup', onKeyUp);
  window.addEventListener('blur', () => { keys.clear(); touchVec = { dx: 0, dy: 0 }; sendInput(); });
  // After alt-tab the browser hands focus back to the SITE page, not to this frame, and keys went nowhere.
  // The frame is same-origin, so it listens on the parent page too and takes focus back on any click.
  try {
    if (window.parent !== window) {
      window.parent.addEventListener('keydown', onKeyDown);
      window.parent.addEventListener('keyup', onKeyUp);
      window.parent.addEventListener('focus', () => { if (me) window.focus(); });
    }
  } catch { /* cross-origin host: nothing to do */ }
  canvas.addEventListener('pointerdown', () => window.focus());

  canvas.addEventListener('pointerdown', (e) => {
    if (e.pointerType === 'mouse' && e.button !== 0) return;
    canvas.setPointerCapture(e.pointerId);
    touchOrigin = { x: e.clientX, y: e.clientY, id: e.pointerId };
    audio.unlock();
  });
  canvas.addEventListener('pointermove', (e) => {
    if (!touchOrigin || e.pointerId !== touchOrigin.id) return;
    let dx = e.clientX - touchOrigin.x, dy = e.clientY - touchOrigin.y;
    const len = Math.hypot(dx, dy);
    const dead = 8, full = 40;
    if (len < dead) { touchVec = { dx: 0, dy: 0 }; }
    else { const k = Math.min(1, (len - dead) / (full - dead)); touchVec = { dx: dx / len * k, dy: dy / len * k }; }
    sendInput();
  });
  const endTouch = (e) => { if (touchOrigin && e.pointerId === touchOrigin.id) { touchOrigin = null; touchVec = { dx: 0, dy: 0 }; sendInput(); } };
  canvas.addEventListener('pointerup', endTouch);
  canvas.addEventListener('pointercancel', endTouch);
  setInterval(() => { lastSent = { dx: NaN, dy: NaN }; sendInput(); }, 1000); // heartbeat, in case a packet was lost

  // ---------------------------------------------------------------- prediction
  // My own character moves the moment I press a key, with the same walls and speed the server uses; the server's
  // position pulls the prediction back gently, or snaps it when they disagree by a lot (a pass changed my speed, a
  // round reset moved me). Everyone else is interpolated toward their last known position.
  function blocked(x, y, r) {
    if (!map) return false;
    const x0 = Math.floor((x - r) / TILE), x1 = Math.floor((x + r) / TILE);
    const y0 = Math.floor((y - r) / TILE), y1 = Math.floor((y + r) / TILE);
    for (let ty = y0; ty <= y1; ty++) {
      for (let tx = x0; tx <= x1; tx++) {
        if (tx < 0 || ty < 0 || ty >= map.length || tx >= map[0].length || map[ty][tx] === '#') return true;
      }
    }
    return false;
  }

  function predictMine(d, p, dt) {
    const canMove = state.phase === 'lobby' || (state.phase === 'playing' && p.inMatch && p.alive);
    const v = canMove ? currentInput() : { dx: 0, dy: 0 };
    const speed = p.speed || 92;
    if (v.dx || v.dy) {
      const nx = d.x + v.dx * speed * dt, ny = d.y + v.dy * speed * dt;
      if (!blocked(nx, d.y, RADIUS)) d.x = nx;
      if (!blocked(d.x, ny, RADIUS)) d.y = ny;
      d.moving = true;
      d.f = Math.abs(v.dx) > Math.abs(v.dy) ? (v.dx > 0 ? 2 : 1) : (v.dy > 0 ? 0 : 3);
    } else {
      d.moving = false;
    }
    // reconcile with the server: a small pull always, a snap when far off
    const ex = p.x - d.x, ey = p.y - d.y;
    const err = Math.hypot(ex, ey);
    if (err > 28) { d.x = p.x; d.y = p.y; }
    else if (err > 0.5) { const k = Math.min(1, dt * (v.dx || v.dy ? 2.5 : 8)); d.x += ex * k; d.y += ey * k; }
  }

  // ---------------------------------------------------------------- drawing
  function drawFloor() {
    if (floorCanvas || !map) return;
    floorCanvas = document.createElement('canvas');
    floorCanvas.width = canvas.width;
    floorCanvas.height = canvas.height;
    const g = floorCanvas.getContext('2d');
    const H = map.length, W = map[0].length;
    for (let y = 0; y < H; y++) {
      for (let x = 0; x < W; x++) {
        const c = map[y][x];
        const px = x * TILE, py = y * TILE;
        if (c === '#') {
          const below = y + 1 < H && map[y + 1][x] !== '#';
          g.fillStyle = '#2b2440';
          g.fillRect(px, py, TILE, TILE);
          g.fillStyle = '#4a3f6b';
          g.fillRect(px, py, TILE, 4);
          if (below) { g.fillStyle = '#17132a'; g.fillRect(px, py + TILE - 3, TILE, 3); }
          g.fillStyle = '#3a3158';
          g.fillRect(px + 2, py + 6, 5, 3); g.fillRect(px + 9, py + 6, 5, 3); g.fillRect(px + 5, py + 11, 6, 3);
        } else {
          const top = y < 9;
          const left = x < 15, mid = x < 20;
          let a, b;
          if (top && left) { a = '#8c6a4a'; b = '#946f4e'; }
          else if (top) { a = '#6f7f8f'; b = '#75859a'; }
          else if (x < 8) { a = '#5b7a4a'; b = '#60804e'; }
          else if (!mid) { a = '#7a5b6e'; b = '#805f73'; }
          else { a = '#8a7a5a'; b = '#907f5f'; }
          g.fillStyle = ((x + y) & 1) ? a : b;
          g.fillRect(px, py, TILE, TILE);
          g.fillStyle = 'rgba(0,0,0,.08)';
          g.fillRect(px, py, TILE, 1); g.fillRect(px, py, 1, TILE);
        }
      }
    }
  }

  function drawBomb(x, y, now, lit) {
    ctx.fillStyle = '#111';
    ctx.beginPath(); ctx.arc(x, y, 5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#444';
    ctx.fillRect(x - 1, y - 6, 2, 3);
    if (lit) {
      const sp = Math.floor(now / 80) % 2;
      ctx.fillStyle = sp ? '#ffd23b' : '#ff5d3b';
      ctx.fillRect(x - 2 + (sp ? 1 : -1), y - 9, 3, 3);
    }
  }

  function drawPlayer(p, pos, now) {
    const col = COLORS[p.color % COLORS.length];
    const x = Math.round(pos.x), y = Math.round(pos.y);
    const f = p.id === me && pos.f !== undefined ? pos.f : p.f;
    const walking = pos.moving;
    const step = walking ? Math.floor(now / 110) % 2 : 0;
    const isBomb = holdsBomb(p.id);
    const isJoker = state.joker === p.id;
    ctx.fillStyle = 'rgba(0,0,0,.35)';
    ctx.fillRect(x - 5, y + 4, 10, 3);
    ctx.fillStyle = '#1a1626';
    ctx.fillRect(x - 4, y + 1 + (step ? 1 : 0), 3, 4);
    ctx.fillRect(x + 1, y + 1 + (step ? 0 : 1), 3, 4);
    ctx.fillStyle = col;
    ctx.fillRect(x - 5, y - 5, 10, 7);
    if (isJoker) { ctx.fillStyle = '#111'; ctx.fillRect(x - 5, y - 5, 10, 2); ctx.fillRect(x - 1, y - 5, 2, 7); }
    ctx.fillStyle = '#f1c9a5';
    if (f === 1) ctx.fillRect(x - 7, y - 3, 2, 4); else if (f === 2) ctx.fillRect(x + 5, y - 3, 2, 4); else { ctx.fillRect(x - 7, y - 3, 2, 4); ctx.fillRect(x + 5, y - 3, 2, 4); }
    ctx.fillStyle = isJoker ? '#e9e2f0' : '#f1c9a5';
    ctx.fillRect(x - 4, y - 12, 8, 7);
    ctx.fillStyle = isJoker ? '#2e8b57' : '#2b1d14';
    ctx.fillRect(x - 4, y - 12, 8, 2);
    if (f !== 3) {
      ctx.fillStyle = isJoker ? '#c00' : '#111';
      if (f === 0) { ctx.fillRect(x - 3, y - 9, 2, 2); ctx.fillRect(x + 1, y - 9, 2, 2); }
      else if (f === 1) ctx.fillRect(x - 3, y - 9, 2, 2);
      else ctx.fillRect(x + 1, y - 9, 2, 2);
    }
    if (isJoker && f === 0) { ctx.fillStyle = '#c00'; ctx.fillRect(x - 2, y - 6, 4, 1); }
    if (isBomb) {
      drawBomb(x, y - 18, now, true);
      ctx.strokeStyle = 'rgba(255,93,59,' + (0.5 + 0.5 * Math.sin(now / 120)) + ')';
      ctx.lineWidth = 1;
      ctx.beginPath(); ctx.arc(x, y + 4, 9, 0, Math.PI * 2); ctx.stroke();
      if (isJoker) {
        // the blast he is hoping for
        ctx.strokeStyle = 'rgba(255,60,60,' + (0.25 + 0.2 * Math.sin(now / 200)) + ')';
        ctx.setLineDash([3, 3]);
        ctx.beginPath(); ctx.arc(x, y, 44, 0, Math.PI * 2); ctx.stroke();
        ctx.setLineDash([]);
      }
      if (p.arm) {
        ctx.fillStyle = '#ffd23b';
        ctx.font = '6px "Press Start 2P", monospace';
        ctx.textAlign = 'center';
        ctx.fillText('THROW', x, y - 30);
      }
    }
    ctx.font = '6px "Press Start 2P", monospace';
    ctx.textAlign = 'center';
    ctx.fillStyle = 'rgba(0,0,0,.7)';
    ctx.fillText(p.name, x + 1, y - 15 - (isBomb ? 14 : 0) + 1);
    ctx.fillStyle = p.id === me ? '#ffd23b' : isJoker ? '#ff8a70' : '#fff';
    ctx.fillText(p.name, x, y - 15 - (isBomb ? 14 : 0));
  }

  function drawItem(it, now) {
    const x = Math.round(it.x), y = Math.round(it.y + Math.sin(now / 250 + it.id) * 1.5);
    const col = it.kind === 'speed' ? '#ffd23b' : it.kind === 'shield' ? '#3bd1ff' : it.kind === 'slow' ? '#d45bff' : '#cfcfcf';
    ctx.fillStyle = 'rgba(0,0,0,.35)';
    ctx.fillRect(x - 6, it.y + 5, 12, 3);
    ctx.fillStyle = '#5a3d1e';
    ctx.fillRect(x - 6, y - 6, 12, 12);
    ctx.fillStyle = '#8a5f30';
    ctx.fillRect(x - 5, y - 5, 10, 10);
    ctx.fillStyle = col;
    ctx.fillRect(x - 5, y - 1, 10, 2); ctx.fillRect(x - 1, y - 5, 2, 10);
    ctx.fillStyle = '#fff';
    ctx.fillRect(x - 4, y - 4, 2, 2);
    ctx.font = '6px "Press Start 2P", monospace';
    ctx.textAlign = 'center';
    ctx.fillStyle = col;
    ctx.fillText(it.kind === 'speed' ? '>>' : it.kind === 'shield' ? '()' : it.kind === 'slow' ? 'zz' : '?', x, y - 9);
  }

  function drawEffects(p, pos, now) {
    const x = Math.round(pos.x), y = Math.round(pos.y);
    if (p.sh) {
      ctx.strokeStyle = 'rgba(59,209,255,' + (0.6 + 0.4 * Math.sin(now / 90)) + ')';
      ctx.lineWidth = 2;
      ctx.beginPath(); ctx.arc(x, y - 3, 11, 0, Math.PI * 2); ctx.stroke();
    }
    if (p.sp) {
      ctx.fillStyle = 'rgba(255,210,59,.8)';
      const k = Math.floor(now / 60) % 3;
      ctx.fillRect(x - 11 - k, y - 2, 4, 1); ctx.fillRect(x - 10 - k, y + 2, 3, 1); ctx.fillRect(x + 7 + k, y, 4, 1);
    }
    if (p.sl) {
      ctx.fillStyle = 'rgba(212,91,255,.8)';
      ctx.font = '6px "Press Start 2P", monospace';
      ctx.textAlign = 'center';
      ctx.fillText('z', x + 8, y - 12 - (Math.floor(now / 300) % 3));
    }
  }

  function drawGrave(p, pos) {
    const x = Math.round(pos.x), y = Math.round(pos.y);
    ctx.fillStyle = 'rgba(0,0,0,.45)';
    ctx.fillRect(x - 7, y - 2, 14, 7);
    ctx.fillStyle = '#333';
    ctx.fillRect(x - 6, y - 1, 3, 2); ctx.fillRect(x + 1, y + 1, 4, 2); ctx.fillRect(x - 2, y - 3, 2, 2);
  }

  function frame(now) {
    requestAnimationFrame(frame);
    const dt = Math.min(0.05, (now - lastFrame) / 1000 || 0);
    lastFrame = now;
    ctx.fillStyle = '#0f0d1a';
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    if (!map || !state) { drawIdle(now); return; }
    drawFloor();
    ctx.drawImage(floorCanvas, 0, 0);

    // my character is predicted, everyone else interpolated toward the server positions
    const alive = new Set();
    for (const p of state.players) {
      alive.add(p.id);
      let d = shown.get(p.id);
      if (!d) { d = { x: p.x, y: p.y, moving: false }; shown.set(p.id, d); }
      if (p.id === me) { predictMine(d, p, dt); continue; }
      const dx = p.x - d.x, dy = p.y - d.y;
      const dist = Math.hypot(dx, dy);
      if (dist > 40) { d.x = p.x; d.y = p.y; }
      else { d.x += dx * 0.35; d.y += dy * 0.35; }
      d.moving = dist > 0.6;
    }
    for (const id of [...shown.keys()]) if (!alive.has(id)) shown.delete(id);

    for (const it of state.items || []) drawItem(it, now);
    for (const b of state.loose || []) {
      ctx.fillStyle = 'rgba(0,0,0,.35)';
      ctx.fillRect(b.x - 5, b.y + 4, 10, 3);
      drawBomb(b.x, b.y - (b.air ? 8 : 0), now, true);
    }

    const order = [...state.players].sort((a, b) => a.y - b.y);
    for (const p of order) {
      const pos = shown.get(p.id);
      if (!p.inMatch && state.phase !== 'lobby') continue;      // spectators are not on the floor
      if (!p.alive) drawGrave(p, pos); else { drawEffects(p, pos, now); drawPlayer(p, pos, now); }
    }

    fx = fx.filter((e) => now - e.t < 700);
    for (const e of fx) {
      const k = (now - e.t) / 700;
      const size = e.big ? 2.2 : 1;
      ctx.strokeStyle = 'rgba(255,210,59,' + (1 - k) + ')';
      ctx.lineWidth = 3;
      ctx.beginPath(); ctx.arc(e.x, e.y, (6 + k * 40) * size, 0, Math.PI * 2); ctx.stroke();
      ctx.fillStyle = 'rgba(255,93,59,' + (1 - k) * 0.6 + ')';
      ctx.beginPath(); ctx.arc(e.x, e.y, (4 + k * 22) * size, 0, Math.PI * 2); ctx.fill();
    }

    if (state.phase === 'countdown') {
      ctx.font = '28px "Press Start 2P", monospace';
      ctx.textAlign = 'center';
      const n = Math.ceil(state.countdown);
      ctx.fillStyle = '#000'; ctx.fillText(n, canvas.width / 2 + 3, canvas.height / 2 + 13);
      ctx.fillStyle = '#ffd23b'; ctx.fillText(n, canvas.width / 2, canvas.height / 2 + 10);
    }
    if (state.phase === 'playing') {
      const bars = 5;
      for (let i = 0; i < bars; i++) {
        const h = 3 + Math.abs(Math.sin(now / 90 + i)) * 10;
        ctx.fillStyle = i % 2 ? '#ffd23b' : '#ff5d3b';
        ctx.fillRect(canvas.width - 30 + i * 5, canvas.height - 6 - h, 3, h);
      }
    }
    if (bannerUntil && now > bannerUntil) { ui.banner.classList.add('hidden'); bannerUntil = 0; }
  }

  function drawIdle(now) {
    const x = canvas.width / 2 + Math.sin(now / 900) * 120;
    const y = canvas.height / 2 + 60 + Math.abs(Math.sin(now / 300)) * -30;
    drawBomb(x, y, now, true);
  }
  requestAnimationFrame(frame);

  // ---------------------------------------------------------------- audio: a chiptune that stops
  const audio = (() => {
    let ac = null, master = null, playing = false, nextNote = 0, noteIndex = 0, timer = null;
    const bpm = 150;
    const beat = 60 / bpm;
    const melody = [76, 0, 76, 0, 79, 0, 76, 74, 72, 0, 74, 76, 0, 74, 72, 69, 71, 0, 71, 0, 74, 0, 71, 69, 67, 0, 69, 71, 0, 72, 74, 76];
    const bass = [40, 40, 47, 47, 45, 45, 52, 52, 43, 43, 50, 50, 41, 41, 48, 48];
    const hz = (m) => 440 * Math.pow(2, (m - 69) / 12);
    function unlock() {
      if (!ac) {
        try { ac = new (window.AudioContext || window.webkitAudioContext)(); master = ac.createGain(); master.gain.value = 0.18; master.connect(ac.destination); } catch { return; }
      }
      if (ac.state === 'suspended') ac.resume();
    }
    function tone(freq, start, dur, type, gain) {
      const o = ac.createOscillator(); const g = ac.createGain();
      o.type = type; o.frequency.value = freq;
      g.gain.setValueAtTime(gain, start); g.gain.exponentialRampToValueAtTime(0.001, start + dur);
      o.connect(g); g.connect(master); o.start(start); o.stop(start + dur + 0.02);
    }
    function schedule() {
      if (!playing) return;
      while (nextNote < ac.currentTime + 0.25) {
        const i = noteIndex % melody.length;
        const m = melody[i];
        if (m) tone(hz(m), nextNote, beat * 0.45, 'square', 0.5);
        const b = bass[Math.floor(i / 2) % bass.length];
        if (i % 2 === 0) tone(hz(b), nextNote, beat * 0.4, 'triangle', 0.7);
        if (i % 4 === 0) noise(nextNote, 0.05, 0.25);
        if (i % 4 === 2) noise(nextNote, 0.03, 0.12);
        nextNote += beat / 2;
        noteIndex++;
      }
      timer = setTimeout(schedule, 60);
    }
    function noise(start, dur, gain) {
      const len = Math.floor(ac.sampleRate * dur);
      const buf = ac.createBuffer(1, len, ac.sampleRate);
      const d = buf.getChannelData(0);
      for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
      const s = ac.createBufferSource(); s.buffer = buf;
      const g = ac.createGain(); g.gain.value = gain;
      s.connect(g); g.connect(master); s.start(start);
    }
    return {
      unlock,
      startMusic() { unlock(); if (!ac || playing) return; playing = true; nextNote = ac.currentTime + 0.05; noteIndex = 0; schedule(); },
      stopMusic() { playing = false; if (timer) clearTimeout(timer); timer = null; },
      pickup(kind) {
        unlock(); if (!ac) return; const t = ac.currentTime;
        if (kind === 'speed') { tone(660, t, 0.06, 'square', 0.4); tone(880, t + 0.06, 0.06, 'square', 0.4); tone(1320, t + 0.12, 0.1, 'square', 0.4); }
        else if (kind === 'shield') { tone(523, t, 0.12, 'triangle', 0.6); tone(784, t + 0.1, 0.18, 'triangle', 0.6); }
        else { tone(300, t, 0.15, 'sawtooth', 0.4); tone(200, t + 0.15, 0.25, 'sawtooth', 0.4); }
      },
      whoosh() { unlock(); if (!ac) return; noise(ac.currentTime, 0.25, 0.5); },
      blip(up) { unlock(); if (!ac) return; const t = ac.currentTime; tone(up ? 880 : 440, t, 0.08, 'square', 0.4); tone(up ? 1320 : 330, t + 0.08, 0.1, 'square', 0.3); },
      explode() { unlock(); if (!ac) return; const t = ac.currentTime; noise(t, 0.6, 1.2); tone(60, t, 0.5, 'sawtooth', 0.8); tone(45, t + 0.1, 0.6, 'sawtooth', 0.6); }
    };
  })();

  // a shared link: join at once when a name is known, otherwise ask for one first
  if (params.get('room')) {
    if (ui.name.value) ui.join.click();
    else { setMsg(ui.connectMsg, 'Pick a name, then press Join to enter room ' + ui.code.value + '.'); ui.name.focus(); }
  }
})();
