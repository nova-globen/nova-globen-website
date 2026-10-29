/* Bullet Noir - browser client. The server owns the match (Lek.Server.Noir); this file sends inputs at 30 Hz
   with sequence numbers, predicts your own body with physics.js, draws everyone else 100 ms in the past, and runs an
   over-the-shoulder camera with pointer-lock aim. Your own shots are drawn at once; everyone else's bullets play on the
   same delayed timeline as their bodies, so what you see lines up. world.js draws it all. */
import * as THREE from 'three';
import { World, rayBox } from './world.js?v=2';

const P = window.NoirPhysics, S = window.NoirAudio;
const $ = (id) => document.getElementById(id);
const FIELDS = ['id', 'x', 'z', 'diveT', 'diveX', 'diveZ', 'diveCd', 'speed', 'prevDive', 'seq', 'aimYaw', 'aimPitch', 'hp', 'alive', 'slot', 'second', 'ammo', 'reloading', 'meter', 'bt', 'protect', 'kills', 'deaths', 'respawn', 'yaw'];
const GUNS = [
  { name: 'PISTOL', mag: 12, cd: 0.26, pellets: 1, spread: 0.006, speed: 90 },
  { name: 'SHOTGUN', mag: 6, cd: 0.85, pellets: 8, spread: 0.055, speed: 75 },
  { name: 'SMG', mag: 30, cd: 0.085, pellets: 1, spread: 0.032, speed: 90 },
  { name: 'RIFLE', mag: 25, cd: 0.12, pellets: 1, spread: 0.014, speed: 100 },
  { name: 'BARREL' }
];
const BUBBLE = 5, DIVE_BUBBLE = 3.2, SLOW_BULLETS = 0.2, INTERP = 0.1, KILLS_TO_WIN = 15;
const params = new URLSearchParams(location.search);
const store = {
  get(k, d) { try { const v = localStorage.getItem(LekSite.key(k)); return v == null ? d : v; } catch { return d; } },
  set(k, v) { try { localStorage.setItem(LekSite.key(k), v); } catch { } }
};

const world = new World($('view'));
let map = null, ready = false;
let me = null, state = null, snaps = [], clockOffset = null, lastPhase = 'lobby', lobbySig = '';
const roster = new Map();
let seq = 0, pending = [], myBody = null, offset = { x: 0, z: 0 };
let yaw = 0, pitch = -0.08, aim = { yaw: 0, pitch: 0 }, camPos = null;
let localCd = 0, firedSeqs = [], mine = null, wasAlive = false, wasDiving = false, wasBt = false;
let bullets = [], bubbleList = [], feed = [], banner = null, hurt = 0, hitmark = 0, hitHead = false, deadBy = '';

// ------------------------------------------------------------------ input
const held = new Set();
let mouseDown = false;
const KEYS = { fwd: ['KeyW'], back: ['KeyS'], left: ['KeyA'], right: ['KeyD'], turnL: ['ArrowLeft'], turnR: ['ArrowRight'], lookU: ['ArrowUp'], lookD: ['ArrowDown'], dive: ['Space', 'ShiftLeft', 'ShiftRight'], fire: ['KeyF'], bt: ['KeyQ', 'KeyE'], reload: ['KeyR'], w1: ['Digit1'], w2: ['Digit2'], board: ['Tab'], sensDown: ['BracketLeft'], sensUp: ['BracketRight'] };
const isDown = (a) => KEYS[a].some((k) => held.has(k));
const playing = () => state && (state.ph === 'playing' || state.ph === 'countdown') && me;

function onKeyDown(e) {
  if (e.target && e.target.tagName === 'INPUT') return;
  const action = Object.keys(KEYS).find((a) => KEYS[a].includes(e.code));
  if (!action) return;
  if (playing()) e.preventDefault();
  if (held.has(e.code)) return;
  held.add(e.code);
  if (!playing() || !hubUp()) return;
  if (action === 'bt') hub.send('BulletTime').catch(() => { });
  if (action === 'reload') hub.send('Reload').catch(() => { });
  if (action === 'w1') hub.send('Weapon', 0).catch(() => { });
  if (action === 'w2') hub.send('Weapon', 1).catch(() => { });
  if (action === 'sensDown') setSens(sens - 0.1);
  if (action === 'sensUp') setSens(sens + 0.1);
}
window.addEventListener('keydown', onKeyDown);
window.addEventListener('keyup', (e) => held.delete(e.code));
window.addEventListener('blur', () => { held.clear(); mouseDown = false; });
try { window.parent.addEventListener('keydown', onKeyDown); window.parent.addEventListener('keyup', (e) => held.delete(e.code)); } catch { }

const canvas = $('view');
const locked = () => document.pointerLockElement === canvas;
window.addEventListener('mousedown', (e) => { (window.__allDowns = window.__allDowns || []).push([Math.round(performance.now()), e.button, e.target.id || e.target.tagName]); }, true);
canvas.addEventListener('mousedown', (e) => {
  S.unlock();
  if (!playing()) return;
  if (aimMode === 'lock' && !locked()) { lockMouse(); return; }
  downs.push([Math.round(performance.now()), e.button, e.target.id || e.target.tagName]);
  if (aimMode === 'cursor') { const r = canvas.getBoundingClientRect(); cursor.x = e.clientX - r.left; cursor.y = e.clientY - r.top; cursor.inside = true; }
  if (e.button === 0) { mouseDown = true; clickFire = true; }
  if (e.button === 2 && hubUp()) hub.send('BulletTime').catch(() => { });
});
window.addEventListener('mouseup', (e) => { if (e.button === 0) mouseDown = false; });
canvas.addEventListener('contextmenu', (e) => e.preventDefault());

