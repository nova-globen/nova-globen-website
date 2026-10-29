/* Inside Job - the rules and the solver, shared by the safehouse editor, the raid, the replay and the verifier.
   This is Silent Hour's core (same grid, guards, view cones, turn order, BFS) plus compile(), which builds a level
   from a player's layout instead of from a seed. Keep apply() identical to Silent Hour's: both games are one rulebook.
   Loads in a browser (window.IJ) and in Node (module.exports) so tools/check-insidejob.mjs can verify layouts. */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.IJ = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';
  const W = 9, H = 9, N = 81, DX = [0, 1, 0, -1], DY = [-1, 0, 1, 0], RANGE = 4, GUARDS = 3;
  const gcd = (a, b) => b ? gcd(b, a % b) : a, lcm = (a, b) => a / gcd(a, b) * b;

  function dists(wall, from) {
    const d = new Int16Array(N).fill(-1), q = [from]; d[from] = 0;
    for (let h = 0; h < q.length; h++) {
      const c = q[h], x = c % W, y = c / W | 0;
      for (let k = 0; k < 4; k++) {
        const nx = x + DX[k], ny = y + DY[k];
        if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
        const n = ny * W + nx;
        if (wall[n] || d[n] >= 0) continue;
        d[n] = d[c] + 1; q.push(n);
      }
    }
    return d;
  }

  function cone(wall, c, d, B, S) {
    let x = c % W, y = c / W | 0;
    for (let k = 1; k <= RANGE; k++) {
      x += DX[d]; y += DY[d];
      if (x < 0 || y < 0 || x >= W || y >= H || wall[y * W + x]) break;
      B[y * W + x] = 1;
      if (k === 1) S[y * W + x] = 1;
      if (k >= 2) for (const s of [-1, 1]) {
        const lx = x + s * DY[d], ly = y + s * DX[d];
        if (lx >= 0 && ly >= 0 && lx < W && ly < H && !wall[ly * W + lx]) B[ly * W + lx] = 1;
      }
    }
  }

  /**
   * A layout is what the editor saves: { wall: [81 x 0|1], starts: [c, c], exits: [c, c], intel: c,
   * guards: [{ path: [c, ...], dir: 0..3, phase: n }] } with exactly three guards; a one-cell path is a sentry with
   * a fixed facing, a longer path is a straight line walked back and forth. Returns { level } or { error }.
   */
  function compile(layout) {
    const bad = (error) => ({ error });
    if (!layout || !Array.isArray(layout.wall) || layout.wall.length !== N) return bad('The layout needs 81 cells.');
    const wall = new Uint8Array(N); for (let i = 0; i < N; i++) wall[i] = layout.wall[i] ? 1 : 0;
    const cell = (c) => Number.isInteger(c) && c >= 0 && c < N;
    const starts = layout.starts || [], exits = layout.exits || [], intel = layout.intel;
    if (starts.length !== 2 || !starts.every(cell) || starts[0] === starts[1]) return bad('Place two start squares.');
    if (exits.length !== 2 || !exits.every(cell) || exits[0] === exits[1]) return bad('Place two OUT squares.');
    if (!cell(intel)) return bad('Place the vault.');
    const special = [...starts, ...exits, intel];
    if (new Set(special).size !== special.length) return bad('Start, OUT and vault squares must all differ.');
    if (special.some((c) => wall[c])) return bad('Start, OUT and vault squares cannot be walls.');
    const guards = layout.guards || [];
    if (guards.length !== GUARDS) return bad('Place exactly three guards.');
    const used = new Set(special);
    for (const g of guards) {
      if (!g || !Array.isArray(g.path) || g.path.length < 1 || g.path.length > 7 || !g.path.every(cell)) return bad('A guard route is 1 to 7 squares.');
      if (!Number.isInteger(g.dir) || g.dir < 0 || g.dir > 3) return bad('A guard needs a facing.');
      for (let i = 0; i < g.path.length; i++) {
        const c = g.path[i];
        if (wall[c]) return bad('A guard route crosses a wall.');
        if (used.has(c)) return bad('Guard routes cannot cross each other or the special squares.');
        if (i > 0) {
          const p = g.path[i - 1], d = c - p;
          const step = d === -W ? 0 : d === 1 ? 1 : d === W ? 2 : d === -1 ? 3 : -1;
          if (step < 0 || (i > 1 && step !== g._step)) return bad('A guard route must be a straight line of neighbouring squares.');
          if (step === 1 && c % W === 0) return bad('A guard route wraps around the edge.');
          if (step === 3 && p % W === 0) return bad('A guard route wraps around the edge.');
          g._step = step;
        }
      }
      g.path.forEach((c) => used.add(c));
      if (g.path.length === 1) {
        // a sentry needs three clear squares ahead, as in Silent Hour
        let x = g.path[0] % W, y = g.path[0] / W | 0;
        for (let k = 1; k <= 3; k++) { x += DX[g.dir]; y += DY[g.dir]; if (x < 0 || y < 0 || x >= W || y >= H || wall[y * W + x]) return bad('A sentry needs three clear squares in front of him.'); }
      }
    }
    const dS = dists(wall, starts[0]);
    let floor = 0; for (let c = 0; c < N; c++) if (!wall[c]) floor++;
    if (floor < 30) return bad('Too many walls: at least 30 floor squares.');
    if (dS[starts[1]] < 0 || dS[exits[0]] < 0 || dS[exits[1]] < 0 || dS[intel] < 0) return bad('The vault and the OUT squares must be reachable from the start.');
    for (const g of guards) for (const c of g.path) if (dS[c] < 0) return bad('A guard stands in a sealed-off area.');
    const dE = dists(wall, exits[0]);
    if (dS[intel] < 3 || dE[intel] < 3) return bad('The vault must be at least three steps from the start and from the OUT squares.');

    const L = { wall, starts: starts.slice(), exits: exits.slice(), intel, guards: guards.map((g) => ({ path: g.path.slice(), dir: g.dir, phase: g.path.length === 1 ? 0 : ((g.phase | 0) % (2 * (g.path.length - 1)) + 2 * (g.path.length - 1)) % (2 * (g.path.length - 1)) })), full: 7, isExit: new Uint8Array(N) };
    exits.forEach((c) => L.isExit[c] = 1);
    let T = 1; const per = L.guards.map((g) => g.path.length === 1 ? 1 : 2 * (g.path.length - 1)); per.forEach((p) => T = lcm(T, p));
    if (T > 24) return bad('Those patrol lengths do not fit together (the cycle must be at most 24 turns).');
    L.T = T; L.gpos = []; L.gdir = []; L.seenB = []; L.seenS = [];
    L.guards.forEach((g, gi) => {
      const P = per[gi], n = g.path.length, pos = [], dir = [], sb = [], ss = [];
      for (let t = 0; t < T; t++) {
        let c, d;
        if (n === 1) { c = g.path[0]; d = g.dir; }
        else {
          const k = (t + g.phase) % P, k2 = (k + 1) % P, i = k < n ? k : P - k, i2 = k2 < n ? k2 : P - k2;
          c = g.path[i]; const df = g.path[i2] - c; d = df === -W ? 0 : df === 1 ? 1 : df === W ? 2 : 3;
        }
        const B = new Uint8Array(N), S = new Uint8Array(N); cone(wall, c, d, B, S);
        pos.push(c); dir.push(d); sb.push(B); ss.push(S);
      }
      L.gpos.push(pos); L.gdir.push(dir); L.seenB.push(sb); L.seenS.push(ss);
    });
    for (let t = 0; t < T; t++) for (let g = 0; g < GUARDS; g++) if (L.seenB[g][t][starts[0]] || L.seenB[g][t][starts[1]]) return bad('A guard can see a start square. Starts must never be in view.');
    const dSp = dists(wall, starts[1]), dI = dists(wall, intel), dB = dists(wall, starts[0]);
    L.free = dSp[intel] + Math.min(dI[exits[0]] + dB[exits[1]], dI[exits[1]] + dB[exits[0]]);
    return { level: L };
  }

  // One rule function shared by the game and the solver. Returns 0 ok, 1 illegal, 2 caught; results in out.
  const out = { a: 0, b: 0, alive: 0, intel: 0, guard: 0, who: 0, t: 0 };
  function apply(L, a, b, t, alive, intel, act) {
    let na = a, nb = b, nal = alive, nin = intel;
    if (act < 8) {
      const who = act >> 2, d = act & 3, c = who ? b : a, x = c % W + DX[d], y = (c / W | 0) + DY[d];
      if (x < 0 || y < 0 || x >= W || y >= H) return 1;
      const n = y * W + x; if (L.wall[n] || n === (who ? a : b)) return 1;
      for (let g = 0; g < GUARDS; g++) if (alive >> g & 1 && L.gpos[g][t] === n) return 1;
      if (who) { nb = n; if (n === L.intel) nin = 1; } else na = n;
    } else if (act > 8) {
      const g = act - 9; if (alive !== L.full) return 1; const p = L.gpos[g][t];
      if (Math.abs(p % W - a % W) + Math.abs((p / W | 0) - (a / W | 0)) !== 1) return 1; nal = alive & ~(1 << g);
    }
    out.a = na; out.b = nb; out.alive = nal; out.intel = nin;
    for (let k = 0; k < 2; k++) {
      const tt = k ? (t + 1) % L.T : t;
      for (let g = 0; g < GUARDS; g++) {
        if (!(nal >> g & 1)) continue; const p = L.gpos[g][tt];
        if (p === na || L.seenB[g][tt][na]) { out.guard = g; out.who = 0; out.t = tt; return 2; }
        if (p === nb || L.seenS[g][tt][nb]) { out.guard = g; out.who = 1; out.t = tt; return 2; }
      }
    }
    return 0;
  }

  let VIS, PAR, PACT, Q;
  function solve(L, a0, b0, t0, al0, in0) {
    const T = L.T;
    if (!VIS) { VIS = new Uint8Array(N * N * 24 * 16); PAR = new Int32Array(VIS.length); PACT = new Uint8Array(VIS.length); Q = new Int32Array(VIS.length); }
    VIS.fill(0, 0, N * N * T * 16);
    const enc = (a, b, t, al, i) => (((a * N + b) * T + t) * 8 + al) * 2 + i, s0 = enc(a0, b0, t0, al0, in0);
    let qh = 0, qt = 0; Q[qt++] = s0; VIS[s0] = 1; PAR[s0] = -1;
    while (qh < qt) {
      const s = Q[qh++]; let v = s; const i = v & 1; v >>= 1; const al = v & 7; v >>= 3; const t = v % T; v = (v / T) | 0; const b = v % N, a = (v / N) | 0;
      for (let act = 0; act < 12; act++) {
        if (apply(L, a, b, t, al, i, act) !== 0) continue;
        const ns = enc(out.a, out.b, (t + 1) % T, out.alive, out.intel);
        if (VIS[ns]) continue; VIS[ns] = 1; PAR[ns] = s; PACT[ns] = act;
        if (out.intel && L.isExit[out.a] && L.isExit[out.b]) { const path = []; let c = ns; while (PAR[c] !== -1) { path.push(PACT[c]); c = PAR[c]; } return path.reverse(); }
        Q[qt++] = ns;
      }
    }
    return null;
  }

  /** Replays a list of actions from the start; returns { ok, turns, state } or { ok: false, at, reason }. */
  function replay(L, acts) {
    let s = { a: L.starts[0], b: L.starts[1], t: 0, alive: 7, intel: 0 };
    for (let i = 0; i < acts.length; i++) {
      const r = apply(L, s.a, s.b, s.t, s.alive, s.intel, acts[i]);
      if (r !== 0) return { ok: false, at: i, reason: r === 1 ? 'illegal' : 'caught' };
      s = { a: out.a, b: out.b, t: (s.t + 1) % L.T, alive: out.alive, intel: out.intel };
    }
    return { ok: true, turns: acts.length, state: s, won: !!(s.intel && L.isExit[s.a] && L.isExit[s.b]) };
  }

  /** The vault par: the solver's shortest raid, or null when the layout cannot be beaten. */
  function rate(layout) {
    const c = compile(layout);
    if (c.error) return c;
    const sol = solve(c.level, c.level.starts[0], c.level.starts[1], 0, 7, 0);
    if (!sol) return { error: 'No crew can beat this layout. Loosen a patrol or open a path: every safehouse must be beatable.' };
    return { level: c.level, par: sol.length, solution: sol, free: c.level.free };
  }

  return { W, H, N, DX, DY, GUARDS, compile, apply, out, solve, replay, rate };
});
