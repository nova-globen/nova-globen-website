/* Relic Rush art: a lit temple labyrinth in 3/4 top-down pixel art, drawn in code at 640 x 352 (16 px tiles) and
   scaled up pixel-perfect. Floors are carved slabs with grout, moss and cracks; walls show a lit cap and a brick
   face with the shadow they cast; torches flicker; a light layer keeps the maze dark except where torches, the relic
   and the shrines shine. Runners are shaded, outlined sprites in their team's colours, in four directions. */
(function (root) {
  'use strict';
  const T = 16, COLS = 40, ROWS = 22, W = COLS * T, H = ROWS * T;

  // ------------------------------------------------------------------ helpers
  function hash(x, y, s) {
    let h = (Math.imul(x | 0, 374761393) + Math.imul(y | 0, 668265263) + Math.imul(s | 0, 1442695041)) | 0;
    h = Math.imul(h ^ (h >>> 13), 1274126177);
    h ^= h >>> 16;
    return (h >>> 0) / 4294967296;
  }
  function makeCanvas(w, h) { const c = document.createElement('canvas'); c.width = w; c.height = h; return c; }
  function shade(hex, f) {
    const n = parseInt(hex.slice(1), 16);
    const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
    const k = (v) => Math.max(0, Math.min(255, Math.round(f >= 0 ? v + (255 - v) * f : v * (1 + f))));
    return '#' + ((1 << 24) | (k(r) << 16) | (k(g) << 8) | k(b)).toString(16).slice(1);
  }
  function mix(c1, c2, t) {
    const a = parseInt(c1.slice(1), 16), b = parseInt(c2.slice(1), 16);
    const ch = (s) => Math.round(((a >> s) & 255) + ((((b >> s) & 255) - ((a >> s) & 255)) * t));
    return '#' + ((1 << 24) | (ch(16) << 16) | (ch(8) << 8) | ch(0)).toString(16).slice(1);
  }
  function px(g, x, y, c) { g.fillStyle = c; g.fillRect(x, y, 1, 1); }

  /** A material mask lit from the top left: four tones per material, an outline around the silhouette. */
  function shadeSprite(rows, mat, outline) {
    const w = Math.max(...rows.map((r) => r.length)), h = rows.length;
    const at = (x, y) => (y < 0 || y >= h || x < 0 || x >= w ? '.' : (rows[y][x] || '.'));
    const filled = (x, y) => at(x, y) !== '.';
    const c = makeCanvas(w + 2, h + 2), g = c.getContext('2d');
    for (let y = -1; y <= h; y++) {
      for (let x = -1; x <= w; x++) {
        const m = at(x, y);
        if (m === '.') {
          if ([[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => filled(x + dx, y + dy))) px(g, x + 1, y + 1, outline);
          continue;
        }
        const def = mat[m];
        if (!def) continue;
        let col;
        if (def.fixed) col = def.fixed;
        else {
          const lit = (at(x, y - 1) !== m && !(mat[at(x, y - 1)] || {}).fixed) || !filled(x - 1, y);
          if (lit) col = def[0];
          else if (!filled(x, y + 1) || !filled(x + 1, y)) col = def[3];
          else if (!filled(x, y + 2) || !filled(x + 2, y) || at(x, y + 1) !== m) col = def[2];
          else col = def[1];
        }
        px(g, x + 1, y + 1, col);
      }
    }
    return c;
  }

  // ------------------------------------------------------------------ the maze
  // warm sandstone underfoot, cool slate walls: where you can walk reads at a glance
  const STONE = ['#8a7660', '#93806a', '#7f6c57', '#9a8670'];
  const GROUT = '#4a3c30', SLAB_HI = '#b09a80', SLAB_LO = '#6a5848';
  const MOSS = ['#3f6b4a', '#4f8a58', '#2f5038', '#6aa86a'];
  const CAP = ['#6a6482', '#8a84a4', '#4e4866', '#3a3550'];
  const BRICK = ['#2e2a3c', '#383248', '#262232', '#40394f'];
  const MORTAR = '#15121c';

  /** Pre-renders the static maze; returns the canvas and the torch positions for the live layer. */
  function renderMaze(cells) {
    const c = makeCanvas(W, H), g = c.getContext('2d');
    const wall = (x, y) => x < 0 || y < 0 || x >= COLS || y >= ROWS || cells[y * COLS + x] === '#';
    const torches = [];
    for (let ty = 0; ty < ROWS; ty++) {
      for (let tx = 0; tx < COLS; tx++) {
        if (wall(tx, ty)) paintWall(g, wall, tx, ty, torches);
        else paintFloor(g, wall, tx, ty);
      }
    }
    // cast shadows: the brick faces darken the floor right below them, walls darken their sides a little
    g.save();
    for (let ty = 0; ty < ROWS; ty++) for (let tx = 0; tx < COLS; tx++) {
      if (wall(tx, ty)) continue;
      const ox = tx * T, oy = ty * T;
      if (wall(tx, ty - 1)) for (let i = 0; i < 4; i++) { g.fillStyle = `rgba(8,6,14,${0.42 - i * 0.1})`; g.fillRect(ox, oy + i, T, 1); }
      if (wall(tx - 1, ty)) for (let i = 0; i < 3; i++) { g.fillStyle = `rgba(8,6,14,${0.28 - i * 0.09})`; g.fillRect(ox + i, oy, 1, T); }
      if (wall(tx + 1, ty)) { g.fillStyle = 'rgba(8,6,14,0.12)'; g.fillRect(ox + T - 1, oy, 1, T); }
    }
    g.restore();
    return { canvas: c, torches };
  }

  function paintFloor(g, wall, tx, ty) {
    const ox = tx * T, oy = ty * T;
    // slab layout per tile: one big slab, two halves, or four quarters
    const kind = hash(tx, ty, 1);
    const slabs = kind < 0.55 ? [[0, 0, 16, 16]] : kind < 0.9 ? (hash(tx, ty, 2) < 0.5 ? [[0, 0, 16, 8], [0, 8, 16, 8]] : [[0, 0, 8, 16], [8, 0, 8, 16]]) : [[0, 0, 8, 8], [8, 0, 8, 8], [0, 8, 8, 8], [8, 8, 8, 8]];
    slabs.forEach(([sx, sy, sw, sh], i) => {
      const tone = STONE[Math.floor(hash(tx * 4 + i, ty, 3) * STONE.length)];
      for (let y = 0; y < sh; y++) for (let x = 0; x < sw; x++) {
        let col = tone;
        const n = hash(ox + sx + x, oy + sy + y, 4);
        if (n > 0.95) col = shade(tone, 0.06); else if (n < 0.05) col = shade(tone, -0.1);
        if (x === 0 || y === 0) col = mix(tone, SLAB_HI, 0.35);
        if (x === sw - 2 || y === sh - 2) col = mix(tone, SLAB_LO, 0.3);
        if (x === sw - 1 || y === sh - 1) col = GROUT;
        px(g, ox + sx + x, oy + sy + y, col);
      }
    });
    // a crack now and then
    if (hash(tx, ty, 5) > 0.82) {
      let x = 3 + Math.floor(hash(tx, ty, 6) * 9), y = 2 + Math.floor(hash(tx, ty, 7) * 5);
      for (let i = 0; i < 7; i++) { px(g, ox + x, oy + y, '#5a4a3c'); x += hash(tx, ty + i, 8) > 0.5 ? 1 : 0; y += 1; if (x > 14 || y > 14) break; }
    }
    // moss creeps out from the walls
    const near = [wall(tx - 1, ty), wall(tx + 1, ty), wall(tx, ty - 1), wall(tx, ty + 1)];
    if (near.some(Boolean) && hash(tx, ty, 9) > 0.45) {
      for (let i = 0; i < 14; i++) {
        const side = Math.floor(hash(tx, ty + i, 10) * 4);
        if (!near[side]) continue;
        const along = Math.floor(hash(tx + i, ty, 11) * 16), depth = Math.floor(Math.pow(hash(tx, ty, 12 + i), 2) * 5);
        const x = side === 0 ? depth : side === 1 ? 15 - depth : along;
        const y = side === 2 ? depth + 3 : side === 3 ? 15 - depth : along;
        px(g, ox + x, oy + y, MOSS[Math.floor(hash(i, tx, ty) * 3)]);
        if (hash(i, ty, tx) > 0.7) px(g, ox + x, oy + y - 1, MOSS[3]);
      }
    }
    // pebbles and dust
    if (hash(tx, ty, 13) > 0.8) {
      const x = 2 + Math.floor(hash(tx, ty, 14) * 11), y = 2 + Math.floor(hash(tx, ty, 15) * 11);
      px(g, ox + x, oy + y, '#b8a48a'); px(g, ox + x + 1, oy + y, '#7a6652'); px(g, ox + x, oy + y + 1, '#4e3e30');
    }
    // a faint carved rune on a few slabs
    if (hash(tx, ty, 16) > 0.985) {
      g.fillStyle = 'rgba(70,50,30,0.35)';
      g.fillRect(ox + 6, oy + 4, 1, 7); g.fillRect(ox + 9, oy + 4, 1, 7); g.fillRect(ox + 6, oy + 7, 4, 1);
    }
  }

  function paintWall(g, wall, tx, ty, torches) {
    const ox = tx * T, oy = ty * T;
    const face = !wall(tx, ty + 1);               // a floor below: the brick face shows
    const capH = face ? 6 : 16;
    // cap: big lit blocks on top of the wall
    for (let y = 0; y < capH; y++) for (let x = 0; x < T; x++) {
      const bx = Math.floor((x + (Math.floor((oy + y) / 8) % 2) * 4) / 8);
      let col = CAP[Math.floor(hash(tx * 2 + bx, Math.floor((oy + y) / 8), 21) * 2) === 0 ? 0 : 2];
      col = mix(col, '#3a3550', 0.3);
      if ((x + (Math.floor((oy + y) / 8) % 2) * 4) % 8 === 7 || (oy + y) % 8 === 7) col = shade(col, -0.25);
      if (hash(ox + x, oy + y, 22) > 0.94) col = shade(col, 0.1);
      px(g, ox + x, oy + y, col);
    }
    // lit rim where the cap meets open floor
    if (!wall(tx, ty - 1)) for (let x = 0; x < T; x++) px(g, ox + x, oy, CAP[1]);
    if (!wall(tx - 1, ty)) for (let y = 0; y < capH; y++) px(g, ox, oy + y, CAP[0]);
    if (!wall(tx + 1, ty)) for (let y = 0; y < capH; y++) px(g, ox + T - 1, oy + y, CAP[3]);
    if (face) {
      for (let x = 0; x < T; x++) px(g, ox + x, oy + capH - 1, CAP[3]);
      // brick face, 5 px courses, staggered
      for (let y = capH; y < T; y++) for (let x = 0; x < T; x++) {
        const course = Math.floor((y - capH) / 5);
        const off = course % 2 ? 4 : 0;
        const brick = Math.floor((x + off) / 8);
        let col = BRICK[Math.floor(hash(tx * 3 + brick, ty * 3 + course, 23) * BRICK.length)];
        if ((y - capH) % 5 === 0) col = shade(col, 0.12);
        if ((y - capH) % 5 === 4 || (x + off) % 8 === 7) col = MORTAR;
        if (y >= T - 1) col = '#15121a';
        px(g, ox + x, oy + y, col);
      }
      // ivy on some faces, torches on others
      if (hash(tx, ty, 24) > 0.78) {
        const x0 = 2 + Math.floor(hash(tx, ty, 25) * 10);
        for (let v = 0; v < 3; v++) {
          const len = 3 + Math.floor(hash(tx + v, ty, 26) * 8);
          for (let y = 0; y < len; y++) px(g, ox + x0 + v * 2 + (y % 3 === 0 ? 1 : 0), oy + capH + y, MOSS[y % 2 ? 0 : 1]);
        }
      } else if (hash(tx, ty, 27) > 0.86 && hash(tx - 1, ty, 27) <= 0.86 && hash(tx + 1, ty, 27) <= 0.86) {
        // iron bracket; the flame is drawn live
        g.fillStyle = '#1a1820'; g.fillRect(ox + 7, oy + capH + 3, 2, 5);
        g.fillStyle = '#5a5060'; g.fillRect(ox + 6, oy + capH + 2, 4, 2);
        torches.push({ x: ox + 8, y: oy + capH + 1, seed: tx * 31 + ty });
      }
    }
    // dark outline where wall meets floor at the sides
    if (!wall(tx - 1, ty) && face) for (let y = capH; y < T; y++) px(g, ox, oy + y, '#1a1720');
    if (!wall(tx + 1, ty) && face) for (let y = capH; y < T; y++) px(g, ox + T - 1, oy + y, '#141118');
  }

  function drawTorches(g, torches, t) {
    for (const tc of torches) {
      const f = 0.5 + 0.5 * Math.sin(t * 13 + tc.seed) * Math.sin(t * 7.3 + tc.seed * 2);
      const x = tc.x, y = tc.y;
      px(g, x - 1, y, '#ff7a1f'); px(g, x, y, '#ffb02e'); px(g, x + 1, y, '#ff7a1f');
      px(g, x - 1, y - 1, '#ffb02e'); px(g, x, y - 1, '#ffe27a'); px(g, x + 1, y - 1, '#ffb02e');
      px(g, x, y - 2, f > 0.4 ? '#ffe27a' : '#ffb02e');
      if (f > 0.6) px(g, x + (f > 0.8 ? 1 : -1), y - 3, '#ff7a1f');
      if (hash(tc.seed, Math.floor(t * 6), 3) > 0.85) px(g, x + Math.floor(hash(tc.seed, Math.floor(t * 6), 4) * 5) - 2, y - 5 - Math.floor(hash(tc.seed, Math.floor(t * 6), 5) * 4), '#ffb02e');
    }
  }

  // ------------------------------------------------------------------ light
  /** A darkness layer with holes cut by lights, then a warm additive glow on top. */
  function lighting(w, h) {
    const layer = makeCanvas(w, h), lg = layer.getContext('2d');
    const blobs = {};
    function blob(color) {
      if (blobs[color]) return blobs[color];
      const c = makeCanvas(64, 64), gg = c.getContext('2d');
      const gr = gg.createRadialGradient(32, 32, 0, 32, 32, 32);
      gr.addColorStop(0, color); gr.addColorStop(0.45, color.replace(/[\d.]+\)$/, '0.55)')); gr.addColorStop(1, color.replace(/[\d.]+\)$/, '0)'));
      gg.fillStyle = gr; gg.fillRect(0, 0, 64, 64);
      return (blobs[color] = c);
    }
    const lights = [];
    return {
      begin() { lights.length = 0; },
      add(x, y, r, strength, tint) { lights.push({ x, y, r, s: strength, tint }); },
      apply(g, ambient) {
        lg.globalCompositeOperation = 'source-over';
        lg.clearRect(0, 0, w, h);
        lg.fillStyle = ambient || 'rgba(6,4,14,0.62)';
        lg.fillRect(0, 0, w, h);
        lg.globalCompositeOperation = 'destination-out';
        for (const l of lights) {
          lg.globalAlpha = Math.min(1, l.s);
          lg.drawImage(blob('rgba(0,0,0,1)'), l.x - l.r, l.y - l.r, l.r * 2, l.r * 2);
        }
        lg.globalAlpha = 1;
        g.drawImage(layer, 0, 0);
        g.save();
        g.globalCompositeOperation = 'lighter';
        for (const l of lights) {
          if (!l.tint) continue;
          g.globalAlpha = 0.35 * l.s;
          g.drawImage(blob(l.tint), l.x - l.r * 0.8, l.y - l.r * 0.8, l.r * 1.6, l.r * 1.6);
        }
        g.restore();
      }
    };
  }
  function glow(g, x, y, r, color, a) {
    if (!glow.cache) glow.cache = {};
    if (!glow.cache[color]) {
      const c = makeCanvas(64, 64), gg = c.getContext('2d');
      const gr = gg.createRadialGradient(32, 32, 0, 32, 32, 32);
      gr.addColorStop(0, color); gr.addColorStop(0.4, color + '88'); gr.addColorStop(1, color + '00');
      gg.fillStyle = gr; gg.fillRect(0, 0, 64, 64);
      glow.cache[color] = c;
    }
    g.save(); g.globalCompositeOperation = 'lighter'; g.globalAlpha = a == null ? 1 : a;
    g.drawImage(glow.cache[color], x - r, y - r, r * 2, r * 2);
    g.restore();
  }

  // ------------------------------------------------------------------ runners
  const TEAMS = [
    { name: 'Red', color: '#ff5a4f', t: ['#ff9488', '#dc4a40', '#ac2e2e', '#6c161a'], c: ['#c8403c', '#94262c', '#701a20', '#420c12'], trim: '#ffd25a' },
    { name: 'Blue', color: '#4fa8ff', t: ['#9cd2ff', '#4486e0', '#2c5cb0', '#163470'], c: ['#4472c8', '#264a94', '#183270', '#0c1c44'], trim: '#e6f4ff' }
  ];
  const HAIR = [
    ['#5a463a', '#33261e', '#241a14', '#140c08'], ['#b07a42', '#7e5026', '#5c3618', '#3a200c'], ['#ffe38c', '#e2b24c', '#b8862a', '#7a5410'],
    ['#ff9050', '#d25a2a', '#a23e18', '#62200c'], ['#ffffff', '#dedee8', '#b2b2c2', '#7a7a8e'], ['#6a7aaa', '#3a466e', '#262e4e', '#14182c']
  ];
  const SKIN = [['#ffe2c6', '#f2c29a', '#dca078', '#b47652'], ['#ecbc8e', '#c88e60', '#a86e44', '#7a4a2a'], ['#b07656', '#86503a', '#6a3a28', '#44221a']];
  const BODY = {
    down: [
      '...hhhhhh...', '..hhhhhhhh..', '.hhhhhhhhhh.', '.hhsssssshh.', '.hsessssesh.', '.hssssmsssh.', '...ssssss...',
      '..cttyyttc..', '.acttttttca.', '.acttttttca.', '.kcbbBBbbck.', '..cttttttc..', '..cttttttc..'
    ],
    up: [
      '...hhhhhh...', '..hhhhhhhh..', '.hhhhhhhhhh.', '.hhhhhhhhhh.', '.hhhhhhhhhh.', '..hhhhhhhh..', '....ssss....',
      '..cccccccc..', '.acccyyccca.', '.acccccccca.', '.kcbbbbbbck.', '..cccccccc..', '..cccccccc..'
    ],
    side: [
      '...hhhhh....', '..hhhhhhh...', '.hhhhhhhhh..', '.hhhhsssss..', '.hhhssssess.', '..hhsssssm..', '....ssss....',
      '..ccttttt...', '..cctttatt..', '..cctttatt..', '..ccbbbkbb..', '..cctttttt..', '...cttttt...'
    ]
  };
  const LEGS = { front: ['pp', 'pp', 'ff'], side: ['pp.', 'pp.', 'fff'] };
  const spriteCache = {};
  function runnerParts(team, look, dir) {
    const key = team + ':' + look + ':' + dir;
    if (spriteCache[key]) return spriteCache[key];
    const tm = TEAMS[team] || TEAMS[0];
    const mat = {
      h: HAIR[look % HAIR.length], s: SKIN[look % SKIN.length], k: SKIN[look % SKIN.length], e: { fixed: '#1a1420' }, m: { fixed: '#9a4a44' },
      t: tm.t, c: tm.c, a: tm.t, y: { fixed: tm.trim }, b: ['#9a6a3a', '#6b4224', '#4e2e16', '#2e1a0a'], B: { fixed: '#e8c45a' },
      p: ['#5a5068', '#3c3448', '#2c2636', '#1a1620'], f: ['#8a5a32', '#5e3a1c', '#46280f', '#2a1606']
    };
    const legs = dir === 'side' ? LEGS.side : LEGS.front;
    return (spriteCache[key] = {
      body: shadeSprite(BODY[dir], mat, '#0c0a12'),
      leg: shadeSprite(legs, mat, '#0c0a12')
    });
  }

  /**
   * A runner with its feet at (x, y). s: { team, look, faceX, faceY, t, moving, dashing, me, immune }.
   */
  function drawRunner(g, x, y, s) {
    const fx = s.faceX || 0, fy = s.faceY || 0;
    const dir = Math.abs(fx) > Math.abs(fy) + 0.01 ? 'side' : fy < 0 ? 'up' : 'down';
    const parts = runnerParts(s.team, s.look || 0, dir);
    const t = s.t || 0;
    const phase = s.moving ? Math.sin(t * 16) : 0;
    const bob = s.moving && Math.abs(phase) > 0.7 ? -1 : 0;
    const X = Math.round(x), Y = Math.round(y);
    // ground shadow and team ring
    g.fillStyle = 'rgba(4,2,10,0.45)';
    g.beginPath(); g.ellipse(X, Y, 6, 2.5, 0, 0, Math.PI * 2); g.fill();
    g.strokeStyle = TEAMS[s.team].color;
    g.globalAlpha = s.me ? 0.95 : 0.6;
    g.lineWidth = 1;
    g.beginPath(); g.ellipse(X, Y, 7.5, 3, 0, 0, Math.PI * 2); g.stroke();
    g.globalAlpha = 1;
    g.save();
    g.translate(X, Y);
    if (dir === 'side' && fx < 0) g.scale(-1, 1);
    const legs = parts.leg;
    if (dir === 'side') {
      const swing = Math.round(phase * 2);
      g.drawImage(legs, -4 + swing, -5);
      g.drawImage(legs, -3 - swing, -5);
    } else {
      const lift = s.moving ? Math.round(phase) : 0;
      g.drawImage(legs, -4, -5 - Math.max(0, lift));
      g.drawImage(legs, 0, -5 - Math.max(0, -lift));
    }
    g.drawImage(parts.body, -7, -18 + bob);
    g.restore();
    if (s.immune) {
      g.strokeStyle = 'rgba(255,240,180,0.6)';
      g.beginPath(); g.arc(X, Y - 9, 11, 0, Math.PI * 2); g.stroke();
    }
  }

  // ------------------------------------------------------------------ relic and shrines
  const RELIC = shadeSpriteLater();
  function shadeSpriteLater() { let c = null; return () => c || (c = shadeSprite(['..ggg..', '.gGGGg.', 'gGwGGgg', 'gGGGGgg', 'gGGGggg', '.ggggg.', '..ggg..'], { g: ['#fff3b0', '#f2c642', '#c88e1c', '#8a5a0c'], G: ['#fff8d0', '#ffdc6a', '#e0a82a', '#a8700c'], w: { fixed: '#ffffff' } }, '#3a2406')); }
  function drawRelic(g, x, y, t, carried) {
    const bobY = Math.round(Math.sin(t * 4) * 1.5);
    const X = Math.round(x), Y = Math.round(y) + bobY;
    glow(g, X, Y, 18, '#ffd25a', 0.55 + Math.sin(t * 6) * 0.1);
    const img = RELIC();
    g.drawImage(img, X - 4, Y - 4);
    // orbiting sparks
    for (let i = 0; i < 3; i++) {
      const a = t * 3 + i * 2.1;
      px(g, Math.round(X + Math.cos(a) * 7), Math.round(Y + Math.sin(a) * 3.5), i ? '#ffe27a' : '#ffffff');
    }
    if (!carried) {
      g.fillStyle = 'rgba(4,2,10,0.4)';
      g.beginPath(); g.ellipse(X, Math.round(y) + 8, 4, 1.5, 0, 0, Math.PI * 2); g.fill();
    }
  }
  function drawShrine(g, x, y, team, t, mine) {
    const tm = TEAMS[team];
    const X = Math.round(x), Y = Math.round(y);
    // carved ring of runes
    g.fillStyle = 'rgba(10,6,20,0.5)';
    g.beginPath(); g.ellipse(X, Y, 15, 9, 0, 0, Math.PI * 2); g.fill();
    for (let i = 0; i < 16; i++) {
      const a = i / 16 * Math.PI * 2 + t * 0.6;
      const on = (Math.floor(t * 4) + i) % 4 !== 0;
      px(g, Math.round(X + Math.cos(a) * 13), Math.round(Y + Math.sin(a) * 7.5), on ? tm.color : shade(tm.color, -0.5));
    }
    for (let i = 0; i < 8; i++) {
      const a = -i / 8 * Math.PI * 2 + t * 0.9;
      px(g, Math.round(X + Math.cos(a) * 8), Math.round(Y + Math.sin(a) * 4.5), tm.trim);
    }
    // the sigil
    g.fillStyle = tm.color;
    g.fillRect(X - 1, Y - 3, 2, 6); g.fillRect(X - 3, Y - 1, 6, 2);
    px(g, X, Y, '#ffffff');
    // a column of light rising from it
    const grd = g.createLinearGradient(0, Y - 34, 0, Y);
    grd.addColorStop(0, tm.color + '00'); grd.addColorStop(1, tm.color + (mine ? '66' : '44'));
    g.save(); g.globalCompositeOperation = 'lighter';
    g.fillStyle = grd; g.fillRect(X - 6, Y - 34, 12, 34);
    g.restore();
    for (let i = 0; i < 6; i++) {
      const life = (t * 0.7 + i / 6) % 1;
      px(g, Math.round(X + Math.sin(i * 7 + t * 2) * 6), Math.round(Y - life * 30), life < 0.5 ? '#ffffff' : tm.color);
    }
    glow(g, X, Y - 4, 26, tm.color, 0.5);
  }

  root.RelicArt = { T, COLS, ROWS, W, H, TEAMS, hash, makeCanvas, shade, renderMaze, drawTorches, lighting, glow, drawRunner, drawRelic, drawShrine };
})(typeof window !== 'undefined' ? window : globalThis);