// Mouse look. Raw input (no Windows acceleration) by default, switchable in the lobby, because some browser and
// mouse combinations report it badly. The first event after locking is skipped (it can carry the whole jump of the
// cursor) and single events are capped rather than dropped, so a fast or high-DPI mouse still turns.
let rawMouse = store.get('noir-raw', '1') === '1', rawActive = false;
async function lockMouse() {
  rawActive = false;
  if (rawMouse) {
    try { await canvas.requestPointerLock({ unadjustedMovement: true }); rawActive = true; } catch { }
  }
  if (!rawActive) {
    try { await canvas.requestPointerLock(); }
    catch { setAimMode('cursor', 'This browser will not capture the mouse: cursor aiming is on.'); return; }
  }
  skipMoves = 1;
}

// Cursor aiming, for Remote Desktop, touchpads and browsers without pointer lock: the crosshair is the cursor, you shoot
// where it points, and holding it toward the left or right edge turns the camera (top and bottom edges look up and down).
let aimMode = store.get('noir-aim', 'lock') === 'cursor' ? 'cursor' : 'lock';
const cursor = { x: 0, y: 0, inside: false };
function setAimMode(mode, why) {
  aimMode = mode;
  store.set('noir-aim', mode);
  $('aim-mode').value = mode;
  if (mode === 'cursor' && locked()) document.exitPointerLock();
  canvas.classList.toggle('cursor-aim', mode === 'cursor');
  if (why) banner = { text: 'CURSOR AIM', sub: why, until: performance.now() + 2600 };
}
// tracked on the whole document and clamped to the view: pushing the cursor past the edge keeps turning, as it should
document.addEventListener('mousemove', (e) => {
  const r = canvas.getBoundingClientRect();
  cursor.x = Math.max(0, Math.min(r.width, e.clientX - r.left));
  cursor.y = Math.max(0, Math.min(r.height, e.clientY - r.top));
  cursor.inside = true;
  rec.cursorMoves++;
});
window.addEventListener('blur', () => { cursor.inside = false; });
/** Turning from the cursor's distance to the edges (cursor mode) and from the arrow keys (always). */
function steer(dt) {
  if (!playing()) return;
  let turn = (isDown('turnR') ? 1 : 0) - (isDown('turnL') ? 1 : 0);
  let look = (isDown('lookU') ? 1 : 0) - (isDown('lookD') ? 1 : 0);
  if (aimMode === 'cursor' && cursor.inside) {
    const r = canvas.getBoundingClientRect();
    const nx = cursor.x / r.width * 2 - 1, ny = cursor.y / r.height * 2 - 1;
    const edge = (v, from) => Math.sign(v) * Math.max(0, (Math.abs(v) - from) / (1 - from));
    turn += edge(nx, 0.45) * 1.4;
    look -= edge(ny, 0.6);
  }
  yaw -= turn * 2.4 * sens * dt;
  rec.turned += Math.abs(turn * 2.4 * sens * dt);
  pitch = Math.max(-1.1, Math.min(0.95, pitch + look * 1.3 * dt));
}
let sens = Number(store.get('noir-sens', '1')) || 1, skipMoves = 0;
const SENS_BASE = 0.0018, CAP = 120;
const probe = { n: 0, sx: 0, sy: 0, maxX: 0, maxY: 0, capped: 0, last: '' };
document.addEventListener('mousemove', (e) => {
  if (!locked()) return;
  if (skipMoves > 0) { skipMoves--; return; }
  let mx = e.movementX || 0, my = e.movementY || 0;
  probe.n++; probe.sx += Math.abs(mx); probe.sy += Math.abs(my);
  probe.maxX = Math.max(probe.maxX, Math.abs(mx)); probe.maxY = Math.max(probe.maxY, Math.abs(my));
  if (Math.abs(mx) > CAP || Math.abs(my) > CAP) probe.capped++;
  rec.n++;
  if (mx < 0) rec.left -= mx; else rec.right += mx;
  rec.y += Math.abs(my);
  if (mx === 0 && my !== 0) rec.yOnly++;
  rec.maxX = Math.max(rec.maxX, Math.abs(mx));
  mx = Math.max(-CAP, Math.min(CAP, mx));
  my = Math.max(-CAP, Math.min(CAP, my));
  const k = SENS_BASE * sens;
  yaw -= mx * k;
  pitch = Math.max(-1.1, Math.min(0.95, pitch - my * k));
});
// F2: a readout of what the mouse really reports, once a second
let showProbe = false;
setInterval(() => {
  probe.last = `mouse ${rawActive ? 'raw' : 'normal'} · ${probe.n} moves/s · x ${probe.sx} (max ${probe.maxX}) · y ${probe.sy} (max ${probe.maxY}) · capped ${probe.capped} · yaw ${yaw.toFixed(2)}`;
  Object.assign(probe, { n: 0, sx: 0, sy: 0, maxX: 0, maxY: 0, capped: 0 });
  $('probe').textContent = showProbe ? probe.last : '';
}, 1000);
// Play-test recorder: every 2 s while in a match, one line about the mouse goes to the server log (NoirHub.Diag),
// so a problem on a real machine can be read afterwards. Also every pointer-lock, focus and visibility change.
const rec = { n: 0, left: 0, right: 0, y: 0, yOnly: 0, maxX: 0, notes: [], cursorMoves: 0, frames: 0, worst: 0, turned: 0 };
const note = (t) => { rec.notes.push(t); if (rec.notes.length > 8) rec.notes.shift(); };
const inFrame = (() => { try { return window.top !== window; } catch { return true; } })();
document.addEventListener('pointerlockchange', () => note(aimMode === 'cursor' ? 'cursormode' : locked() ? 'lock' + (rawActive ? ':raw' : ':normal') : 'unlock'));
document.addEventListener('pointerlockerror', () => note('lockerror'));
window.addEventListener('blur', () => note('blur'));
window.addEventListener('focus', () => note('focus'));
document.addEventListener('visibilitychange', () => note('vis:' + document.visibilityState));
setInterval(() => {
  if (!playing() || !hubUp()) { rec.notes = []; return; }
  if (rec.n === 0 && rec.cursorMoves === 0 && rec.notes.length === 0 && !locked() && !mine) return;
  const cr = canvas.getBoundingClientRect();
  const line = `${inFrame ? 'iframe' : 'page'} aim=${aimMode} ${mine ? (mine.alive ? 'alive hp=' + mine.hp : 'DEAD') : '-'} fps=${(rec.frames / 2).toFixed(0)} worstFrame=${Math.round(rec.worst * 1000)}ms cursor=${cursor.inside ? (cursor.x / cr.width).toFixed(2) + ',' + (cursor.y / cr.height).toFixed(2) : 'out'} cursorMoves=${rec.cursorMoves} turned=${rec.turned.toFixed(2)} ${locked() ? 'locked' : 'free'} ${rawActive ? 'raw' : 'normal'} moves=${rec.n} left=${rec.left} right=${rec.right} y=${rec.y} yOnly=${rec.yOnly} maxX=${rec.maxX} yaw=${yaw.toFixed(2)} keys=${[...held].join('+') || '-'} sens=${sens} ${rec.notes.join(',')}`;
  hub.send('Diag', line).catch(() => { });
  Object.assign(rec, { n: 0, left: 0, right: 0, y: 0, yOnly: 0, maxX: 0, notes: [], cursorMoves: 0, frames: 0, worst: 0, turned: 0 });
}, 2000);
window.addEventListener('keydown', (e) => { if (e.code === 'F2') { e.preventDefault(); showProbe = !showProbe; $('probe').textContent = showProbe ? probe.last : ''; } });
$('aim-mode').value = aimMode;
canvas.classList.toggle('cursor-aim', aimMode === 'cursor');
$('aim-mode').addEventListener('change', () => setAimMode($('aim-mode').value));
$('raw').checked = rawMouse;
$('raw').addEventListener('change', () => { rawMouse = $('raw').checked; store.set('noir-raw', rawMouse ? '1' : '0'); });
function setSens(v) {
  sens = Math.max(0.2, Math.min(3, Math.round(v * 10) / 10));
  store.set('noir-sens', String(sens));
  $('sens').value = String(sens);
  $('sens-val').textContent = sens.toFixed(1);
  banner = { text: 'MOUSE ' + sens.toFixed(1), sub: '[ and ] change it', until: performance.now() + 900 };
}
$('sens').value = String(sens);
$('sens-val').textContent = sens.toFixed(1);
$('sens').addEventListener('input', () => setSens(Number($('sens').value)));
canvas.addEventListener('wheel', (e) => {
  if (!playing() || !mine || !hubUp()) return;
  hub.send('Weapon', mine.slot === 1 ? 0 : 1).catch(() => { });
}, { passive: true });
document.addEventListener('pointerlockchange', () => { if (!locked()) mouseDown = false; });
// for automated play tests, where pointer lock is not available: look, hold the trigger, read the state
let debugFire = false, clickFire = false;
const trace = [], downs = [];
window.noirDebug = {
  look(y, p) { yaw = y; if (p != null) pitch = p; },
  fire(on) { debugFire = !!on; },
  bt() { if (hubUp()) hub.send('BulletTime'); },
  me: () => mine, aim: () => aim, players: () => state && state.ps, world: () => world, trace: () => trace, downs: () => downs
};

