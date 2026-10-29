/* Relic Rush - browser client. The server owns the match (Lek.Server.RelicRush); this file sends inputs at 30 Hz
   with sequence numbers, predicts the player's own runner with physics.js, interpolates everyone else 150 ms in the
   past, and draws the lit maze with art.js. Each team receives only its own shrine (and the other one when revealed). */
(() => {
  'use strict';
  const A = window.RelicArt, P = window.RelicPhysics, S = window.ElementalsAudio;
  const T = 16, COLS = 40, ROWS = 22, W = 640, H = 352;
  const $ = (id) => document.getElementById(id);
  const canvas = $('game');
  const g = canvas.getContext('2d');
  g.imageSmoothingEnabled = false;
  const params = new URLSearchParams(location.search);
  const store = {
    get(k, d) { try { const v = localStorage.getItem(LekSite.key(k)); return v == null ? d : v; } catch { return d; } },
    set(k, v) { try { localStorage.setItem(LekSite.key(k), v); } catch { } }
  };
  const FIELDS = ['id', 'x', 'y', 'dashT', 'dashX', 'dashY', 'dashCd', 'faceX', 'faceY', 'speed', 'prevDash', 'seq', 'aim', 'im', 'stealCd', 'immune'];

  let me = null, state = null, snaps = [], clockOffset = null, lastPhase = 'lobby', lobbySig = '';
  const roster = new Map();
  let cells = null, mazeVersion = -1, maze = null, light = A.lighting(W, H);
  let seq = 0, pending = [], myBody = null, myPrev = null, stepAt = 0, offset = { x: 0, y: 0 };
  let particles = [], texts = [], arcs = [], shake = 0, banner = null;
  const trails = new Map();

  // ------------------------------------------------------------------ input
  const held = new Set();
  const virtual = new Set();
  const KEYS = { up: ['KeyW', 'ArrowUp'], down: ['KeyS', 'ArrowDown'], left: ['KeyA', 'ArrowLeft'], right: ['KeyD', 'ArrowRight'], dash: ['ShiftLeft', 'ShiftRight', 'KeyK'], steal: ['Space', 'KeyJ'], pass: ['KeyE', 'KeyL'] };
  const isDown = (a) => virtual.has(a) || KEYS[a].some((k) => held.has(k));
  let mouse = { x: W / 2, y: H / 2, movedAt: -1e9 };
  function onKeyDown(e) {
    if (e.target && e.target.tagName === 'INPUT') return;
    const action = Object.keys(KEYS).find((a) => KEYS[a].includes(e.code));
    if (!action) return;
    e.preventDefault();
    if (held.has(e.code)) return;
    held.add(e.code);
    press(action);
  }
  function press(action) {
    if (!state || state.ph !== 'playing') return;
    if (action === 'steal') hub.send('Steal').catch(() => { });
    if (action === 'pass') hub.send('Pass', myAim()).catch(() => { });
  }
  window.addEventListener('keydown', onKeyDown);
  window.addEventListener('keyup', (e) => held.delete(e.code));
  window.addEventListener('blur', () => held.clear());
  try { window.parent.addEventListener('keydown', onKeyDown); window.parent.addEventListener('keyup', (e) => held.delete(e.code)); } catch { }
  canvas.addEventListener('mousemove', (e) => {
    const r = canvas.getBoundingClientRect();
    mouse.x = (e.clientX - r.left) / r.width * W;
    mouse.y = (e.clientY - r.top) / r.height * H;
    mouse.movedAt = performance.now();
  });
  canvas.addEventListener('mousedown', (e) => { S.unlock(); if (e.button === 0) press('pass'); if (e.button === 2) press('steal'); });
  canvas.addEventListener('contextmenu', (e) => e.preventDefault());

  function myAim() {
    if (!myBody) return 0;
    if (performance.now() - mouse.movedAt < 2000) return Math.atan2(mouse.y - (myBody.y - 6), mouse.x - myBody.x);
    return Math.atan2(myBody.faceY, myBody.faceX);
  }

  // ------------------------------------------------------------------ connection
  const hub = new signalR.HubConnectionBuilder().withUrl(LekSite.url('/hubs/relicrush')).withAutomaticReconnect().configureLogging(signalR.LogLevel.Warning).build();
  hub.on('state', onState);
  hub.onreconnected(() => { me = null; state = null; showMenu('Connection was lost. Join again.'); });
  hub.onclose(() => showMenu('Disconnected.'));
  async function connect() {
    if (hub.state === signalR.HubConnectionState.Connected) return true;
    try { await hub.start(); return true; } catch (err) { setMsg('menu-msg', 'Cannot reach the server.'); console.error(err); return false; }
  }
  function myName() { const n = $('name').value.trim().slice(0, 14); store.set('relicrush-name', n); return n; }
  $('name').value = params.get('name') || store.get('relicrush-name', '');
  if (params.get('room')) $('code').value = params.get('room').toUpperCase();
  function setMsg(id, text) { $(id).textContent = text || ''; }

  async function enter(result) {
    if (!result || result.error) { setMsg('menu-msg', result ? result.error : 'Something went wrong.'); return; }
    me = result.playerId;
    $('room-code').textContent = result.code;
    $('menu').classList.add('hidden');
    $('lobby').classList.remove('hidden');
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
  $('start').onclick = async () => { S.unlock(); const r = await hub.invoke('Start'); if (r && r.error) setMsg('lobby-msg', r.error); };
  $('leave').onclick = async () => { await hub.invoke('Leave'); me = null; state = null; showMenu(''); };
  $('share').onclick = async () => {
    const url = location.origin + LekSite.url('/relic-rush') + '?room=' + $('room-code').textContent;
    try { await navigator.clipboard.writeText(url); setMsg('lobby-msg', 'Invite link copied.'); } catch { setMsg('lobby-msg', url); }
  };
  document.querySelectorAll('#modes button').forEach((b) => { b.onclick = async () => { const r = await hub.invoke('SetMode', Number(b.dataset.mode)); if (r && r.error) setMsg('lobby-msg', r.error); }; });
  document.querySelectorAll('[data-team]').forEach((b) => { b.onclick = () => hub.invoke('SetTeam', Number(b.dataset.team)); });
  function showMenu(msg) {
    $('lobby').classList.add('hidden');
    $('menu').classList.remove('hidden');
    setMsg('menu-msg', msg);
    myBody = null; pending = [];
  }
  setInterval(async () => {
    if (hub.state !== signalR.HubConnectionState.Connected) { $('ping').textContent = ''; return; }
    const t0 = performance.now();
    try { await hub.invoke('Ping', t0); $('ping').textContent = Math.round(performance.now() - t0) + ' ms'; } catch { $('ping').textContent = ''; }
  }, 2000);
  $('btn-full').onclick = async () => {
    try { if (document.fullscreenElement) await document.exitFullscreen(); else { await document.documentElement.requestFullscreen({ navigationUI: 'hide' }); if (screen.orientation && screen.orientation.lock) screen.orientation.lock('landscape').catch(() => { }); } } catch { }
  };
  const soundIcon = () => { $('btn-sound').innerHTML = S.isMuted() ? '&#128263;' : '&#128266;'; };
  soundIcon();
  $('btn-sound').onclick = () => { S.unlock(); S.setMuted(!S.isMuted()); soundIcon(); };

  // ------------------------------------------------------------------ snapshots
  function onState(s) {
    const arrival = performance.now() / 1000;
    const off = s.now - arrival;
    if (clockOffset == null || off > clockOffset || Math.abs(off - clockOffset) > 1) clockOffset = off; else clockOffset -= 0.0005;
    if (s.ro) { roster.clear(); for (const r of s.ro) roster.set(r.id, r); }
    if (s.mz && s.mv !== mazeVersion) { mazeVersion = s.mv; cells = s.mz.join('').split(''); maze = A.renderMaze(cells); }
    s.ps = s.ps.map((arr) => { const o = {}; FIELDS.forEach((k, i) => { o[k] = arr[i]; }); o.im = !!o.im; o.immune = !!o.immune; return Object.assign(o, roster.get(o.id) || {}); }).filter((o) => o.n !== undefined);
    state = s;
    snaps.push(s);
    while (snaps.length > 2 && snaps[0].now < s.now - 1.5) snaps.shift();
    for (const e of s.ev || []) onEvent(e, s);
    if (s.ph !== lastPhase) onPhase(s.ph, s);
    lastPhase = s.ph;
    reconcile(s);
    renderLobby();
  }

  const wall = (x, y) => !cells || x < 0 || y < 0 || x >= COLS || y >= ROWS || cells[y * COLS + x] === '#';
  const maskFor = (ph) => (ph === 'playing' ? ~0 : 0);

  function reconcile(s) {
    const p = s.ps.find((q) => q.id === me);
    if (!p || !p.im || s.ph === 'lobby' || s.ph === 'over' || !cells) { myBody = null; pending = []; return; }
    const before = myBody ? { x: myBody.x + offset.x, y: myBody.y + offset.y } : null;
    myBody = P.body({ x: p.x, y: p.y, dashT: p.dashT, dashX: p.dashX, dashY: p.dashY, dashCd: p.dashCd, faceX: p.faceX, faceY: p.faceY, speed: p.speed, prevDash: !!p.prevDash });
    pending = pending.filter((i) => i.seq > p.seq);
    for (const i of pending) P.step(myBody, i.bits & maskFor(s.ph), wall);
    if (before) {
      const dx = before.x - myBody.x, dy = before.y - myBody.y;
      if (Math.hypot(dx, dy) < 40) { offset.x = dx; offset.y = dy; } else { offset.x = 0; offset.y = 0; }
    }
    myPrev = { x: myBody.x, y: myBody.y };
  }

  function teamOf(id) { const r = roster.get(id); return r ? r.tm : 0; }
  function myTeam() { return teamOf(me); }

  function onPhase(ph, s) {
    if (ph === 'lobby') { $('lobby').classList.remove('hidden'); lobbySig = ''; banner = null; S.playMusic('menu'); particles = []; texts = []; arcs = []; }
    else { $('lobby').classList.add('hidden'); S.playMusic(ph === 'over' ? 'victory' : 'castle'); }
    if (ph === 'over') {
      const won = s.win === myTeam();
      banner = { text: s.win < 0 ? 'DRAW' : A.TEAMS[s.win].name.toUpperCase() + ' WINS', sub: s.sc[0] + ' - ' + s.sc[1], color: s.win < 0 ? '#ffffff' : A.TEAMS[s.win].color, until: Infinity };
      S.play(won ? 'win' : 'lose');
    }
  }

  function onEvent(e, s) {
    const who = (id) => s.ps.find((p) => p.id === id);
    const name = (id) => (roster.get(id) || {}).n || '?';
    switch (e.k) {
      case 'round':
        banner = { text: e.v === 1 ? 'FIND YOUR SHRINE' : 'ROUND ' + (['ONE', 'TWO', 'THREE', 'FOUR', 'FIVE', 'SIX'][e.v - 1] || e.v), sub: (e.a === me ? 'You have the relic!' : teamOf(e.a) === myTeam() ? name(e.a) + ' has the relic' : 'Steal the relic from ' + name(e.a)), color: '#ffd25a', until: performance.now() + 2600 };
        S.play('beep');
        break;
      case 'go': banner = { text: 'GO!', sub: '', color: '#ffffff', until: performance.now() + 800 }; S.play('go'); break;
      case 'pass': {
        const from = who(e.a), to = who(e.b);
        if (from && to) arcs.push({ x1: from.x, y1: from.y - 14, id: e.b, t: 0, life: 0.28 });
        S.play('shoot');
        break;
      }
      case 'steal': {
        const to = who(e.b);
        if (to) { burst(to.x, to.y - 12, '#ffd25a', 18, 110); texts.push({ x: to.x, y: to.y - 26, text: 'STOLEN!', color: A.TEAMS[teamOf(e.b)].color, t: 0, life: 1 }); }
        shake = Math.max(shake, e.b === me || e.a === me ? 5 : 2);
        S.play('hit');
        break;
      }
      case 'grab': if (e.a === me) S.play('swing'); break;
      case 'guard': burst(e.x, e.y - 10, '#fff3b0', 8, 60); break;
      case 'nopass': if (e.a === me) texts.push({ x: myBody ? myBody.x : 0, y: myBody ? myBody.y - 30 : 0, text: 'no teammate in sight', color: '#aaa', t: 0, life: 1 }); break;
      case 'score': {
        const team = e.v;
        burst(e.x, e.y, A.TEAMS[team].color, 50, 160);
        burst(e.x, e.y, '#ffd25a', 30, 120);
        shake = Math.max(shake, 7);
        banner = { text: A.TEAMS[team].name.toUpperCase() + ' SCORES', sub: name(e.a) + ' reached the shrine', color: A.TEAMS[team].color, until: performance.now() + 3200 };
        S.play(team === myTeam() ? 'win' : 'lose');
        break;
      }
    }
  }
  function burst(x, y, color, n, speed) {
    for (let i = 0; i < n; i++) {
      const a = Math.random() * Math.PI * 2, v = speed * (0.3 + Math.random() * 0.7);
      particles.push({ x, y, vx: Math.cos(a) * v, vy: Math.sin(a) * v, t: 0, life: 0.3 + Math.random() * 0.4, color });
    }
  }

  // ------------------------------------------------------------------ lobby
  function renderLobby() {
    if (!state || !me || state.ph !== 'lobby') return;
    const owner = state.owner === me;
    const sig = JSON.stringify([state.mode, owner, [...roster.values()].map((r) => [r.id, r.n, r.tm])]);
    if (sig === lobbySig) return;
    lobbySig = sig;
    document.querySelectorAll('#modes button').forEach((b) => { b.classList.toggle('on', Number(b.dataset.mode) === state.mode); b.disabled = !owner; });
    $('start').disabled = !owner;
    $('start').textContent = owner ? 'Start the match' : 'Waiting for the owner';
    for (const team of [0, 1]) {
      const ul = $('team' + team);
      ul.innerHTML = '';
      const list = [...roster.values()].filter((r) => r.tm === team);
      for (const r of list) {
        const li = document.createElement('li');
        if (r.id === me) li.className = 'me';
        li.textContent = r.n + (r.id === state.owner ? ' ★' : '');
        ul.appendChild(li);
      }
      for (let i = list.length; i < state.mode; i++) { const li = document.createElement('li'); li.className = 'bot'; li.textContent = 'a bot fills this seat'; ul.appendChild(li); }
    }
  }

  // ------------------------------------------------------------------ 30 Hz input
  function inputTick() {
    if (!state || !myBody || hub.state !== signalR.HubConnectionState.Connected) return;
    let bits = 0;
    if (isDown('up')) bits |= P.Up;
    if (isDown('down')) bits |= P.Down;
    if (isDown('left')) bits |= P.Left;
    if (isDown('right')) bits |= P.Right;
    if (isDown('dash')) bits |= P.Dash;
    if (document.hidden) bits = 0;
    seq++;
    pending.push({ seq, bits });
    if (pending.length > 90) pending.shift();
    myPrev = { x: myBody.x, y: myBody.y };
    const dashBefore = myBody.dashT;
    P.step(myBody, bits & maskFor(state.ph), wall);
    if (myBody.dashT > 0 && dashBefore <= 0) S.play('jump');
    stepAt = performance.now();
    hub.send('Input', seq, bits, myAim()).catch(() => { });
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
    if (state && state.ph !== 'lobby' && maze) drawGame(t, dt);
    else drawBackdrop(t);
    requestAnimationFrame(frame);
  }

  function drawBackdrop(t) {
    if (maze) {
      g.drawImage(maze.canvas, 0, 0);
      A.drawTorches(g, maze.torches, t);
      light.begin();
      for (const tc of maze.torches) light.add(tc.x, tc.y, 58, 0.9, 'rgba(255,150,60,1)');
      light.add(W / 2, H / 2, 70 + Math.sin(t) * 6, 0.8, 'rgba(255,210,90,1)');
      light.apply(g, 'rgba(6,4,14,0.7)');
      A.drawRelic(g, W / 2, H / 2, t, false);
    } else {
      const grd = g.createRadialGradient(W / 2, H / 2, 10, W / 2, H / 2, 360);
      grd.addColorStop(0, '#2a2140'); grd.addColorStop(1, '#07060f');
      g.fillStyle = grd; g.fillRect(0, 0, W, H);
      A.drawRelic(g, W / 2, H / 2 - 40, t, false);
    }
  }

  function renderTime() { return performance.now() / 1000 + (clockOffset || 0) - 0.15; }
  function sampleAt(rt) {
    let a = snaps[0], b = snaps[snaps.length - 1];
    for (let i = 0; i < snaps.length - 1; i++) if (snaps[i].now <= rt && snaps[i + 1].now >= rt) { a = snaps[i]; b = snaps[i + 1]; break; }
    if (rt > b.now) a = b;
    const k = b.now > a.now ? Math.min(1, Math.max(0, (rt - a.now) / (b.now - a.now))) : 1;
    return { a, b, k };
  }

  function positions() {
    const { a, b, k } = sampleAt(renderTime());
    const out = new Map();
    for (const p of state.ps) {
      if (!p.im) continue;
      if (p.id === me && myBody) {
        const alpha = Math.min(1, (performance.now() - stepAt) / (P.Dt * 1000));
        const x = (myPrev ? myPrev.x + (myBody.x - myPrev.x) * alpha : myBody.x) + offset.x;
        const y = (myPrev ? myPrev.y + (myBody.y - myPrev.y) * alpha : myBody.y) + offset.y;
        const moving = isDown('up') || isDown('down') || isDown('left') || isDown('right');
        out.set(p.id, { p, x, y, faceX: myBody.faceX, faceY: myBody.faceY, moving, dashing: myBody.dashT > 0 });
      } else {
        const pa = a.ps.find((q) => q.id === p.id), pb = b.ps.find((q) => q.id === p.id) || p;
        const x = pa ? pa.x + (pb.x - pa.x) * k : pb.x, y = pa ? pa.y + (pb.y - pa.y) * k : pb.y;
        const moving = pa ? Math.hypot(pb.x - pa.x, pb.y - pa.y) > 0.3 : false;
        out.set(p.id, { p, x, y, faceX: pb.faceX, faceY: pb.faceY, moving, dashing: pb.dashT > 0 });
      }
    }
    return out;
  }

  function drawGame(t, dt) {
    const s = state;
    const sx = shake > 0 ? (Math.random() - 0.5) * shake : 0, sy = shake > 0 ? (Math.random() - 0.5) * shake : 0;
    shake = Math.max(0, shake - dt * 30);
    g.setTransform(1, 0, 0, 1, Math.round(sx), Math.round(sy));
    g.drawImage(maze.canvas, 0, 0);
    // shrines this team can see
    for (const [team, x, y] of s.sh || []) A.drawShrine(g, x, y, team, t, team === myTeam());
    const pos = positions();
    // dash trails
    for (const [id, r] of pos) {
      const tr = trails.get(id) || [];
      if (r.dashing) tr.push({ x: r.x, y: r.y, t });
      while (tr.length && t - tr[0].t > 0.18) tr.shift();
      trails.set(id, tr);
      for (const q of tr) { g.globalAlpha = 0.25 * (1 - (t - q.t) / 0.18); A.drawRunner(g, q.x, q.y, { team: r.p.tm, look: r.p.lk, faceX: r.faceX, faceY: r.faceY, t }); }
      g.globalAlpha = 1;
    }
    // runners, back to front
    const order = [...pos.values()].sort((u, v) => u.y - v.y);
    for (const r of order) {
      A.drawRunner(g, r.x, r.y, { team: r.p.tm, look: r.p.lk, faceX: r.faceX, faceY: r.faceY, t: t + r.p.id.charCodeAt(0), moving: r.moving, me: r.p.id === me, immune: r.p.immune && r.p.id === s.hold });
    }
    A.drawTorches(g, maze.torches, t);
    // the relic over its carrier's head, or flying along a pass
    const carrier = s.hold ? pos.get(s.hold) : null;
    const arc = arcs.find((x) => x.id === s.hold);
    if (carrier) {
      let rx = carrier.x, ry = carrier.y - 24;
      if (arc) {
        const k = Math.min(1, arc.t / arc.life);
        rx = arc.x1 + (rx - arc.x1) * k;
        ry = arc.y1 + (ry - arc.y1) * k - Math.sin(k * Math.PI) * 18;
      }
      A.drawRelic(g, rx, ry, t, true);
    }
    arcs = arcs.filter((x) => (x.t += dt) < x.life);
    // light: torches, relic, shrines, a faint glow around each runner
    light.begin();
    for (const tc of maze.torches) light.add(tc.x, tc.y, 56 + Math.sin(t * 11 + tc.seed) * 3, 0.9, 'rgba(255,150,60,1)');
    if (carrier) light.add(carrier.x, carrier.y - 20, 82, 1, 'rgba(255,210,90,1)');
    for (const [team, x, y] of s.sh || []) light.add(x, y - 6, 52, 0.9, team === 0 ? 'rgba(255,90,80,1)' : 'rgba(80,168,255,1)');
    for (const r of pos.values()) light.add(r.x, r.y - 8, r.p.id === me ? 44 : 30, r.p.id === me ? 0.8 : 0.5, null);
    light.apply(g, 'rgba(8,5,16,0.5)');
    // name tags above the light so they read in the dark
    for (const r of order) tag(r);
    drawParticles(dt);
    drawTexts(dt);
    g.setTransform(1, 0, 0, 1, 0, 0);
    hud(s, t);
  }

  function tag(r) {
    const text = r.p.n;
    g.font = '8px "Pixelify Sans"';
    g.textAlign = 'center';
    const w = Math.ceil(g.measureText(text).width) + 4;
    const x = Math.round(r.x), y = Math.round(r.y) - 26;
    g.fillStyle = 'rgba(0,0,0,0.5)';
    g.fillRect(x - Math.round(w / 2), y - 8, w, 9);
    g.fillStyle = r.p.id === me ? '#ffd25a' : A.TEAMS[r.p.tm].color;
    g.fillText(text, x, y - 1);
  }
  function drawParticles(dt) {
    particles = particles.filter((p) => (p.t += dt) < p.life);
    for (const p of particles) {
      p.x += p.vx * dt; p.y += p.vy * dt; p.vx *= 0.92; p.vy *= 0.92;
      g.globalAlpha = Math.max(0, 1 - p.t / p.life);
      g.fillStyle = p.color;
      g.fillRect(Math.round(p.x), Math.round(p.y), 1, 1);
    }
    g.globalAlpha = 1;
  }
  function drawTexts(dt) {
    texts = texts.filter((x) => (x.t += dt) < x.life);
    g.textAlign = 'center';
    g.font = '10px "Pixelify Sans"';
    for (const x of texts) {
      const y = Math.round(x.y - x.t * 20);
      g.globalAlpha = Math.max(0, 1 - Math.pow(x.t / x.life, 3));
      g.fillStyle = '#000'; g.fillText(x.text, Math.round(x.x) + 1, y + 1);
      g.fillStyle = x.color; g.fillText(x.text, Math.round(x.x), y);
    }
    g.globalAlpha = 1;
  }

  function hud(s, t) {
    g.fillStyle = 'rgba(0,0,0,0.6)';
    g.fillRect(262, 3, 116, 22);
    g.font = '700 14px "Pixelify Sans"';
    g.textAlign = 'center';
    g.fillStyle = A.TEAMS[0].color; g.fillText(String(s.sc[0]), 280, 19);
    g.fillStyle = A.TEAMS[1].color; g.fillText(String(s.sc[1]), 360, 19);
    const tl = Math.ceil(s.ph === 'playing' ? s.tl : 0);
    g.fillStyle = s.ph === 'playing' && s.tl < 20 ? '#ff5a4f' : '#ffffff';
    g.fillText(s.ph === 'playing' ? Math.floor(tl / 60) + ':' + String(tl % 60).padStart(2, '0') : '-:--', 320, 19);
    const mine = s.ps.find((p) => p.id === me);
    if (mine && mine.im && s.ph === 'playing') {
      // what to do now, in one line
      const carrierTeam = s.hold ? teamOf(s.hold) : -1;
      const line = s.hold === me ? 'You carry the relic: run to your shrine.  E or click: pass' : carrierTeam === myTeam() ? 'Protect your carrier, get open for a pass' : 'Catch the carrier: Space (or right click) to steal';
      g.font = '10px "Pixelify Sans"';
      g.fillStyle = 'rgba(0,0,0,0.55)';
      const w = g.measureText(line).width + 12;
      g.fillRect(W / 2 - w / 2, H - 22, w, 16);
      g.fillStyle = s.hold === me ? '#ffd25a' : '#e8e4f2';
      g.fillText(line, W / 2, H - 10);
      // dash readiness
      const cd = myBody ? myBody.dashCd : 0;
      g.fillStyle = 'rgba(0,0,0,0.6)'; g.fillRect(10, H - 22, 64, 16);
      g.fillStyle = cd > 0 ? '#5a5468' : '#7fe3ff'; g.fillRect(12, H - 20, 60 * (1 - cd / P.DashCooldown), 12);
      g.fillStyle = '#ffffff'; g.font = '8px "Pixelify Sans"'; g.fillText('DASH  Shift', 42, H - 11);
    }
    if (s.ph === 'countdown') {
      g.font = '700 48px "Pixelify Sans"';
      g.fillStyle = '#000'; g.fillText(String(Math.ceil(s.tl)), W / 2 + 2, H / 2 + 22);
      g.fillStyle = '#ffffff'; g.fillText(String(Math.ceil(s.tl)), W / 2, H / 2 + 20);
    }
    if (banner && (banner.until === Infinity || performance.now() < banner.until)) {
      g.font = '700 26px "Pixelify Sans"';
      g.fillStyle = '#000'; g.fillText(banner.text, W / 2 + 2, H / 2 - 38);
      g.fillStyle = banner.color; g.fillText(banner.text, W / 2, H / 2 - 40);
      if (banner.sub) { g.font = '12px "Pixelify Sans"'; g.fillStyle = '#ffffff'; g.fillText(banner.sub, W / 2, H / 2 - 20); }
    }
    if (s.ph === 'over') { g.font = '10px "Pixelify Sans"'; g.fillStyle = '#a39bbd'; g.fillText('Back to the lobby in ' + Math.ceil(s.tl), W / 2, H / 2); }
  }

  // ------------------------------------------------------------------ touch
  if (matchMedia('(pointer: coarse)').matches) {
    $('touch').classList.remove('hidden');
    document.querySelectorAll('#touch [data-hold]').forEach((b) => {
      const a = b.dataset.hold;
      b.addEventListener('pointerdown', (e) => { e.preventDefault(); virtual.add(a); S.unlock(); });
      for (const ev of ['pointerup', 'pointercancel', 'pointerleave']) b.addEventListener(ev, () => virtual.delete(a));
    });
    document.querySelectorAll('#touch [data-press]').forEach((b) => b.addEventListener('pointerdown', (e) => { e.preventDefault(); press(b.dataset.press); }));
  }

  document.fonts.load('12px "Pixelify Sans"').catch(() => { });
  requestAnimationFrame(frame);
})();
