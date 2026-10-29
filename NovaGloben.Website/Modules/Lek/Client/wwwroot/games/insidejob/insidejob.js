/* Inside Job - the page: safehouse home, the layout editor with the solver gate, raids against other players'
   safehouses (Silent Hour's play rules, from core.js) and replays of raids on your own. Talks to api/insidejob. */
(() => {
  'use strict';
  const { W, H, N, DX, DY } = IJ;
  const $ = (id) => document.getElementById(id);
  const DIRN = ['up', 'right', 'down', 'left'];
  const ARROW = ['↑', '→', '↓', '←'];
  const api = {
    async get(path) { const r = await fetch(LekSite.url('/api/insidejob/') + path, { credentials: 'same-origin', headers: { Accept: 'application/json' } }); return r; },
    async send(method, path, body) {
      return fetch(LekSite.url('/api/insidejob/') + path, { method, credentials: 'same-origin', headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'InsideJob' }, body: body === undefined ? undefined : JSON.stringify(body) });
    }
  };

  let me = null;          // { name, userId, safehouse, raids }
  let myLevel = null;

  // ---------------------------------------------------------------- screens
  function show(name) {
    document.querySelectorAll('.screen').forEach((s) => s.classList.toggle('hidden', s.id !== 'screen-' + name));
    document.querySelectorAll('#tabs button').forEach((b) => b.classList.toggle('on', b.dataset.screen === name));
    if (name === 'targets') loadTargets();
    if (name === 'replays') loadReplays();
    if (name === 'editor') editorOpen();
  }
  document.querySelectorAll('#tabs button').forEach((b) => b.onclick = () => show(b.dataset.screen));
  $('home-design').onclick = () => show('editor');
  $('home-raid').onclick = () => show('targets');

  // ---------------------------------------------------------------- board drawing
  const cx = (c) => c % W * 10 + 5, cy = (c) => (c / W | 0) * 10 + 5;
  function rect(c, cls, inset) { const i = inset || 0.3; return '<rect x="' + (c % W * 10 + i) + '" y="' + ((c / W | 0) * 10 + i) + '" width="' + (10 - 2 * i) + '" height="' + (10 - 2 * i) + '" rx="1" class="' + cls + '"/>'; }

  /** The static picture of a layout: walls, specials, routes, guards at t = 0 with their view. L may be null. */
  function drawLayout(svg, layout, L, extras) {
    let h = '';
    for (let c = 0; c < N; c++) h += rect(c, layout.wall[c] ? 'wall' : 'floor');
    if (L) {
      const uB = new Uint8Array(N), uS = new Uint8Array(N);
      for (let g = 0; g < 3; g++) for (let c = 0; c < N; c++) { uB[c] |= L.seenB[g][0][c]; uS[c] |= L.seenS[g][0][c]; }
      for (let c = 0; c < N; c++) if (uB[c]) h += rect(c, 'vis' + (uS[c] ? ' close' : ''));
    }
    for (const c of layout.exits || []) h += rect(c, 'exfil', 1) + '<text x="' + cx(c) + '" y="' + cy(c) + '" class="et">OUT</text>';
    for (const c of layout.starts || []) h += rect(c, 'start', 1) + '<text x="' + cx(c) + '" y="' + cy(c) + '" class="st">START</text>';
    if (layout.intel >= 0 && layout.intel != null) { const x = cx(layout.intel), y = cy(layout.intel); h += '<rect x="' + (x - 2.2) + '" y="' + (y - 2.8) + '" width="4.4" height="5.6" class="intel"/>'; for (const o of [-1.2, 0, 1.2]) h += '<line x1="' + (x - 1.2) + '" x2="' + (x + 1.2) + '" y1="' + (y + o) + '" y2="' + (y + o) + '" class="intel-l"/>'; }
    (layout.guards || []).forEach((g, gi) => {
      const sel = extras && extras.selected === gi;
      const p = g.path;
      if (p.length > 1) h += '<line x1="' + cx(p[0]) + '" y1="' + cy(p[0]) + '" x2="' + cx(p[p.length - 1]) + '" y2="' + cy(p[p.length - 1]) + '" class="route' + (sel ? ' sel' : '') + '"/>';
      const c = L ? L.gpos[gi][0] : p[0], d = L ? L.gdir[gi][0] : g.dir, x = cx(c), y = cy(c), ax = DX[d], ay = DY[d];
      h += '<polygon class="guard' + (sel ? ' sel' : '') + '" points="' + (x + ax * 4.7) + ',' + (y + ay * 4.7) + ' ' + (x + ax * 2.2 + ay * 2) + ',' + (y + ay * 2.2 + ax * 2) + ' ' + (x + ax * 2.2 - ay * 2) + ',' + (y + ay * 2.2 - ax * 2) + '"/><circle cx="' + x + '" cy="' + y + '" r="2.9" class="guard' + (sel ? ' sel' : '') + '"/>';
    });
    if (extras && extras.pick != null) h += rect(extras.pick, 'pick', 0.3);
    if (extras && extras.cursor != null) h += rect(extras.cursor, 'cursor', 0.8);
    svg.innerHTML = h;
  }

  /** Silent Hour's play picture: the level at a state, with watched squares, operatives, hints. */
  function drawPlay(svg, L, v, opts) {
    const t = v.t, t2 = (t + 1) % L.T, caught = opts.caught, done = opts.done, sel = opts.sel;
    let h = '';
    const uB = new Uint8Array(N), uS = new Uint8Array(N), nB = new Uint8Array(N), nS = new Uint8Array(N);
    for (let g = 0; g < 3; g++) { if (!(v.alive >> g & 1)) continue; for (let c = 0; c < N; c++) { uB[c] |= L.seenB[g][t][c]; uS[c] |= L.seenS[g][t][c]; nB[c] |= L.seenB[g][t2][c]; nS[c] |= L.seenS[g][t2][c]; } }
    for (let c = 0; c < N; c++) {
      h += rect(c, L.wall[c] ? 'wall' : 'floor');
      if (uB[c]) h += rect(c, 'vis' + (uS[c] ? ' close' : ''));
      if (!caught && !done && nB[c]) h += rect(c, 'vnext' + (nS[c] ? ' close' : ''), 1.6);
    }
    L.exits.forEach((c) => { h += rect(c, 'exfil', 1) + '<text x="' + cx(c) + '" y="' + cy(c) + '" class="et">OUT</text>'; });
    if (!v.intel) { const x = cx(L.intel), y = cy(L.intel); h += '<rect x="' + (x - 2.2) + '" y="' + (y - 2.8) + '" width="4.4" height="5.6" class="intel"/>'; for (const o of [-1.2, 0, 1.2]) h += '<line x1="' + (x - 1.2) + '" x2="' + (x + 1.2) + '" y1="' + (y + o) + '" y2="' + (y + o) + '" class="intel-l"/>'; }
    for (let g = 0; g < 3; g++) { if (!(v.alive >> g & 1)) continue; const p = L.guards[g].path; if (p.length > 1) h += '<line x1="' + cx(p[0]) + '" y1="' + cy(p[0]) + '" x2="' + cx(p[p.length - 1]) + '" y2="' + cy(p[p.length - 1]) + '" class="route"/>'; }
    if (!caught && !done && sel >= 0) {
      const c = sel ? v.b : v.a;
      for (let d = 0; d < 4; d++) { const x = c % W + DX[d], y = (c / W | 0) + DY[d]; if (x < 0 || y < 0 || x >= W || y >= H) continue; const n = y * W + x; if (L.wall[n] || n === v.a || n === v.b) continue; let occ = false; for (let g = 0; g < 3; g++) if (v.alive >> g & 1 && L.gpos[g][t] === n) occ = true; if (!occ) h += '<circle cx="' + cx(n) + '" cy="' + cy(n) + '" r="1" class="dot"/>'; }
    }
    if (opts.hint >= 0) h += rect(opts.hint, 'hint', 0.8);
    let canT = false;
    for (let g = 0; g < 3; g++) {
      if (!(v.alive >> g & 1)) continue;
      const p = L.gpos[g][t], d = L.gdir[g][t], x = cx(p), y = cy(p), ax = DX[d], ay = DY[d];
      h += '<polygon class="guard" points="' + (x + ax * 4.7) + ',' + (y + ay * 4.7) + ' ' + (x + ax * 2.2 + ay * 2) + ',' + (y + ay * 2.2 + ax * 2) + ' ' + (x + ax * 2.2 - ay * 2) + ',' + (y + ay * 2.2 - ax * 2) + '"/><circle cx="' + x + '" cy="' + y + '" r="2.9" class="guard"/>';
      if (!caught && !done && v.alive === 7 && adj(v.a, p)) { canT = true; h += '<circle cx="' + x + '" cy="' + y + '" r="4.3" class="tkd"/>'; }
    }
    if (caught) { const p = L.gpos[caught.g][t], o = caught.who ? v.b : v.a; h += '<line x1="' + cx(p) + '" y1="' + cy(p) + '" x2="' + cx(o) + '" y2="' + cy(o) + '" class="cline"/><circle cx="' + cx(o) + '" cy="' + cy(o) + '" r="4.4" class="cring"/>'; }
    h += '<rect x="' + (cx(v.a) - 3) + '" y="' + (cy(v.a) - 3) + '" width="6" height="6" rx="1.2" class="beret"/><text x="' + cx(v.a) + '" y="' + (cy(v.a) + .2) + '" class="ut">B</text>';
    h += '<rect x="' + (cx(v.b) - 2.6) + '" y="' + (cy(v.b) - 2.6) + '" width="5.2" height="5.2" class="spy" transform="rotate(45 ' + cx(v.b) + ' ' + cy(v.b) + ')"/><text x="' + cx(v.b) + '" y="' + (cy(v.b) + .2) + '" class="ut">S</text>';
    if (!done && sel >= 0) { const c = sel ? v.b : v.a; h += '<circle cx="' + cx(c) + '" cy="' + cy(c) + '" r="4.5" class="sel"/>'; }
    svg.innerHTML = h;
    return canT;
  }
  const adj = (p, q) => Math.abs(p % W - q % W) + Math.abs((p / W | 0) - (q / W | 0)) === 1;
  function cellAt(svg, e) {
    const r = svg.getBoundingClientRect(), x = Math.floor((e.clientX - r.left) / r.width * W), y = Math.floor((e.clientY - r.top) / r.height * H);
    return x < 0 || y < 0 || x >= W || y >= H ? -1 : y * W + x;
  }

  // ---------------------------------------------------------------- home
  async function loadMe() {
    const r = await api.get('me');
    if (r.status === 401 || r.status === 403) { $('screen-guest').classList.remove('hidden'); $('screen-home').classList.add('hidden'); $('tabs').classList.add('hidden'); return false; }
    me = await r.json();
    renderHome();
    return true;
  }
  function renderHome() {
    const s = me.safehouse || {};
    $('home-loot').textContent = s.layout ? s.loot.toLocaleString() : '–';
    $('home-par').textContent = s.layout ? s.par : '–';
    $('home-suffered').textContent = s.raidsSuffered || 0;
    $('home-defended').textContent = s.defended || 0;
    $('home-earned').textContent = (s.earned || 0).toLocaleString();
    if (s.layout) {
      const c = IJ.compile(s.layout);
      myLevel = c.level || null;
      drawLayout($('home-board'), s.layout, myLevel);
      $('home-layout-note').textContent = 'Par ' + s.par + ': the solver cracks it in ' + s.par + ' turns. ' + (me.raids && me.raids.length ? me.raids.length + ' raid' + (me.raids.length > 1 ? 's' : '') + ' on record.' : 'Nobody has raided you yet.');
    } else {
      $('home-board').innerHTML = '';
      $('home-layout-note').textContent = 'No safehouse yet. Pick a starter in Design and save it, or your vault cannot be raided and you cannot raid.';
    }
  }

  // ---------------------------------------------------------------- editor
  const ed = { layout: null, tool: 'wall', pending: null, selected: -1, rated: null };
  const blank = () => ({ wall: new Array(N).fill(0), starts: [], exits: [], intel: -1, guards: [] });
  function editorOpen() {
    if (!ed.layout) ed.layout = me && me.safehouse && me.safehouse.layout ? clone(me.safehouse.layout) : clone(IJ_STARTERS.STARTERS.townhouse);
    const sel = $('starter');
    if (sel.options.length === 1) for (const k of Object.keys(IJ_STARTERS.STARTERS)) { const o = document.createElement('option'); o.value = k; o.textContent = k; sel.appendChild(o); }
    editorDraw();
  }
  const clone = (o) => JSON.parse(JSON.stringify(o));
  function editorDraw() {
    const c = IJ.compile(ed.layout);
    drawLayout($('ed-board'), ed.layout, c.level || null, { selected: ed.selected, pick: ed.pending });
    const list = $('guard-list');
    list.innerHTML = '';
    ed.layout.guards.forEach((g, i) => {
      const row = document.createElement('div');
      const label = document.createElement('span');
      label.textContent = 'Guard ' + (i + 1) + ': ' + (g.path.length === 1 ? 'sentry facing ' + DIRN[g.dir] : g.path.length + ' squares, starts ' + DIRN[g.dir] + ', phase ' + g.phase);
      row.appendChild(label);
      if (g.path.length === 1) {
        for (let d = 0; d < 4; d++) { const b = document.createElement('button'); b.textContent = ARROW[d]; b.onclick = () => { g.dir = d; ed.rated = null; editorDraw(); }; row.appendChild(b); }
      } else {
        const rev = document.createElement('button'); rev.textContent = 'reverse'; rev.onclick = () => { g.path.reverse(); g.dir = (g.dir + 2) % 4; ed.rated = null; editorDraw(); }; row.appendChild(rev);
        const ph = document.createElement('button'); ph.textContent = 'phase +'; ph.onclick = () => { g.phase = ((g.phase | 0) + 1) % (2 * (g.path.length - 1)); ed.rated = null; editorDraw(); }; row.appendChild(ph);
      }
      const del = document.createElement('button'); del.textContent = 'remove'; del.onclick = () => { ed.layout.guards.splice(i, 1); ed.selected = -1; ed.rated = null; editorDraw(); }; row.appendChild(del);
      list.appendChild(row);
    });
    $('ed-save').disabled = !ed.rated;
    $('ed-watch').disabled = !ed.rated;
    if (!ed.rated) setMsg('ed-msg', c.error ? c.error : 'Looks valid. Verify to get your par.', !c.error);
  }
  document.querySelectorAll('#palette button').forEach((b) => b.onclick = () => { ed.tool = b.dataset.tool; ed.pending = null; document.querySelectorAll('#palette button').forEach((x) => x.classList.toggle('on', x === b)); editorDraw(); });
  $('ed-load').onclick = () => { const k = $('starter').value; if (!k) ed.layout = me.safehouse && me.safehouse.layout ? clone(me.safehouse.layout) : blank(); else ed.layout = clone(IJ_STARTERS.STARTERS[k]); ed.rated = null; ed.pending = null; ed.selected = -1; editorDraw(); };
  $('ed-board').addEventListener('click', (e) => {
    const c = cellAt($('ed-board'), e); if (c < 0) return;
    const L = ed.layout; ed.rated = null;
    const clearSpecial = (cell) => { L.starts = L.starts.filter((x) => x !== cell); L.exits = L.exits.filter((x) => x !== cell); if (L.intel === cell) L.intel = -1; L.guards = L.guards.filter((g) => !g.path.includes(cell)); };
    switch (ed.tool) {
      case 'wall': clearSpecial(c); L.wall[c] = 1; break;
      case 'floor': L.wall[c] = 0; break;
      case 'start': clearSpecial(c); L.wall[c] = 0; L.starts.push(c); while (L.starts.length > 2) L.starts.shift(); break;
      case 'exit': clearSpecial(c); L.wall[c] = 0; L.exits.push(c); while (L.exits.length > 2) L.exits.shift(); break;
      case 'vault': clearSpecial(c); L.wall[c] = 0; L.intel = c; break;
      case 'erase-guard': L.guards = L.guards.filter((g) => !g.path.includes(c)); break;
      case 'guard': {
        if (L.wall[c]) { setMsg('ed-msg', 'Guards walk on floor, not through walls.'); return; }
        if (ed.pending == null) { ed.pending = c; setMsg('ed-msg', 'Now click the last square of the route (the same square for a sentry).', true); editorDraw(); return; }
        const a = ed.pending; ed.pending = null;
        const ax = a % W, ay = a / W | 0, bx = c % W, by = c / W | 0;
        if (ax !== bx && ay !== by) { setMsg('ed-msg', 'A route is a straight line.'); editorDraw(); return; }
        const n = Math.abs(ax - bx) + Math.abs(ay - by) + 1;
        if (n > 7) { setMsg('ed-msg', 'A route is at most 7 squares.'); editorDraw(); return; }
        const dir = n === 1 ? 2 : (bx > ax ? 1 : bx < ax ? 3 : by > ay ? 2 : 0);
        const path = []; for (let i = 0; i < n; i++) path.push((ay + DY[dir] * i) * W + ax + DX[dir] * i);
        if (L.guards.length >= 3) L.guards.shift();
        L.guards.push({ path, dir, phase: 0 });
        ed.selected = L.guards.length - 1;
        break;
      }
    }
    editorDraw();
  });
  $('ed-verify').onclick = () => {
    const r = IJ.rate(ed.layout);
    if (r.error) { ed.rated = null; setMsg('ed-msg', r.error); editorDraw(); return; }
    ed.rated = r;
    editorDraw();
    setMsg('ed-msg', 'Beatable. Par ' + r.par + ' turns (the plain walk is ' + r.free + '; the guards add ' + (r.par - r.free) + '). Raiders must beat ' + r.par + ' to take 30%.', true);
  };
  $('ed-save').onclick = async () => {
    if (!ed.rated) return;
    const r = await api.send('PUT', 'base', { layout: ed.layout, par: ed.rated.par, solution: ed.rated.solution });
    if (!r.ok) { setMsg('ed-msg', 'The server refused the layout (' + r.status + ').'); return; }
    const d = await r.json();
    me.safehouse = d.safehouse;
    renderHome();
    setMsg('ed-msg', 'Saved. Your safehouse is live with par ' + ed.rated.par + '.', true);
  };
  $('ed-watch').onclick = () => { if (ed.rated) runReplay($('ed-board'), ed.rated.level, ed.rated.solution, 'The solver, ' + ed.rated.par + ' turns', () => editorDraw()); };

  // ---------------------------------------------------------------- targets and raids
  async function loadTargets() {
    const r = await api.get('targets');
    if (!r.ok) { setMsg('targets-msg', 'Could not load targets.'); return; }
    const d = await r.json();
    const body = $('targets').querySelector('tbody');
    body.innerHTML = '';
    if (!d.targets.length) setMsg('targets-msg', 'Nobody else has a safehouse yet. Invite a friend to register and design one.', true);
    else setMsg('targets-msg', me.safehouse && me.safehouse.layout ? '' : 'Save a safehouse of your own first: raiders need something to lose.');
    for (const t of d.targets) {
      const tr = document.createElement('tr');
      tr.innerHTML = '<td></td><td></td><td></td><td></td><td></td>';
      tr.children[0].textContent = t.name; tr.children[1].textContent = t.par; tr.children[2].textContent = t.loot.toLocaleString(); tr.children[3].textContent = t.raidsSuffered + ' (' + t.defended + ' caught)';
      const b = document.createElement('button'); b.className = 'small primary'; b.textContent = 'Raid'; b.disabled = !(me.safehouse && me.safehouse.layout);
      b.onclick = () => startRaid(t.userId);
      tr.children[4].appendChild(b);
      body.appendChild(tr);
    }
  }

  const raid = { target: null, L: null, hist: [], caught: null, sel: 0, alarms: 0, done: false, posted: false };
  const cur = () => raid.hist[raid.hist.length - 1];
  const won = () => { const s = cur(); return !!(raid.L && s.intel && raid.L.isExit[s.a] && raid.L.isExit[s.b]); };
  async function startRaid(targetId) {
    const r = await api.get('base/' + targetId);
    if (!r.ok) { setMsg('targets-msg', 'That safehouse is gone.'); return; }
    const t = await r.json();
    const c = IJ.compile(t.layout);
    if (c.error) { setMsg('targets-msg', 'That safehouse does not compile: ' + c.error); return; }
    raid.target = t; raid.L = c.level; raid.hist = [{ a: c.level.starts[0], b: c.level.starts[1], t: 0, alive: 7, intel: 0, act: -1 }];
    raid.caught = null; raid.sel = 0; raid.alarms = 0; raid.done = false; raid.posted = false;
    $('raid-title').textContent = t.name + "'s safehouse";
    $('raid-par').textContent = t.par; $('raid-loot').textContent = t.loot.toLocaleString();
    $('raid-result').classList.add('hidden');
    show('raid');
    idle(); renderRaid();
  }
  function say(msg, cls) { $('status').textContent = msg; $('status').className = cls || ''; }
  function idle() { if (won()) return; const s = cur(); say((raid.sel ? 'Spy' : 'Beret') + ' selected. ' + (s.intel ? 'Loot in hand: get both to the OUT squares.' : 'Walk the Spy onto the vault.')); }
  function doAct(act) {
    if (!raid.L || raid.caught || raid.done) return;
    const s = cur(), r = IJ.apply(raid.L, s.a, s.b, s.t, s.alive, s.intel, act);
    if (r === 1) { say(act > 8 ? 'The Beret has to be next to the guard, and he gets one takedown per raid.' : 'That square is blocked.', 'bad'); return; }
    if (r === 2) { raid.caught = { a: IJ.out.a, b: IJ.out.b, t: IJ.out.t, alive: IJ.out.alive, intel: IJ.out.intel, g: IJ.out.guard, who: IJ.out.who, late: IJ.out.t !== s.t }; raid.alarms++; say('Spotted. A guard saw the ' + (IJ.out.who ? 'Spy' : 'Beret') + (raid.caught.late ? ' after the patrols stepped.' : ' as he moved.') + ' Undo and try another way, or abort.', 'bad'); renderRaid(); return; }
    raid.hist.push({ a: IJ.out.a, b: IJ.out.b, t: (s.t + 1) % raid.L.T, alive: IJ.out.alive, intel: IJ.out.intel, act });
    if (won()) finishRaid(false); else idle();
    renderRaid();
  }
  function undo() { if (!raid.L || raid.done) return; if (raid.caught) raid.caught = null; else if (raid.hist.length > 1) raid.hist.pop(); idle(); renderRaid(); }
  function restart() { if (!raid.L || raid.done) return; raid.caught = null; raid.hist = [raid.hist[0]]; idle(); renderRaid(); }
  function takeout() { if (!raid.L) return; const s = cur(); for (let g = 0; g < 3; g++) if (s.alive >> g & 1 && adj(s.a, raid.L.gpos[g][s.t])) { doAct(9 + g); return; } doAct(9); }
  function renderRaid() {
    if (!raid.L) return;
    const v = raid.caught || cur();
    const canT = drawPlay($('raid-board'), raid.L, v, { caught: raid.caught, done: raid.done || won(), sel: raid.sel, hint: -1 });
    $('raid-turns').textContent = raid.hist.length - 1;
    $('opB').setAttribute('aria-pressed', raid.sel === 0); $('opS').setAttribute('aria-pressed', raid.sel === 1);
    $('tkd').disabled = !canT; $('wait').disabled = !!raid.caught || raid.done; $('undo').disabled = raid.done || (raid.hist.length < 2 && !raid.caught);
  }
  async function finishRaid(caught) {
    raid.done = true;
    const acts = raid.hist.slice(1).map((h) => h.act);
    const turns = acts.length, par = raid.target.par;
    say(caught ? 'You pulled out. The crew pays for it.' : (turns <= par ? 'Perfect raid' : turns <= par + 3 ? 'Clean raid' : 'Out, but slow') + ': ' + turns + ' turns against par ' + par + '.', caught ? 'bad' : 'good');
    const r = await api.send('POST', 'raid', { targetId: raid.target.userId, acts, caught, alarms: raid.alarms });
    const box = $('raid-result'); box.classList.remove('hidden');
    if (!r.ok) { $('raid-outcome').textContent = 'Not recorded'; $('raid-detail').textContent = 'The server refused the raid (' + r.status + ').'; return; }
    const d = await r.json();
    raid.posted = true;
    const names = { perfect: 'Perfect raid', clean: 'Clean raid', slow: 'Slow raid', caught: 'Caught' };
    $('raid-outcome').textContent = names[d.outcome] || d.outcome;
    $('raid-detail').textContent = d.loot > 0 ? 'You took ' + d.loot.toLocaleString() + ' from the vault. Yours is now ' + d.myLoot.toLocaleString() + '.' : d.loot < 0 ? 'You lost ' + (-d.loot).toLocaleString() + ' to ' + raid.target.name + '. Yours is now ' + d.myLoot.toLocaleString() + '.' : 'The vault was empty.';
    renderRaid();
    loadMe();
  }
  $('abort').onclick = () => { if (!raid.L || raid.done) return; if (confirm('Abort the raid? It counts as caught and costs 10% of your vault.')) finishRaid(true); };
  $('raid-done').onclick = () => { raid.L = null; show('targets'); };
  $('opB').onclick = () => { raid.sel = 0; if (!raid.caught) idle(); renderRaid(); };
  $('opS').onclick = () => { raid.sel = 1; if (!raid.caught) idle(); renderRaid(); };
  $('wait').onclick = () => doAct(8); $('tkd').onclick = takeout; $('undo').onclick = undo; $('restart').onclick = restart;
  $('raid-board').addEventListener('click', (e) => {
    if (!raid.L || raid.done || raid.caught) return;
    const c = cellAt($('raid-board'), e); if (c < 0) return;
    const s = cur();
    if (c === s.a) { raid.sel = 0; idle(); renderRaid(); return; } if (c === s.b) { raid.sel = 1; idle(); renderRaid(); return; }
    for (let g = 0; g < 3; g++) if (s.alive >> g & 1 && raid.L.gpos[g][s.t] === c) { doAct(9 + g); return; }
    const m = raid.sel ? s.b : s.a;
    if (adj(m, c)) { doAct(raid.sel * 4 + (c === m - W ? 0 : c === m + 1 ? 1 : c === m + W ? 2 : 3)); return; }
    const other = raid.sel ? s.a : s.b;
    if (adj(other, c)) { raid.sel = 1 - raid.sel; doAct(raid.sel * 4 + (c === other - W ? 0 : c === other + 1 ? 1 : c === other + W ? 2 : 3)); }
  });
  document.addEventListener('keydown', (e) => {
    if (!raid.L || $('screen-raid').classList.contains('hidden')) return;
    if (e.ctrlKey || e.metaKey || e.altKey) return; const k = e.key.toLowerCase(), tag = e.target.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
    const d = { arrowup: 0, w: 0, arrowright: 1, d: 1, arrowdown: 2, s: 2, arrowleft: 3, a: 3 }[k];
    if (d !== undefined) { e.preventDefault(); doAct(raid.sel * 4 + d); return; }
    if (k === ' ') { if (tag === 'BUTTON') return; e.preventDefault(); doAct(8); }
    else if (k === '1') { raid.sel = 0; renderRaid(); } else if (k === '2') { raid.sel = 1; renderRaid(); }
    else if (k === 'z' || k === 'backspace') { e.preventDefault(); undo(); }
    else if (k === 'r') restart(); else if (k === 't') takeout();
  });

  // ---------------------------------------------------------------- replays
  let rp = { timer: null, L: null, acts: [], i: 0, svg: null, title: '', after: null };
  function runReplay(svg, L, acts, title, after) {
    stopReplay();
    rp = { timer: null, L, acts, i: 0, svg, title, after };
    rpDraw();
    rp.timer = setInterval(() => { if (rp.i >= rp.acts.length) { stopReplay(); return; } rp.i++; rpDraw(); }, 450);
  }
  function stopReplay() { if (rp.timer) clearInterval(rp.timer); rp.timer = null; $('rp-play').textContent = 'Play'; }
  function rpDraw() {
    const r = IJ.replay(rp.L, rp.acts.slice(0, rp.i));
    const v = r.ok ? r.state : null;
    if (!v) { // caught at this step: show the state before, flagged
      const before = IJ.replay(rp.L, rp.acts.slice(0, Math.max(0, rp.i - 1)));
      drawPlay(rp.svg, rp.L, before.state, { caught: null, done: true, sel: -1, hint: -1 });
    } else drawPlay(rp.svg, rp.L, v, { caught: null, done: rp.i >= rp.acts.length, sel: -1, hint: -1 });
    if (rp.svg === $('replay-board')) { $('replay-title').textContent = rp.title; $('rp-step').textContent = rp.i + ' / ' + rp.acts.length + ' turns'; }
    if (rp.i >= rp.acts.length && rp.after && rp.svg !== $('replay-board')) { setTimeout(() => { if (rp.i >= rp.acts.length) rp.after(); }, 1200); }
  }
  $('rp-prev').onclick = () => { stopReplay(); rp.i = Math.max(0, rp.i - 1); rpDraw(); };
  $('rp-next').onclick = () => { stopReplay(); rp.i = Math.min(rp.acts.length, rp.i + 1); rpDraw(); };
  $('rp-play').onclick = () => { if (rp.timer) { stopReplay(); return; } if (rp.i >= rp.acts.length) rp.i = 0; $('rp-play').textContent = 'Pause'; rp.timer = setInterval(() => { if (rp.i >= rp.acts.length) { stopReplay(); return; } rp.i++; rpDraw(); }, 450); };
  async function loadReplays() {
    const r = await api.get('me');
    if (!r.ok) return;
    me = await r.json();
    const list = $('replay-list'); list.innerHTML = '';
    if (!me.raids || !me.raids.length) { setMsg('replay-msg', 'No raids on your safehouse yet.', true); $('replay-board').innerHTML = ''; $('replay-title').textContent = ''; return; }
    setMsg('replay-msg', '');
    me.raids.forEach((raidLog, i) => {
      const li = document.createElement('li');
      const left = document.createElement('span'); left.textContent = raidLog.raider + ' · ' + new Date(raidLog.when).toLocaleString();
      const right = document.createElement('span'); right.className = 'o-' + raidLog.outcome; right.textContent = raidLog.outcome + (raidLog.loot ? ' · ' + (raidLog.loot > 0 ? '-' : '+') + Math.abs(raidLog.loot) : '') + ' · ' + raidLog.turns + ' / ' + raidLog.par;
      li.appendChild(left); li.appendChild(right);
      li.onclick = () => {
        document.querySelectorAll('#replay-list li').forEach((x) => x.classList.toggle('on', x === li));
        const c = IJ.compile(raidLog.layout);
        if (c.error) { setMsg('replay-msg', 'That layout no longer compiles.'); return; }
        runReplay($('replay-board'), c.level, raidLog.acts, raidLog.raider + ', ' + raidLog.outcome + ', ' + raidLog.turns + ' turns against par ' + raidLog.par);
      };
      list.appendChild(li);
      if (i === 0) li.click();
    });
  }

  function setMsg(id, text, ok) { const el = $(id); el.textContent = text || ''; el.classList.toggle('ok', !!ok); }

  loadMe();
})();