// ------------------------------------------------------------------ connection
const hub = new signalR.HubConnectionBuilder().withUrl(LekSite.url('/hubs/noir')).withAutomaticReconnect().configureLogging(signalR.LogLevel.Warning).build();
const hubUp = () => hub.state === signalR.HubConnectionState.Connected;
hub.on('state', onState);
hub.onreconnected(() => { me = null; state = null; showMenu('Connection was lost. Join again.'); });
hub.onclose(() => showMenu('Disconnected.'));
async function connect() {
  if (hubUp()) return true;
  try { await hub.start(); return true; } catch (err) { setMsg('menu-msg', 'Cannot reach the server.'); console.error(err); return false; }
}
function myName() { const n = $('name').value.trim().slice(0, 14); store.set('noir-name', n); return n; }
$('name').value = params.get('name') || store.get('noir-name', '');
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
}
$('create').onclick = async () => { S.unlock(); if (ready && await connect()) enter(await hub.invoke('CreateRoom', myName())); };
$('join').onclick = async () => {
  S.unlock();
  const code = $('code').value.trim().toUpperCase();
  if (code.length !== 4) { setMsg('menu-msg', 'A room code has 4 letters.'); return; }
  if (ready && await connect()) enter(await hub.invoke('JoinRoom', code, myName()));
};
$('code').addEventListener('keydown', (e) => { if (e.key === 'Enter') $('join').click(); });
$('start').onclick = async () => { S.unlock(); const r = await hub.invoke('Start'); if (r && r.error) setMsg('lobby-msg', r.error); };
$('leave').onclick = async () => { await hub.invoke('Leave'); me = null; state = null; showMenu(''); };
$('share').onclick = async () => {
  const url = location.origin + LekSite.url('/bullet-noir') + '?room=' + $('room-code').textContent;
  try { await navigator.clipboard.writeText(url); setMsg('lobby-msg', 'Invite link copied.'); } catch { setMsg('lobby-msg', url); }
};
document.querySelectorAll('#sizes button').forEach((b) => { b.onclick = async () => { const r = await hub.invoke('SetSize', Number(b.dataset.size)); if (r && r.error) setMsg('lobby-msg', r.error); }; });
function showMenu(msg) {
  $('lobby').classList.add('hidden');
  $('hud').classList.add('hidden');
  $('menu').classList.remove('hidden');
  setMsg('menu-msg', msg);
  myBody = null; pending = []; mine = null;
  for (const id of [...world.players.keys()]) world.removeCharacter(id);
  if (locked()) document.exitPointerLock();
}
setInterval(async () => {
  if (!hubUp()) { $('ping').textContent = ''; return; }
  const t0 = performance.now();
  try { await hub.invoke('Ping', t0); $('ping').textContent = Math.round(performance.now() - t0) + ' ms'; } catch { $('ping').textContent = ''; }
}, 2000);
$('btn-full').onclick = async () => {
  try { if (document.fullscreenElement) await document.exitFullscreen(); else await document.documentElement.requestFullscreen({ navigationUI: 'hide' }); } catch { }
};
const soundIcon = () => { $('btn-sound').innerHTML = S.isMuted() ? '&#128263;' : '&#128266;'; };
soundIcon();
$('btn-sound').onclick = () => { S.unlock(); S.setMuted(!S.isMuted()); soundIcon(); };

// the map and the models load before you can create or join, so the docks are already there behind the menu
(async () => {
  $('create').disabled = $('join').disabled = true;
  if (!await connect()) { $('loading').textContent = 'Cannot reach the server.'; return; }
  map = await hub.invoke('Map');
  const names = new Set(['Character_Enemy', 'Character_Soldier', 'Character_Hazmat', 'Exploding_Barrel', 'Health', 'Pistol', 'Shotgun', 'Smg', 'Ak47']);
  for (const p of map.props) names.add(p.m);
  for (const p of map.decor) names.add(p.m);
  await world.load([...names], (n, of) => { $('loading').textContent = `Loading the docks… ${Math.round(n / of * 100)}%`; });
  world.build(map);
  ready = true;
  $('loading').textContent = '';
  $('create').disabled = $('join').disabled = false;
  if (params.get('room') && $('name').value) $('join').click();
})();

// ------------------------------------------------------------------ snapshots
const serverNow = () => performance.now() / 1000 + (clockOffset || 0);

function onState(s) {
  const arrival = performance.now() / 1000;
  const off = s.now - arrival;
  if (clockOffset == null || off > clockOffset || Math.abs(off - clockOffset) > 1) clockOffset = off; else clockOffset -= 0.0005;
  if (s.ro) { roster.clear(); for (const r of s.ro) roster.set(r.id, r); }
  s.ps = s.ps.map((arr) => { const o = {}; FIELDS.forEach((k, i) => { o[k] = arr[i]; }); o.alive = !!o.alive; o.bt = !!o.bt; o.protect = !!o.protect; o.prevDive = !!o.prevDive; return o; });
  s.pmap = new Map(s.ps.map((p) => [p.id, p]));
  state = s;
  snaps.push(s);
  while (snaps.length > 2 && snaps[0].now < s.now - 1.5) snaps.shift();
  if (s.ph !== lastPhase) onPhase(s.ph, s);
  lastPhase = s.ph;
  for (const e of s.ev || []) onEvent(e, s);
  reconcile(s);
  renderLobby();
  world.setBarrels(s.br);
}

function reconcile(s) {
  const p = s.pmap.get(me);
  mine = p || null;
  if (!p || s.ph === 'lobby' || !p.alive) { myBody = null; pending = []; return; }
  const before = myBody ? { x: myBody.x + offset.x, z: myBody.z + offset.z } : null;
  myBody = P.body({ x: p.x, z: p.z, diveT: p.diveT, diveX: p.diveX, diveZ: p.diveZ, diveCd: p.diveCd, speed: p.speed, prevDive: p.prevDive });
  pending = pending.filter((i) => i.seq > p.seq);
  for (const i of pending) P.step(myBody, i.bits & maskFor(s.ph), i.yaw, map.solids);
  firedSeqs = firedSeqs.filter((q) => q > p.seq);
  if (before) {
    const dx = before.x - myBody.x, dz = before.z - myBody.z;
    if (Math.hypot(dx, dz) < 2) { offset.x = dx; offset.z = dz; } else { offset.x = 0; offset.z = 0; }
  }
}
const maskFor = (ph) => (ph === 'playing' ? ~0 : 0);

function onPhase(ph, s) {
  if (ph === 'lobby') {
    $('lobby').classList.remove('hidden'); $('hud').classList.add('hidden'); lobbySig = ''; banner = null;
    if (locked()) document.exitPointerLock();
    bullets.forEach((b) => world.scene.remove(b.mesh)); bullets = [];
  } else {
    $('lobby').classList.add('hidden'); $('hud').classList.remove('hidden');
  }
  if (ph === 'over') {
    const w = roster.get(s.win);
    banner = { text: s.win === me ? 'YOU WIN' : (w ? w.n.toUpperCase() : 'NOBODY') + ' WINS', sub: 'The docks go quiet.', until: Infinity };
    S.play(s.win === me ? 'win' : 'lose');
    if (locked()) document.exitPointerLock();
  }
}

const name = (id) => (roster.get(id) || {}).n || '?';

function onEvent(e, s) {
  const [k] = e;
  switch (k) {
    case 'round': banner = { text: 'THE DOCKS', sub: 'First to ' + KILLS_TO_WIN + ' kills. Click to fight.', until: performance.now() + 2600 }; S.play('beep'); break;
    case 'go': banner = { text: 'GO', sub: '', until: performance.now() + 900 }; S.play('go'); break;
    case 'shot': {
      const [, id, owner, gun, ox, oy, oz, dx, dy, dz, sp, t] = e;
      if (owner === me) break;
      bullets.push({ id, owner, gun, x: ox, y: oy, z: oz, dx, dy, dz, sp, start: t, end: null, own: false, travelled: 0, started: false, mesh: null });
      break;
    }
    case 'end': {
      const [, id, x, y, z, what, t] = e;
      const b = bullets.find((q) => q.id === id && !q.own);
      if (b) b.end = { x, y, z, what, t };
      break;
    }
    case 'hit': {
      const [, victim, attacker, dmg, head] = e;
      if (attacker === me && victim !== me) { hitmark = 1; hitHead = !!head; S.play(head ? 'headshot' : 'hitmark'); }
      if (victim === me) { hurt = Math.min(1, hurt + dmg / 45); world.shake = Math.max(world.shake, 0.12 + dmg / 200); S.play('hurt'); }
      break;
    }
    case 'kill': {
      const [, victim, killer, gun, head] = e;
      const self = victim === killer;
      feed.unshift({ html: self ? `<b>${esc(name(victim))}</b> blew himself up` : `<b>${esc(name(killer))}</b> ${GUNS[gun] ? GUNS[gun].name.toLowerCase() : ''}${head ? ' ✸' : ''} <b>${esc(name(victim))}</b>`, me: victim === me || killer === me, until: performance.now() + 5000 });
      feed = feed.slice(0, 5);
      if (killer === me && !self) S.play('kill');
      if (victim === me) { deadBy = self ? 'You stood too close to the barrel.' : `Shot by ${name(killer)}${head ? ', in the head' : ''}.`; S.play('die'); }
      break;
    }
    case 'boom': {
      const [, , x, z] = e;
      world.explosion(x, z);
      S.play('boom', { volume: volumeAt(x, z) });
      break;
    }
    case 'pick': { const [, who, kind] = e; if (who === me) S.play(kind < 0 ? 'heal' : 'pick'); break; }
    case 'reload': if (e[1] === me) S.play('reload'); break;
    case 'reloaded': if (e[1] === me) S.play('reloaded'); break;
    case 'spawn': if (e[1] === me) { const p = s.pmap.get(me); if (p) { yaw = p.yaw; pitch = -0.08; } hurt = 0; deadBy = ''; } break;
    case 'over': break;
  }
}
const esc = (t) => String(t).replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
function volumeAt(x, z) {
  const c = camPos || { x: 25, z: 20 };
  return Math.max(0, Math.min(1, 1.2 - Math.hypot(x - c.x, z - c.z) / 40));
}

// ------------------------------------------------------------------ lobby
function renderLobby() {
  if (!state || !me || state.ph !== 'lobby') return;
  const owner = state.owner === me;
  const sig = JSON.stringify([state.size, owner, [...roster.values()].map((r) => [r.id, r.n])]);
  if (sig === lobbySig) return;
  lobbySig = sig;
  document.querySelectorAll('#sizes button').forEach((b) => { b.classList.toggle('on', Number(b.dataset.size) === state.size); b.disabled = !owner; });
  $('start').disabled = !owner;
  $('start').textContent = owner ? 'Start the shootout' : 'Waiting for the owner';
  const ul = $('players');
  ul.innerHTML = '';
  const list = [...roster.values()].filter((r) => !r.bot);
  for (const r of list) {
    const li = document.createElement('li');
    if (r.id === me) li.className = 'me';
    li.textContent = r.n + (r.id === state.owner ? ' ★' : '');
    ul.appendChild(li);
  }
  for (let i = list.length; i < state.size; i++) { const li = document.createElement('li'); li.className = 'bot'; li.textContent = 'a bot takes this seat'; ul.appendChild(li); }
}

// ------------------------------------------------------------------ aim
const camDir = () => ({ x: Math.sin(yaw) * Math.cos(pitch), y: Math.sin(pitch), z: Math.cos(yaw) * Math.cos(pitch) });

function muzzleOf(x, z, diveT, ay) {
  const h = P.height({ diveT });
  const y = diveT < 0 ? 1.42 : h * 0.72;
  const rx = -Math.cos(ay), rz = Math.sin(ay);
  return { x: x + rx * 0.2 + Math.sin(ay) * 0.3, y, z: z + rz * 0.2 + Math.cos(ay) * 0.3 };
}

/** Everyone else as boxes, for the aim ray and for where your own tracers stop. */
function targetBoxes(renderT) {
  const boxes = [];
  if (!state) return boxes;
  for (const p of state.ps) {
    if (p.id === me) continue;
    const q = sample(p.id, renderT);
    if (!q || !q.alive) continue;
    const h = P.height(q), r = q.diveT >= 0 ? 0.5 : 0.4;
    boxes.push([q.x - r, q.z - r, q.x + r, q.z + r, h, 'flesh']);
  }
  (map ? map.barrels : []).forEach(([x, z], i) => { if (state.br && state.br[i]) boxes.push([x - 0.32, z - 0.32, x + 0.32, z + 0.32, 0.84, 'barrel']); });
  return boxes;
}

/** Where the crosshair points: a ray from the camera, skipping anything between the camera and you. */
function updateAim(renderT) {
  if (!myBody || !camPos) return;
  let d = camDir();
  if (aimMode === 'cursor' && cursor.inside) {
    const r = canvas.getBoundingClientRect();
    world.camera.updateMatrixWorld();
    const v = new THREE.Vector3(cursor.x / r.width * 2 - 1, -(cursor.y / r.height * 2 - 1), 0.5).unproject(world.camera).sub(world.camera.position).normalize();
    d = { x: v.x, y: v.y, z: v.z };
  }
  const pivot = { x: myBody.x, y: 1.5, z: myBody.z };
  const skip = Math.max(0, (pivot.x - camPos.x) * d.x + (pivot.y - camPos.y) * d.y + (pivot.z - camPos.z) * d.z);
  const o = { x: camPos.x + d.x * skip, y: camPos.y + d.y * skip, z: camPos.z + d.z * skip };
  const t = Math.min(90, world.ray(o.x, o.y, o.z, d.x, d.y, d.z, targetBoxes(renderT)));
  const target = { x: o.x + d.x * t, y: o.y + d.y * t, z: o.z + d.z * t };
  const m = muzzleOf(myBody.x, myBody.z, myBody.diveT, yaw);
  const ax = target.x - m.x, ay = target.y - m.y, az = target.z - m.z;
  const flat = Math.hypot(ax, az);
  if (flat < 0.6) { aim.yaw = yaw; aim.pitch = pitch; return; }
  aim.yaw = Math.atan2(ax, az);
  aim.pitch = Math.atan2(ay, flat);
}

// ------------------------------------------------------------------ 30 Hz input
function inputTick() {
  if (!state || !myBody || !hubUp()) return;
  let bits = 0;
  if (isDown('fwd')) bits |= P.Fwd;
  if (isDown('back')) bits |= P.Back;
  if (isDown('left')) bits |= P.Left;
  if (isDown('right')) bits |= P.Right;
  if (isDown('dive')) bits |= P.Dive;
  // a click always fires once, even when it is released before the next tick
  if (((mouseDown || clickFire) && (locked() || aimMode === 'cursor')) || isDown('fire') || debugFire) bits |= P.Fire;
  clickFire = false;
  if (document.hidden) bits = 0;
  seq++;
  pending.push({ seq, bits, yaw });
  if (pending.length > 90) pending.shift();
  const diving = myBody.diveT >= 0;
  P.step(myBody, bits & maskFor(state.ph), yaw, map.solids);
  if (myBody.diveT >= 0 && !diving) S.play('dive');
  localFire(bits);
  hub.send('Input', seq, bits, yaw, aim.yaw, aim.pitch).catch(() => { });
  trace.push([Math.round(performance.now()), seq, bits, mouseDown ? 1 : 0, localCd.toFixed(2)]); if (trace.length > 600) trace.shift();
}

/** Your own shots, drawn at once: same cooldown and spread as the server, ammo counted against what it last said. */
function localFire(bits) {
  if (!mine) return;
  localCd = Math.max(0, localCd - P.Dt * myBody.speed);
  if (!(bits & P.Fire) || state.ph !== 'playing' || localCd > 0 || mine.reloading) return;
  const gunIndex = mine.slot === 1 && mine.second >= 0 ? mine.second : 0;
  const gun = GUNS[gunIndex];
  if (mine.ammo - firedSeqs.length <= 0) { S.play('empty'); localCd = 0.3; return; }
  localCd = gun.cd;
  firedSeqs.push(seq);
  const m = muzzleOf(myBody.x, myBody.z, myBody.diveT, aim.yaw);
  const cp = Math.cos(aim.pitch);
  const dx = Math.sin(aim.yaw) * cp, dy = Math.sin(aim.pitch), dz = Math.cos(aim.yaw) * cp;
  const spread = gun.spread * (myBody.diveT >= 0 ? 1.4 : 1) * ((bits & 15) ? 1.3 : 1);
  const boxes = targetBoxes(serverNow() - INTERP);
  for (let i = 0; i < gun.pellets; i++) {
    let ex = dx + gauss() * spread, ey = dy + gauss() * spread, ez = dz + gauss() * spread;
    const len = Math.hypot(ex, ey, ez);
    ex /= len; ey /= len; ez /= len;
    // where it stops: the nearest box, a player, a barrel or the floor
    let best = Math.min(70, world.ray(m.x, m.y, m.z, ex, ey, ez));
    let what = m.y + ey * best <= 0.01 ? 'floor' : 'wall';
    for (const b of boxes) { const t = rayBox(m.x, m.y, m.z, ex, ey, ez, b[0], 0, b[1], b[2], b[4], b[3]); if (t < best) { best = t; what = b[5]; } }
    bullets.push({ id: -1, owner: me, gun: gunIndex, x: m.x, y: m.y, z: m.z, dx: ex, dy: ey, dz: ez, sp: gun.speed, start: 0, own: true, travelled: 0, started: true, maxDist: best, what, mesh: null });
  }
  world.muzzle(m.x, m.y, m.z, gunIndex === 1);
  const c = world.players.get(me);
  if (c) c.lastShot = world.time;
  S.play('shot', { gun: gunIndex });
}
function gauss() { const u = 1 - Math.random(), v = Math.random(); return Math.sqrt(-2 * Math.log(u)) * Math.cos(2 * Math.PI * v); }

// ------------------------------------------------------------------ interpolation
function sample(id, t) {
  if (!snaps.length) return null;
  let a = null, b = null;
  for (let i = snaps.length - 1; i >= 0; i--) { if (snaps[i].now <= t) { a = snaps[i]; b = snaps[i + 1] || snaps[i]; break; } }
  if (!a) { a = b = snaps[0]; }
  const pa = a.pmap.get(id), pb = b.pmap.get(id);
  if (!pa && !pb) return null;
  if (!pa || !pb || !pb.alive || !pa.alive) return Object.assign({}, pb || pa, { vx: 0, vz: 0 });
  const span = b.now - a.now;
  const k = span > 0 ? Math.max(0, Math.min(1, (t - a.now) / span)) : 1;
  const jump = Math.hypot(pb.x - pa.x, pb.z - pa.z) > 4;
  const o = Object.assign({}, pb);
  o.x = jump ? pb.x : pa.x + (pb.x - pa.x) * k;
  o.z = jump ? pb.z : pa.z + (pb.z - pa.z) * k;
  o.aimYaw = lerpAngle(pa.aimYaw, pb.aimYaw, k);
  o.aimPitch = pa.aimPitch + (pb.aimPitch - pa.aimPitch) * k;
  o.diveT = pa.diveT >= 0 && pb.diveT >= pa.diveT ? pa.diveT + (pb.diveT - pa.diveT) * k : pb.diveT;
  o.vx = span > 0 && !jump ? (pb.x - pa.x) / span : 0;
  o.vz = span > 0 && !jump ? (pb.z - pa.z) / span : 0;
  return o;
}
function lerpAngle(a, b, k) { let d = b - a; while (d > Math.PI) d -= Math.PI * 2; while (d < -Math.PI) d += Math.PI * 2; return a + d * k; }

function bubbleAt(x, z) {
  for (const b of bubbleList) if (Math.hypot(b.x - x, b.z - z) <= b.r) return true;
  return false;
}

// ------------------------------------------------------------------ frame
let last = performance.now(), acc = 0, fpsT = 0, fpsN = 0;
function frame(ms) {
  const raw = (ms - last) / 1000;
  const dt = Math.min(0.1, raw);
  last = ms;
  acc += dt;
  let n = 0;
  while (acc >= P.Dt && n < 4) { inputTick(); acc -= P.Dt; n++; }
  if (acc > P.Dt * 4) acc = 0;
  fpsT += raw; fpsN++;
  rec.frames++; rec.worst = Math.max(rec.worst, raw);
  if (fpsT > 1) { $('fps').textContent = fpsN + ' fps'; fpsT = 0; fpsN = 0; }
  offset.x *= 0.85; offset.z *= 0.85;
  const renderT = serverNow() - INTERP;
  const inGame = state && me && state.ph !== 'lobby' && map;

  // characters and bubbles
  bubbleList = [];
  const seen = new Set();
  if (inGame) {
    for (const p of state.ps) {
      const isMe = p.id === me;
      let q;
      if (isMe && myBody) {
        q = Object.assign({}, p, { x: myBody.x + offset.x, z: myBody.z + offset.z, diveT: myBody.diveT, diveX: myBody.diveX, diveZ: myBody.diveZ, aimYaw: aim.yaw, aimPitch: aim.pitch });
        const f = (isDown('fwd') ? 1 : 0) - (isDown('back') ? 1 : 0), s = (isDown('right') ? 1 : 0) - (isDown('left') ? 1 : 0);
        q.moving = (f !== 0 || s !== 0) && state.ph === 'playing';
        q.moveDot = f !== 0 ? f : 0;
      } else {
        q = sample(p.id, renderT);
        if (!q) continue;
        const sp = Math.hypot(q.vx, q.vz);
        q.moving = sp > 0.6;
        q.moveDot = sp > 0.6 ? (q.vx * Math.sin(q.aimYaw) + q.vz * Math.cos(q.aimYaw)) / sp : 0;
      }
      const r = roster.get(p.id) || { n: '?', lk: 0 };
      q.name = r.n;
      q.gun = q.slot === 1 && q.second >= 0 ? q.second : 0;
      const c = world.character(p.id, r.lk, isMe);
      seen.add(p.id);
      world.pose(c, q, dt, q.speed < 1 ? 0.55 : 1);
      const air = q.diveT >= 0 && q.diveT < P.DiveAir;
      const on = q.alive && (q.bt || air);
      const radius = q.bt ? BUBBLE : DIVE_BUBBLE;
      if (on) bubbleList.push({ x: q.x, z: q.z, r: radius, owner: p.id });
      world.bubble(p.id, q.x, q.z, radius, on);
    }
  }
  for (const id of [...world.players.keys()]) if (!seen.has(id)) { world.removeCharacter(id); world.bubble(id, 0, 0, 1, false); }

  // camera
  steer(dt);
  updateCamera(dt);
  updateAim(renderT);
  const slowView = inGame && camPos && myBody ? bubbleAt(myBody.x, myBody.z) : false;
  $('view').classList.toggle('slow', !!slowView);
  $('grade').classList.toggle('slow', !!slowView);
  S.setSlow(slowView ? 0.62 : 1);
  if (mine && mine.bt && !wasBt) S.play('btIn');
  if (mine && !mine.bt && wasBt) S.play('btOut');
  wasBt = !!(mine && mine.bt);

  stepBullets(dt, renderT);
  if (state) world.setPickups(state.pk || []);
  world.frame(dt, slowView ? 0.3 : 1);
  hud(dt);
  requestAnimationFrame(frame);
}

function updateCamera(dt) {
  const cam = world.camera;
  if (!state || !me || state.ph === 'lobby' || !map) {
    // the menu: a slow drift over the docks at night
    const t = performance.now() / 1000 * 0.05;
    cam.position.set(25 + Math.cos(t) * 30, 13, 20 + Math.sin(t) * 26);
    cam.lookAt(25, 1, 20);
    camPos = { x: cam.position.x, y: cam.position.y, z: cam.position.z };
    return;
  }
  let px, pz, eye;
  if (myBody) { px = myBody.x + offset.x; pz = myBody.z + offset.z; eye = myBody.diveT >= 0 ? 1.05 : 1.6; }
  else if (mine) { px = mine.x; pz = mine.z; eye = 2.6; }
  else return;
  const d = camDir();
  const rx = -Math.cos(yaw), rz = Math.sin(yaw);
  const pivot = { x: px + rx * 0.85, y: eye, z: pz + rz * 0.85 };
  const back = myBody ? 3.4 : 5.5;
  let want = { x: pivot.x - d.x * back, y: pivot.y - d.y * back + 0.25, z: pivot.z - d.z * back };
  // keep the camera out of walls
  const vx = want.x - pivot.x, vy = want.y - pivot.y, vz = want.z - pivot.z, len = Math.hypot(vx, vy, vz);
  const hit = world.ray(pivot.x, pivot.y, pivot.z, vx / len, vy / len, vz / len);
  if (hit < len) { const k = Math.max(0.3, hit - 0.25) / len; want = { x: pivot.x + vx * k, y: pivot.y + vy * k, z: pivot.z + vz * k }; }
  want.y = Math.max(0.35, want.y);
  // rigid: the camera sits exactly where it should every frame (a lagging camera makes the aim feel loose);
  // only its height eases, so a dive does not jolt it
  camPos = { x: want.x, y: camPos && myBody ? camPos.y + (want.y - camPos.y) * Math.min(1, dt * 14) : want.y, z: want.z };
  cam.position.set(camPos.x, camPos.y, camPos.z);
  cam.lookAt(camPos.x + d.x * 10, camPos.y + d.y * 10, camPos.z + d.z * 10);
  // the dive tilts the camera a touch
  if (myBody && myBody.diveT >= 0 && myBody.diveT < P.DiveAir) cam.rotateZ(Math.sin(myBody.diveT / P.DiveAir * Math.PI) * 0.06 * (myBody.diveX * Math.cos(yaw) - myBody.diveZ * Math.sin(yaw) > 0 ? 1 : -1));
}

function stepBullets(dt, renderT) {
  for (let i = bullets.length - 1; i >= 0; i--) {
    const b = bullets[i];
    if (!b.own && renderT < b.start) continue;
    if (!b.started) {
      b.started = true;
      world.muzzle(b.x, b.y, b.z, b.gun === 1);
      const c = world.players.get(b.owner);
      if (c) c.lastShot = world.time;
      if (!bullets.some((q) => q !== b && q.started && q.owner === b.owner && q.gun === 1 && q.travelled < 0.5)) S.play('shot', { gun: b.gun, volume: volumeAt(b.x, b.z) });
    }
    if (!b.mesh) b.mesh = world.bulletMesh();
    const factor = bubbleAt(b.x, b.z) ? SLOW_BULLETS : 1;
    let step = b.sp * dt * factor;
    let finish = null;
    if (b.own && b.travelled + step >= b.maxDist) { step = b.maxDist - b.travelled; finish = { x: b.x + b.dx * step, y: b.y + b.dy * step, z: b.z + b.dz * step, what: b.what }; }
    if (!b.own && b.end && renderT >= b.end.t) finish = b.end;
    b.x += b.dx * step; b.y += b.dy * step; b.z += b.dz * step; b.travelled += step;
    if (finish || b.travelled > 72) {
      if (finish) {
        world.impact(finish.x, finish.y, finish.z, finish.what);
        if (finish.what === 'wall' && Math.random() < 0.35) S.play('ric', { volume: volumeAt(finish.x, finish.z) * 0.8 });
        if (finish.what === 'flesh') S.play('flesh', { volume: volumeAt(finish.x, finish.z) });
      }
      world.scene.remove(b.mesh);
      bullets.splice(i, 1);
      continue;
    }
    b.mesh.position.set(b.x, b.y, b.z);
    b.mesh.lookAt(b.x + b.dx, b.y + b.dy, b.z + b.dz);
    // a longer streak at full speed, a short heat trail when it crawls
    b.mesh.userData.trail.scale.set(1, 1, Math.min(b.travelled, factor < 1 ? 1.2 : 3.5));
  }
}

// ------------------------------------------------------------------ HUD
function hud(dt) {
  hurt = Math.max(0, hurt - dt * 0.8);
  hitmark = Math.max(0, hitmark - dt * 4);
  $('hurt').style.opacity = String(Math.max(hurt, mine && mine.alive && mine.hp < 30 ? 0.35 + Math.sin(performance.now() / 250) * 0.1 : 0));
  $('hitmark').style.opacity = String(hitmark);
  $('hitmark').classList.toggle('head', hitHead);
  if (!state || !me || state.ph === 'lobby') { $('lock').classList.add('hidden'); return; }
  const p = mine;
  const active = state.ph === 'playing' || state.ph === 'countdown';
  $('lock').classList.toggle('hidden', locked() || !active || aimMode === 'cursor');
  const ch = $('crosshair');
  if (aimMode === 'cursor' && cursor.inside) { ch.style.left = cursor.x + 'px'; ch.style.top = cursor.y + 'px'; } else { ch.style.left = ''; ch.style.top = ''; }
  const t = Math.ceil(state.tl);
  $('clock').textContent = state.ph === 'countdown' ? String(t) : Math.floor(t / 60) + ':' + String(t % 60).padStart(2, '0');
  if (p) {
    $('my-kills').textContent = p.kills;
    $('hp-fill').style.width = p.hp + '%';
    $('hp-num').textContent = p.hp;
    $('bt-fill').style.height = Math.round(p.meter * 100) + '%';
    $('bt').classList.toggle('on', p.bt);
    const g = p.slot === 1 && p.second >= 0 ? p.second : 0;
    $('w-name').textContent = GUNS[g].name;
    $('w-ammo').textContent = p.reloading && p.alive ? '…' : String(Math.max(0, p.ammo - firedSeqs.length));
    $('w-hint').textContent = !p.alive ? '' : p.reloading ? 'reloading' : p.second >= 0 ? (p.slot === 0 ? '2: ' + GUNS[p.second].name.toLowerCase() : '1: pistol') : '';
    $('crosshair').style.setProperty('--gap', (7 + GUNS[g].spread * 160 + (myBody && myBody.diveT >= 0 ? 6 : 0)) + 'px');
    $('dead').classList.toggle('hidden', p.alive || state.ph === 'over');
    $('view').classList.toggle('dead', !p.alive && state.ph !== 'over');
    if (!p.alive) { $('dead-by').textContent = deadBy || 'Down.'; $('dead-t').textContent = 'back in ' + p.respawn.toFixed(1) + ' s'; }
  }
  const now = performance.now();
  feed = feed.filter((f) => f.until > now);
  const sig = feed.map((f) => f.html).join('|');
  if ($('feed').dataset.sig !== sig) { $('feed').dataset.sig = sig; $('feed').innerHTML = feed.map((f) => `<div class="${f.me ? 'me' : ''}">${f.html}</div>`).join(''); }
  const showBanner = banner && banner.until > now;
  $('banner').classList.toggle('on', !!showBanner);
  if (showBanner) { $('banner-text').textContent = banner.text; $('banner-sub').textContent = banner.sub; }
  const board = isDown('board') || state.ph === 'over';
  $('board').classList.toggle('hidden', !board);
  if (board) {
    const rows = [...state.ps].sort((a, b) => b.kills - a.kills || a.deaths - b.deaths);
    const html = rows.map((r) => `<tr class="${r.id === me ? 'me' : ''}"><td>${esc(name(r.id))}</td><td>${r.kills}</td><td>${r.deaths}</td></tr>`).join('');
    if ($('board-rows').innerHTML !== html) $('board-rows').innerHTML = html;
  }
}

requestAnimationFrame(frame);
