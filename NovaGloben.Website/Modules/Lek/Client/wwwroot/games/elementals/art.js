/* Elementals art. Every sprite, tile, background and effect is drawn in code at the game's logical resolution
   (640 x 352: 40 x 22 tiles of 16 px) and scaled up with pixelated rendering, Terraria style. Characters are paper
   dolls: head, torso, arms and legs are small pixel sprites placed and rotated per frame, so the head follows the aim
   and the weapon swings in an arc. Shared by the game (game.js) and the art samples (samples.html). */
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
  function makeCanvas(w, h) {
    const c = document.createElement('canvas');
    c.width = w; c.height = h;
    return c;
  }
  function compile(rows, pal) {
    const w = Math.max(...rows.map((r) => r.length)), h = rows.length;
    const c = makeCanvas(w, h), g = c.getContext('2d');
    rows.forEach((row, y) => {
      for (let x = 0; x < row.length; x++) {
        const col = pal[row[x]];
        if (col) { g.fillStyle = col; g.fillRect(x, y, 1, 1); }
      }
    });
    return c;
  }
  function shade(hex, f) {
    const n = parseInt(hex.slice(1), 16);
    const r = (n >> 16) & 255, g = (n >> 8) & 255, b = n & 255;
    const k = (v) => Math.max(0, Math.min(255, Math.round(f >= 0 ? v + (255 - v) * f : v * (1 + f))));
    return '#' + ((1 << 24) | (k(r) << 16) | (k(g) << 8) | k(b)).toString(16).slice(1);
  }
  function lerp(a, b, t) { return a + (b - a) * t; }
  function mix(c1, c2, t) {
    const a = parseInt(c1.slice(1), 16), b = parseInt(c2.slice(1), 16);
    const ch = (s) => Math.round(lerp((a >> s) & 255, (b >> s) & 255, t));
    return '#' + ((1 << 24) | (ch(16) << 16) | (ch(8) << 8) | ch(0)).toString(16).slice(1);
  }

  // ------------------------------------------------------------------ characters
  // Characters are authored as material masks (one letter per pixel, '.' empty); shade() lights every material
  // from the top left and outlines the silhouette, so each part reads like hand-shaded Terraria pixel art.
  // A material is [highlight, base, shadow] or { fixed: colour } (eyes, spines, gems: no shading).
  const CHARS = {
    cactus: {
      name: 'Cactus Man', element: 'nature', color: '#6fd04f', outline: '#14260f',
      mat: {
        g: ['#a6e46f', '#56a83f', '#2f6e27'], r: ['#6cbf4f', '#3c8a30', '#26601f'], x: { fixed: '#f4ecc2', bare: true },
        f: ['#ffb3d6', '#ff6fa8', '#c23d77'], y: { fixed: '#ffe066' }, v: { fixed: '#13230f' }, e: { fixed: '#ffe066' },
        l: ['#a8763f', '#7a4a24', '#4a2a14'], t: ['#e8955a', '#c06a35', '#7a3a1a'], k: ['#a6e46f', '#56a83f', '#2f6e27']
      },
      head: [
        '.....ff.....',
        '....fyyf....',
        '.....ff.....',
        '...gggggg...',
        '..gggrgggg..',
        '.xgrggrggg..',
        '..grggrvvvvx',
        '..grggvveevv',
        'x.grggrvvvv.',
        '..grggrggggx',
        '.xgrggrggg..',
        '..gggrgggg..',
        '...gggggg...'
      ],
      torso: [
        '..gggggggg..',
        '.gggrggrggg.',
        'xgggrggrgggx',
        '.gggrggrggg.',
        '..ggrggrgg..',
        'x.ggrggrgg.x',
        '..ggrggrgg..',
        '..llllllll..',
        '..lllyylll..',
        '..ggrggrgg..',
        '..ggrggrgg..',
        '..gggggggg..'
      ],
      arm: ['ggg.', 'grgg', 'grgx', 'ggg.', '.gg.', '.gg.', 'xgg.', '.gg.', '.gg.', '.gk.', '.kk.'],
      leg: ['ggg..', 'grg..', 'grg..', 'grgx.', 'grg..', 'ttt..', 'ttt..', 'ttt..', 'tttt.', 'ttttt']
    },
    ice: {
      name: 'Ice Man', element: 'ice', color: '#7fdcff', outline: '#10213a',
      mat: {
        i: ['#f0fbff', '#a9def7', '#5f9fcc'], c: ['#ffffff', '#9fe6ff', '#3fb6e8'], s: ['#eef8fc', '#cfe8f5', '#94bcd6'],
        e: { fixed: '#2ef2ff' }, d: ['#7fa8d6', '#3b6a9a', '#24486e'], m: ['#ffffff', '#e0f4ff', '#b0d4ea']
      },
      head: [
        '...c..c..c..',
        '..cc.cccccc.',
        '..ccccccccc.',
        '.iiiiiiiiii.',
        '.iiiiiiiiii.',
        '.iiiisssss..',
        '.iiiissees..',
        '.iiiisssss..',
        '.iiiissssm..',
        '.iiimmmmm...',
        '..iimmmm....',
        '...iiii.....'
      ],
      torso: [
        'c.iiiiiiii..',
        'cciiiicciii.',
        '.ciiiiccciic',
        '..iiiicciii.',
        '..iiiiiciii.',
        '..iiiiiiii..',
        '..iiiiiiii..',
        '..dddddddd..',
        '..dddcdddd..',
        '..iiiiiiii..',
        '..iiiiiiii..',
        '..iiiiiiii..'
      ],
      arm: ['ccc.', 'iiic', 'iii.', 'iii.', '.ii.', '.ii.', '.ii.', '.ii.', '.ii.', '.ss.', '.ss.'],
      leg: ['iii..', 'iii..', 'iii..', 'iii..', 'ddd..', 'ddd..', 'iii..', 'iii..', 'iiii.', 'ccccc']
    },
    fire: {
      name: 'Fireman', element: 'fire', color: '#ff8a1f', outline: '#0c0504',
      mat: {
        k: ['#6a5650', '#3d2c28', '#1f1411'], x: { fixed: '#ff8a1f' }, X: { fixed: '#ffd23f' }, v: { fixed: '#120806' },
        e: { fixed: '#fff07a' }, b: ['#5a3a2c', '#3a241a', '#1e120c']
      },
      head: [
        '............',
        '............',
        '............',
        '...kkkkkk...',
        '..kkkkkkkk..',
        '.kkkkkxkkkk.',
        '.kkkkvvvvvv.',
        '.kkkkvXeXvv.',
        '.kkxkvvvvvv.',
        '.kkkxkkkkkk.',
        '.kkkkxkkkk..',
        '..kkkkkkkk..',
        '...kkkkkk...'
      ],
      torso: [
        '..kkkkkkkk..',
        '.kkkkkkkkkk.',
        'kkkxkkkkkkkk',
        '.kkkxXkkkkk.',
        '..kkkxkxkk..',
        '..kkxkkkxk..',
        '..kkkkkxkk..',
        '..bbbXbbbb..',
        '..kkkkkkkk..',
        '..kkxkkkkk..',
        '..kkkxkkkk..',
        '..kkkkkkkk..'
      ],
      arm: ['kkk.', 'kxkk', 'kkk.', 'kkk.', '.kk.', '.kx.', '.kk.', '.kk.', '.kk.', '.bb.', '.bb.'],
      leg: ['kkk..', 'kxk..', 'kkk..', 'kkx..', 'kkk..', 'bbb..', 'bbb..', 'bbb..', 'bbbb.', 'bbbbb']
    },
    thunder: {
      name: 'God of Thunder', element: 'lightning', color: '#ffd84a', outline: '#120f24',
      mat: {
        a: ['#7f9ff5', '#3157b8', '#1f377c'], g: ['#ffe48a', '#f5c842', '#a8780f'], s: ['#ffdcb8', '#f0c9a2', '#c79670'],
        w: ['#ffffff', '#e8e8f2', '#a9a9bd'], W: ['#ffffff', '#eef0ff', '#b8bde0'], h: ['#ffffff', '#c9cfe0', '#7a8199'],
        e: { fixed: '#8ff0ff' }, b: ['#9a6a3a', '#6b4224', '#40250f']
      },
      head: [
        'W...........',
        'WW.hhhhhh...',
        'WWhhhhhhhh..',
        '.Whhhgghhhh.',
        '..hhhhhhhhh.',
        '..hhssssss..',
        '..hhsssess..',
        '..whssssss..',
        '..wwwwwwws..',
        '..wwwwwwww..',
        '...wwwwww...',
        '....wwww....',
        '.....ww.....'
      ],
      torso: [
        '..aaaaaaaa..',
        'ggaaaaaaaagg',
        'gaaaagaaaaag',
        '..aaaggaaa..',
        '..aaaagaaa..',
        '..aaagaaaa..',
        '..aaaaaaaa..',
        '..gggggggg..',
        '..gggegggg..',
        '..aaaaaaaa..',
        '..aaaaaaaa..',
        '..aaaaaaaa..'
      ],
      arm: ['ggg.', 'gggg', 'aaa.', 'aaa.', '.aa.', '.aa.', '.gg.', '.aa.', '.aa.', '.ss.', '.ss.'],
      leg: ['aaa..', 'aaa..', 'aaa..', 'aaa..', 'aaa..', 'bbb..', 'bbb..', 'bgb..', 'bbbb.', 'bbbbb']
    }
  };

  CHARS.vampire = {
    name: 'Vampire', element: 'blood', color: '#ff2a3a', outline: '#0e0710',
    mat: {
      s: ['#f8f2f6', '#ddd0da', '#a294a6'], k: ['#4a4458', '#24202e', '#0e0c14'], r: ['#ff5a6a', '#c8102e', '#7a0a1c'],
      c: ['#5a3a5e', '#3a2240', '#1e1024'], e: { fixed: '#ff2a3a' }, W: { fixed: '#ffffff' }, g: { fixed: '#e0b850' },
      b: ['#4a3a4a', '#2a1e2a', '#140e14']
    },
    head: [
      '............',
      '............',
      '...kkkkkk...',
      '..kkkkkkkkk.',
      '.kkkkkkskkk.',
      '.kkkksssssk.',
      '.kkksssessS.',
      '.kkkssssssS.',
      '.kkksssssss.',
      '..kkssssrrS.',
      '...kssssWs..',
      '...ssssss...',
      '....ssss....'
    ],
    torso: [
      '.rrcccccrr..',
      '.rcWWccWWcr.',
      '..cccWWcccc.',
      '..ccccgccc..',
      '..ccccrccc..',
      '..cccccccc..',
      '..ccccgccc..',
      '..rrrrrrrr..',
      '..cccccccc..',
      '..cccccccc..',
      '..cccccccc..',
      '..cccccccc..'
    ],
    arm: ['ccc.', 'ccc.', 'ccc.', '.cc.', '.cc.', '.cc.', '.cc.', '.rr.', '.ss.', '.ss.', 'sWs.'],
    leg: ['ccc..', 'ccc..', 'ccc..', 'ccc..', 'ccc..', 'bbb..', 'bbb..', 'bbb..', 'bbbb.', 'bbbbb'],
    cape: ['#14080f', '#7a0a1c', '#c8102e']
  };
  CHARS.thunder.cape = ['#5e1219', '#b52a35', '#e04a55'];
  CHARS.patch = {
    name: 'PATCH', element: 'tech', color: '#5afff0', outline: '#0b1418',
    mat: {
      m: ['#eef2f8', '#a7b1c4', '#5e6880'], t: ['#9fffe8', '#2ec4a0', '#16705c'], v: { fixed: '#0a1418' },
      e: { fixed: '#5afff0' }, r: { fixed: '#ff4d6a' }
    },
    head: [
      '.....r......',
      '.....m......',
      '.....m......',
      '..mmmmmmmm..',
      '.mmmmmmmmmm.',
      '.mmvvvvvvvm.',
      '.mmvvvveevm.',
      '.mmvvvvvvvm.',
      '.mmvvveeevm.',
      '.mmmmmmmmmm.',
      '.tmmmmmmmmt.',
      '..mmmmmmmm..',
      '............'
    ],
    torso: [
      '..mmmmmmmm..',
      '.tmmmmmmmmt.',
      '.mmmttttmmm.',
      '..mmtvvtmm..',
      '..mmtvetmm..',
      '..mmttttmm..',
      '..mmmmmmmm..',
      '..tttttttt..',
      '..mmmmmmmm..',
      '..mmmmmmmm..',
      '..mmmmmmmm..',
      '..mmmmmmmm..'
    ],
    arm: ['mmm.', 'mtm.', 'mmm.', '.mm.', '.mm.', '.tt.', '.mm.', '.mm.', '.mm.', '.tt.', '.tt.'],
    leg: ['mmm..', 'mmm..', 'mtm..', 'mmm..', 'mmm..', 'ttt..', 'mmm..', 'mmm..', 'mmmm.', 'mmmmm']
  };

  // ---- v2 heroes: one human body template (Terraria-style proportions, about 20 x 38), each hero dresses it with
  // its own materials and decorations, the way Terraria armour sets share one body.
  const TEMPLATE = {
    head: [
      '....HHHHHH....',
      '..HHHRHHHRHH..',
      '.HHRHHHRHHHHH.',
      '.HHRHHHRHHHHHH',
      'HHRHHHRHHHHHHH',
      'HHRHHhhhhhhhHH',
      'HHRHhhhsssssHH',
      'HHRHhssssssss.',
      'HHRHhssswpsss.',
      'HHRHhssssssss.',
      '.HRHHsssssSs..',
      '.HHHHssssmS...',
      '..HHH.sssS....',
      '......sSS.....'
    ],
    torso: [
      '...AAAAAAAA...',
      '..AAABBBBAAA..',
      '.AAABBBBBBAAA.',
      '.AABBRBBRBBAA.',
      '..ABBRBBRBBA..',
      '..BBBRBBRBBB..',
      '..BBBRBBRBBB..',
      '..LLLLLLLLLL..',
      '..LLLLKKLLLL..',
      '..BBBRBBRBBB..',
      '..BBBRBBRBBB..',
      '..CCCCCCCCCC..',
      '..CCC....CCC..'
    ],
    arm: ['AAAA.', 'AAAAA', 'AAAA.', '.BBB.', '.BRB.', '.BBB.', '.VVV.', '.VVV.', '.BBB.', '.BRB.', '.BBB.', '.gg..', '.ggg.', '.gg..'],
    leg: ['CCC...', 'CCC...', 'BBB...', 'BRB...', 'BBB...', 'BBB...', 'OOO...', 'OOO...', 'OOO...', 'OOOO..', 'OOOOO.', 'OOOOO.']
  };
  /** Builds a hero from the template: rows to add above (top), single-pixel decorations [x, y, char] (y counts the
   *  added rows too), and whole-row replacements by index. */
  function dress(part, spec) {
    const rows = (spec && spec.top ? spec.top : []).concat(TEMPLATE[part]).map((r) => r.split(''));
    for (const [i, row] of Object.entries((spec && spec.rows) || {})) rows[Number(i) + ((spec.top || []).length)] = row.split('');
    for (const [x, y, ch] of (spec && spec.decor) || []) {
      while (rows[y].length <= x) rows[y].push('.');
      rows[y][x] = ch;
    }
    return rows.map((r) => r.join(''));
  }
  const SKIN = { s: ['#ffe2c6', '#f2c29a', '#dca078', '#b47652'], S: { fixed: '#d29870' }, w: { fixed: '#ffffff' }, p: { fixed: '#2a1a10' }, m: { fixed: '#b0604e' } };
  const GREEN = ['#b8ec7c', '#62b248', '#448e34', '#24561c'];
  const RIB = ['#86cc60', '#48923a', '#33742a', '#1c4416'];
  const LEATHER = ['#c08850', '#8a5430', '#68401f', '#3e2410'];

  CHARS.cactus = {
    name: 'Cactus Man', element: 'nature', color: '#6fd04f', outline: '#12220c', v2: true,
    mat: Object.assign({}, SKIN, {
      H: GREEN, A: GREEN, B: GREEN, R: RIB, x: { fixed: '#fff3a8', bare: true }, L: LEATHER, V: LEATHER, K: { fixed: '#e8c45a' },
      C: ['#7a5438', '#523624', '#3e281a', '#24160e'], O: ['#f4a46c', '#cc6e3a', '#9e5026', '#6a3214'], h: ['#9a6638', '#664020', '#4e3018', '#2e1a0c'],
      g: ['#a6e46f', '#56a83f', '#3c8a30', '#24561d'], f: ['#ffc6de', '#ff80b8', '#d4467f', '#8a2050'], y: { fixed: '#ffe066' }
    }),
    head: dress('head', { top: ['.....ff.......', '....fyyf......', '.....ff.......'], decor: [[1, 4, 'x'], [0, 8, 'x'], [1, 11, 'x'], [4, 3, 'x'], [9, 3, 'x'], [12, 4, 'x']] }),
    torso: dress('torso', { decor: [[0, 2, 'x'], [13, 2, 'x'], [1, 5, 'x'], [12, 6, 'x'], [1, 9, 'x'], [12, 10, 'x']] }),
    arm: dress('arm', { decor: [[4, 2, 'x'], [0, 4, 'x'], [4, 9, 'x']] }),
    leg: dress('leg', { decor: [[3, 3, 'x']] })
  };
  CHARS.ice = {
    name: 'Ice Man', element: 'ice', color: '#7fdcff', outline: '#0e1e36', v2: true,
    mat: Object.assign({}, SKIN, {
      H: ['#d8f4ff', '#7ec4ec', '#4f96cc', '#22578e'], A: ['#eefaff', '#9ad6f4', '#5ea6d6', '#2a649a'], B: ['#d0f0ff', '#74bce6', '#4a8cc4', '#1f5288'],
      R: ['#ffffff', '#c8ecfc', '#92ccee', '#5a98c8'], c: ['#ffffff', '#c0f0ff', '#62c0ee', '#2a7ab4'], L: ['#ffffff', '#c4cedc', '#8e9ab0', '#58637a'],
      V: ['#ffffff', '#c4cedc', '#8e9ab0', '#58637a'], K: { fixed: '#4ff0ff' }, C: ['#4f6eb4', '#283f76', '#1d305e', '#101b38'],
      O: ['#d8f4ff', '#7ec4ec', '#4f96cc', '#22578e'], h: ['#ffffff', '#e4eef8', '#b4c6da', '#7a8ea8'], g: ['#ffffff', '#c4cedc', '#8e9ab0', '#58637a']
    }),
    head: dress('head', { top: ['...c..c..c....', '..cc.cc.cc....', '..cccccccc....'], decor: [[12, 5, 'h'], [13, 6, 'h']] }),
    torso: dress('torso', { top: ['c..........c..', 'cc........cc..'], decor: [[0, 3, 'c'], [12, 3, 'c'], [0, 4, 'c'], [6, 7, 'K']] }),
    arm: dress('arm', { top: ['.c.c.'], decor: [] }),
    leg: dress('leg', { decor: [[4, 7, 'c'], [3, 6, 'c']] }),
    cape: ['#7f98b0', '#dfeaf2', '#ffffff']
  };

  /** Compiles a material mask into a lit, outlined sprite (1 px margin for the outline). dark: -0.3 for far limbs. */
  function shadeSprite(rows, mat, outline, dark) {
    const w = Math.max(...rows.map((r) => r.length)), h = rows.length;
    const at = (x, y) => (y < 0 || y >= h || x < 0 || x >= w ? '.' : (rows[y][x] || '.'));
    const filled = (x, y) => at(x, y) !== '.';
    const c = makeCanvas(w + 2, h + 2), g = c.getContext('2d');
    const tone = (col) => (dark ? shade(col, dark) : col);
    for (let y = -1; y <= h; y++) {
      for (let x = -1; x <= w; x++) {
        const m = at(x, y);
        if (m === '.') {
          // outline: an empty pixel touching a filled one (bare materials such as spines get none)
          const touch = [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dx, dy]) => { const n = at(x + dx, y + dy); return n !== '.' && !(mat[n] && mat[n].bare); });
          if (touch) { g.fillStyle = tone(outline); g.fillRect(x + 1, y + 1, 1, 1); }
          continue;
        }
        const def = mat[m];
        if (!def) continue;
        let col;
        if (def.fixed) col = def.fixed;
        else {
          const up = at(x, y - 1), left = at(x - 1, y);
          const down = filled(x, y + 1), right = filled(x + 1, y);
          const lit = up !== m && !(up !== '.' && mat[up] && mat[up].fixed) || !filled(x - 1, y) && left === '.';
          if (def.length >= 4) {
            // four tones: lit edge, base, a soft second ring toward the shadow side, dark edge
            if (lit) col = def[0];
            else if (!down || !right) col = def[3];
            else if (!filled(x, y + 2) || !filled(x + 2, y) || at(x, y + 1) !== m) col = def[2];
            else col = def[1];
          } else if (lit) col = def[0];
          else if (!down || !right) col = def[2];
          else col = def[1];
        }
        g.fillStyle = tone(col);
        g.fillRect(x + 1, y + 1, 1, 1);
      }
    }
    return c;
  }

  // ability icons, 12 x 12 masks lit the same way
  const ICONS = {
    thorns: { mat: { x: ['#ffffff', '#f4ecc2', '#b8a870'], g: ['#a6e46f', '#56a83f', '#2f6e27'] }, rows: ['.....x......', '.....x......', '.....x......', '....ggg.....', 'xxx.ggg.xxx.', '....ggg.....', '.....x......', '.....x......', '.....x......', '............', '............', '............'] },
    cage: { mat: { g: ['#a6e46f', '#56a83f', '#2f6e27'], x: { fixed: '#f4ecc2' }, s: ['#ffffff', '#cfe8f5', '#94bcd6'] }, rows: ['.g.g.g.g....', '.g.g.g.g....', '.gxgxgxg....', '.g.g.g.g....', '.ggggggg....', '.ggggggg....', '..gggggx....', '..ggggg.....', '.xggggg.....', '..ggggg.....', '..gggggx....', '.ggggggg....'] },
    garden: { mat: { g: ['#a6e46f', '#56a83f', '#2f6e27'], d: ['#d9a35b', '#b47a3a', '#7d5228'] }, rows: ['............', '.g....g...g.', '.g...ggg..g.', 'ggg...g..ggg', '.g...ggg..g.', '.g....g...g.', '.g....g...g.', '.g....g...g.', 'dddddddddddd', 'dddddddddddd', '............', '............'] },
    slide: { mat: { c: ['#ffffff', '#9fe6ff', '#3fb6e8'], i: ['#f0fbff', '#a9def7', '#5f9fcc'] }, rows: ['............', '......cc....', '.......cc...', 'iiiiiiiiccc.', 'iiiiiiiiiccc', 'iiiiiiiiccc.', '.......cc...', '......cc....', '............', 'cccccccccccc', '............', '............'] },
    wall: { mat: { c: ['#ffffff', '#9fe6ff', '#3fb6e8'] }, rows: ['....cccc....', '....cccc....', '....cccc....', '....cccc....', '....cccc....', '....cccc....', '....cccc....', '....cccc....', '....cccc....', '....cccc....', 'cccccccccccc', '............'] },
    freeze: { mat: { c: ['#ffffff', '#bdf3ff', '#5fc8f0'] }, rows: ['.....c......', '...c.c.c....', '....ccc.....', '.c..ccc..c..', '..c..c..c...', 'ccccccccccc.', '..c..c..c...', '.c..ccc..c..', '....ccc.....', '...c.c.c....', '.....c......', '............'] },
    rocket: { mat: { k: ['#6a5650', '#3d2c28', '#1f1411'], x: { fixed: '#ff8a1f' }, X: { fixed: '#ffd23f' } }, rows: ['.....kk.....', '....kkkk....', '...kkkkkk...', '..kkkkkkkk..', '....kkkk....', '....kkkk....', '....kkkk....', '...XxxxxX...', '...xXxXxx...', '....xXxx....', '.....xx.....', '.....x......'] },
    pillar: { mat: { x: { fixed: '#ff8a1f' }, X: { fixed: '#ffd23f' }, r: { fixed: '#c2410c' } }, rows: ['.....X......', '....XxX.....', '....xXx..X..', '...xXxXx.x..', '...xXXXx....', '..rxXXXxr...', '..rxXXXxr...', '.rrxXXXxrr..', '.rxxXXXxxr..', 'rrxxXXXxxrr.', 'rrrrrrrrrrr.', '............'] },
    meteor: { mat: { k: ['#8a7068', '#4d3a35', '#241713'], x: { fixed: '#ff8a1f' }, X: { fixed: '#ffd23f' } }, rows: ['X...........', '.xX.........', '..xxX.......', '...xxX......', '....xxkkk...', '.....kkkkk..', '....xkkkkkk.', '.....kkkkkk.', '.....kkkkkk.', '......kkkk..', '............', '............'] },
    step: { mat: { y: ['#ffffff', '#ffd84a', '#b8861c'], e: { fixed: '#8ff0ff' } }, rows: ['............', '.......yy...', '......yy....', '.....yyyyy..', '....yyyyy...', '......yy....', '.....yy.....', '....y.......', '............', 'e.e.e.e.e.e.', '............', '............'] },
    chain: { mat: { e: ['#ffffff', '#8ff0ff', '#3aa0d0'], y: ['#ffffff', '#ffd84a', '#b8861c'] }, rows: ['yy..........', 'yy.e........', '...ee.......', '....ee..e...', '.....eeee...', '......e.ee..', '..........yy', '..........yy', '.........e..', '........e...', '.......yy...', '.......yy...'] },
    judgement: { mat: { w: ['#ffffff', '#c9cfe0', '#7a8199'], y: ['#ffffff', '#ffd84a', '#b8861c'] }, rows: ['..wwww......', '.wwwwwwww...', 'wwwwwwwwww..', '.wwwwwwww...', '....yy......', '...yy.......', '..yyyyy.....', '....yy......', '...yy.......', '..y.........', '............', '............'] }
  };
  const G3 = ['#a6e46f', '#56a83f', '#2f6e27'], C3 = ['#ffffff', '#9fe6ff', '#3fb6e8'], R3 = ['#ff5a6a', '#c8102e', '#7a0a1c'];
  Object.assign(ICONS, {
    shield: { mat: { g: G3, r: ['#6cbf4f', '#3c8a30', '#26601f'], x: { fixed: '#f4ecc2' } }, rows: ['..gggggggg..', '.gggggggggg.', 'xgggrggrgggx', '.gggrggrggg.', '.gggrggrggg.', 'xgggrggrgggx', '..ggrggrgg..', '..ggrggrgg..', '...grggrg...', '....gggg....', '.....gg.....', '............'] },
    regrow: { mat: { h: ['#ff8a9a', '#e8283c', '#8a1020'], g: G3 }, rows: ['.......gg...', '......ggg...', '..hh.hhg....', '.hhhhhhhh...', '.hhhhhhhh...', '.hhhhhhhh...', '..hhhhhh....', '...hhhh.....', '....hh......', '....g.......', '...gg.......', '............'] },
    thornskin: { mat: { g: G3, x: { fixed: '#f4ecc2' } }, rows: ['x....x....x.', '....ggg.....', 'x..ggggg..x.', '...ggggg....', '.x..ggg..x..', '...ggggg....', 'x.ggggggg.x.', '...ggggg....', '.x.gg.gg.x..', '...gg.gg....', 'x.........x.', '............'] },
    armor: { mat: { c: C3, i: ['#f0fbff', '#c9ecfb', '#7fb6dc'] }, rows: ['..cccccccc..', '.cciiiiiicc.', '.ciiiiiiiic.', '.ciiicciiic.', '.ciiicciiic.', '.ciiiiiiiic.', '..ciiiiiic..', '..cciiiicc..', '...cciicc...', '....cccc....', '.....cc.....', '............'] },
    blizzard: { mat: { w: ['#ffffff', '#c9cfe0', '#7a8199'], c: { fixed: '#ffffff' } }, rows: ['..wwww......', '.wwwwwwww...', 'wwwwwwwwww..', '.wwwwwwww...', '............', '.c..c..c..c.', '..c..c..c...', '.c..c..c..c.', '..c..c..c...', '.c..c..c..c.', '............', '............'] },
    frostbite: { mat: { c: C3, i: ['#ffffff', '#c9ecfb', '#7fb6dc'] }, rows: ['..........cc', '.........ccc', '........ccc.', '.......ccc..', '......ccc...', '.....ccc....', '..i.ccc.....', '..iiic......', '...ii.......', '..i..i......', '.i....i.....', '............'] },
    firedash: { mat: { k: ['#8a7068', '#4d3a35', '#241713'], x: { fixed: '#ff8a1f' }, X: { fixed: '#ffd23f' } }, rows: ['............', '.......kk...', '.x.x....kk..', 'xXxXxkkkkkk.', 'XxXxXkkkkkkk', 'xXxXxkkkkkk.', '.x.x....kk..', '.......kk...', '............', '............', '............', '............'] },
    inferno: { mat: { k: ['#8a7068', '#4d3a35', '#241713'], x: { fixed: '#ff8a1f' }, X: { fixed: '#ffd23f' } }, rows: ['...x.xx.x...', '..xXxXXxXx..', '.xX......Xx.', 'xX........Xx', 'X..........X', 'X....kk....X', 'X...kkkk...X', 'xX..kkkk..Xx', '.xX..kk..Xx.', '..xXxXXxXx..', '...x.xx.x...', '............'] },
    kindling: { mat: { k: ['#9a9aa6', '#4a4a52', '#2a2a30'], x: { fixed: '#ff8a1f' }, X: { fixed: '#ffd23f' }, w: ['#9a6a3a', '#6b4224', '#40250f'] }, rows: ['.....x......', '....xXx.....', '....xXx.....', '...kkkkk....', '..kkkkkkk...', '..kkkkkkk...', '...kkkkk....', '.....w......', '....w.......', '...w........', '..w.........', '............'] },
    clap: { mat: { y: ['#ffffff', '#ffd84a', '#b8861c'] }, rows: ['............', '.y........y.', 'y..y....y..y', 'y.y..yy..y.y', 'y.y.yyyy.y.y', 'y.y.yyyy.y.y', 'y.y..yy..y.y', 'y..y....y..y', '.y........y.', '............', '............', '............'] },
    static: { mat: { y: ['#ffffff', '#ffd84a', '#b8861c'], e: { fixed: '#8ff0ff' } }, rows: ['.e.......e..', '......yy....', '.....yy.....', '....yyyyy...', '.e...yyy..e.', '.....yy.....', '....yy......', '...y........', '.e.......e..', '............', '............', '............'] },
    bloodpool: { mat: { r: R3 }, rows: ['.....r......', '.....r......', '....rrr.....', '...rrrrr....', '...rrrrr....', '....rrr.....', '............', '..rrrrrrr...', '.rrrrrrrrrr.', '..rrrrrrrr..', '............', '............'] },
    thirst: { mat: { k: R3, W: { fixed: '#ffffff' }, r: { fixed: '#ff2a3a' } }, rows: ['............', '.kkkkkkkkkk.', '.kWWkkkkWWk.', '.kWWkkkkWWk.', '..WWk..kWW..', '..WW....WW..', '...W....W...', '...W....W...', '...r....r...', '...r....r...', '............', '............'] },
    bat: { mat: { k: ['#6a5a7a', '#3a2a4a', '#1a1024'], e: { fixed: '#ff2a3a' } }, rows: ['............', 'k....kk....k', 'kk..kkkk..kk', 'kkk.kekk.kkk', 'kkkkkkkkkkkk', 'kkkkkkkkkkkk', '.kk.kkkk.kk.', '..k..kk..k..', '.....kk.....', '............', '............', '............'] },
    rewind: { mat: { e: ['#ffffff', '#5afff0', '#16705c'], m: ['#eef2f8', '#a7b1c4', '#5e6880'] }, rows: ['....eeee....', '..ee....ee..', '.e........e.', 'e..m.......e', 'e..mm......e', 'e..mmm.....e', 'e..mm......e', 'e..m.......e', '.e........e.', '..ee....e...', '....eeee.e..', '.........e..'] },
    decoy: { mat: { m: ['#eef2f8', '#a7b1c4', '#5e6880'], e: { fixed: '#5afff0' } }, rows: ['..mm....ee..', '.mmmm..eeee.', '..mm....ee..', '.mmmm..eeee.', 'm.mm.me.ee.e', '..mm....ee..', '.m..m..e..e.', '.m..m..e..e.', '............', '............', '............', '............'] },
    window: { mat: { e: ['#ffffff', '#5afff0', '#16705c'] }, rows: ['eeeeeeeeeeee', 'e..........e', 'e.eeeeeeee.e', 'e..........e', 'eeeeeeeeeeee', '............', '...e....e...', '...e....e...', '..eee..eee..', '............', '............', '............'] },
    drain: { mat: { h: ['#ff8a9a', '#e8283c', '#8a1020'], r: { fixed: '#ff2a3a' } }, rows: ['hh.hh.......', 'hhhhh.......', '.hhh..r.....', '..h..r.r....', '....r...r..r', '...r.....rr.', '............', '............', '............', '............', '............', '............'] }
  });
  const ABILITY_ICONS = { cactus: ['thorns', 'cage', 'garden'], ice: ['slide', 'wall', 'freeze'], fire: ['rocket', 'pillar', 'meteor'], thunder: ['step', 'chain', 'judgement'], vampire: ['bloodpool', 'thirst', 'bat'], patch: ['rewind', 'decoy', 'window'] };
  const iconCache = {};
  function iconOf(id) {
    if (!ICONS[id]) id = 'static';
    if (!iconCache[id]) iconCache[id] = shadeSprite(ICONS[id].rows, ICONS[id].mat, '#0b0a14');
    return iconCache[id];
  }

  const WEAPONS = {
    nail: {
      pal: { o: '#1b2a12', w: '#7a4a24', W: '#a8683a', a: '#58a83f', A: '#2f6e27', h: '#9adf6c', x: '#f4ecc2' },
      rows: [
        '..........x.x...',
        '........x.aaa.x.',
        '.......xaahhaa..',
        'wwwwwwwaahaaaaAx',
        'WWWWWWWaaaaaaAA.',
        '.......xaAAAAA..',
        '........x.AAx...',
        '..........x.....'
      ], pivot: [2, 3.5]
    },
    thrower: {
      pal: { o: '#1b2a12', w: '#7a4a24', W: '#a8683a', a: '#58a83f', A: '#2f6e27', h: '#9adf6c', x: '#f4ecc2' },
      rows: [
        '.........x.x..',
        '.......x.aaa.x',
        'wwwwwwwoahhaao',
        'WWWWWWWoaaaAAo',
        '.......x.AAA.x',
        '.........x.x..'
      ], pivot: [2, 2.5]
    },
    frostblade: {
      pal: { o: '#14243d', h: '#ffffff', a: '#bdeaff', A: '#6fb6e0', g: '#2b4f78', G: '#f5c842' },
      rows: [
        '...o..................',
        '..oGo..ooooooooooooo..',
        'oggGggohhhhhhhhhhhhhho',
        'oggGggoaaaaaaaaaaaaAAo',
        '..oGo..oAAAAAAAAAAAoo.',
        '...o...........oooo...'
      ], pivot: [2, 2.5]
    },
    icestaff: {
      pal: { o: '#14243d', w: '#4a6b8a', W: '#7fa3c2', h: '#ffffff', a: '#9fe6ff', A: '#3fb6e8' },
      rows: [
        '................oo..',
        '...............ohao.',
        'wwwwwwwwwwwwwwohhaAo',
        'WWWWWWWWWWWWWWoaaAAo',
        '...............oAAo.',
        '................oo..'
      ], pivot: [2, 2.5]
    },
    flameaxe: {
      pal: { o: '#120806', w: '#5a3a28', W: '#7d5538', a: '#4a4a52', A: '#2a2a30', h: '#9a9aa6', x: '#ff8a1f', X: '#ffd23f' },
      rows: [
        '...........oooo..',
        '..........ohhaXo.',
        '..........ohaaxXo',
        'wwwwwwwwwwoaaaaxo',
        'WWWWWWWWWWoaaaaxo',
        '..........oAaaxXo',
        '..........oAAAXo.',
        '...........oooo..'
      ], pivot: [2, 3.5]
    },
    firewand: {
      pal: { o: '#120806', w: '#3d2c28', W: '#66504a', x: '#ff8a1f', X: '#ffd23f', r: '#c2410c' },
      rows: [
        '...........oo.',
        '..........oXxo',
        'wwwwwwwwwwoxXo',
        'WWWWWWWWWWoxro',
        '...........oo.'
      ], pivot: [2, 2.5]
    },
    hammer: {
      pal: { o: '#16122b', w: '#6b4a2a', W: '#8f6a3f', a: '#9aa2b8', A: '#5c6378', h: '#e6eaf5', x: '#f5c842' },
      rows: [
        '............oooooo',
        '...........ohhhhAo',
        '...........ohaaaAo',
        'wwwwwwwwwwwoxxxxxo',
        'WWWWWWWWWWWoaaaaAo',
        '...........oaaaAAo',
        '...........oAAAAAo',
        '............oooooo'
      ], pivot: [2, 3.5]
    },
    stormrod: {
      pal: { o: '#16122b', w: '#b8861c', W: '#f5c842', e: '#8ff0ff', h: '#ffffff' },
      rows: [
        '.............o.',
        '............oeo',
        'wwwwwwwwwwwwohe',
        'WWWWWWWWWWWWoeo',
        '.............o.'
      ], pivot: [2, 2.5]
    }
  };

  WEAPONS.claws = {
    pal: { o: '#0e0710', w: '#ffffff', W: '#c9c2d6', r: '#c8102e' },
    rows: ['.....ow...', '.rr..owW..', 'rrrroowWW.', 'rrrrooowWW', '.rr..owW..', '.....ow...'], pivot: [2, 2.5]
  };
  WEAPONS.bloodbolt = {
    pal: { o: '#0e0710', w: '#3a2240', W: '#5a3a5e', r: '#c8102e', R: '#ff5a6a', g: '#e0b850' },
    rows: ['..........oo.', '.........oRro', 'wwwwwwwwgorro', 'WWWWWWWWgorRo', '..........oo.'], pivot: [2, 2.5]
  };
  Object.assign(WEAPONS, {
    sword: {
      pal: { o: '#1a1a24', w: '#6b4a2a', W: '#8f6a3f', h: '#ffffff', a: '#c9cfe0', A: '#7a8199', g: '#e0b850' },
      rows: ['......o.............', '.....ogo..ooooooooo.', 'wwwwwogoohhhhhhhhhho', 'WWWWWogooaaaaaaaaAAo', '.....ogo..oAAAAAAoo.', '......o.............'], pivot: [2, 2.5]
    },
    bow: {
      pal: { o: '#2a1a0a', w: '#8f6a3f', W: '#6b4a2a', s: '#f2eef8' },
      rows: ['...ow.....', '..ow.s....', '.ow..s....', 'ow...s....', 'ow...s....', 'ow...s....', 'ow...s....', '.ow..s....', '..ow.s....', '...ow.....'], pivot: [1, 5]
    },
    pixelstick: {
      pal: { o: '#0b1418', m: '#a7b1c4', M: '#5e6880', t: '#2ec4a0', T: '#9fffe8' },
      rows: ['.............oooo', '............oTTto', 'mmmmmmmmmmmmotTto', 'MMMMMMMMMMMMottto', '............oooo.'], pivot: [2, 2.5]
    },
    bubble: {
      pal: { o: '#0b1418', m: '#a7b1c4', M: '#5e6880', t: '#2ec4a0', b: '#bff7ff' },
      rows: ['....oooooo..ob.', '..oommmmmmoob.b', 'oommttttmmmmmo.', 'oMMMMMMMMMMMMo.', '..oMMo.........', '..oMMo.........'], pivot: [3, 3]
    },
    magnet: {
      pal: { o: '#0b1418', r: '#ff4d6a', R: '#a8203a', m: '#eef2f8', M: '#a7b1c4' },
      rows: ['......oooo..', '.....orrrro.', 'mmmmmorRoooo', 'MMMMMorRo...', 'mmmmmorRoooo', '.....omMMmo.', '......oooo..'], pivot: [2, 3]
    },
    glitch: {
      pal: { o: '#0b1418', m: '#a7b1c4', M: '#5e6880', p: '#b06bff', P: '#e0c0ff' },
      rows: ['.....ooooooooo.', '....ommmmmmmmop', 'mmmmommpPpmmmoP', 'MMMMoMMMMMMMMop', '....oMMo.......', '....oooo.......'], pivot: [3, 3]
    }
  });
  const KIT_WEAPONS = { cactus: ['nail', 'thrower'], ice: ['frostblade', 'icestaff'], fire: ['flameaxe', 'firewand'], thunder: ['hammer', 'stormrod'], vampire: ['claws', 'bloodbolt'] };

  const sprites = {};
  function partsOf(id) {
    if (sprites[id]) return sprites[id];
    const ch = CHARS[id];
    sprites[id] = {
      head: shadeSprite(ch.head, ch.mat, ch.outline),
      torso: shadeSprite(ch.torso, ch.mat, ch.outline),
      arm: shadeSprite(ch.arm, ch.mat, ch.outline),
      armBack: shadeSprite(ch.arm, ch.mat, ch.outline, -0.32),
      leg: shadeSprite(ch.leg, ch.mat, ch.outline),
      legBack: shadeSprite(ch.leg, ch.mat, ch.outline, -0.32)
    };
    return sprites[id];
  }
  const weaponSprites = {};
  function weaponOf(id) {
    if (!WEAPONS[id]) return null;
    if (!weaponSprites[id]) weaponSprites[id] = compile(WEAPONS[id].rows, WEAPONS[id].pal);
    return weaponSprites[id];
  }

  // Draws a sprite with the given pixel of its mask (sprites carry a 1 px outline margin) at (x, y), rotated.
  function drawPart(g, img, x, y, px, py, rot) {
    g.save();
    g.translate(Math.round(x), Math.round(y));
    if (rot) g.rotate(rot);
    g.drawImage(img, -(px + 1), -(py + 1));
    g.restore();
  }

  /**
   * Draws a character with its feet at (x, y). The body is about 14 x 32 px.
   * s: { facing: 1|-1, aim (radians, world), t (seconds), run (0..1 speed), air (bool), vy,
   *      weapon (0|1), swing (-1 or 0..1 progress), cast (0..1 glow), hurt (0..1 flash), frozen (bool), alpha }
   */
  function drawBat(g, x, y, t, facing) {
    const flap = Math.sin(t * 22);
    const cx = Math.round(x), cy = Math.round(y - 18 + Math.sin(t * 6) * 2);
    g.save();
    g.translate(cx, cy);
    g.scale(facing || 1, 1);
    const wing = flap > 0 ? -5 : 3;
    g.fillStyle = '#0e0710';
    g.beginPath(); g.moveTo(-2, 0); g.lineTo(-13, wing); g.lineTo(-9, 3); g.lineTo(-6, 1); g.lineTo(-3, 4); g.closePath(); g.fill();
    g.beginPath(); g.moveTo(2, 0); g.lineTo(13, wing); g.lineTo(9, 3); g.lineTo(6, 1); g.lineTo(3, 4); g.closePath(); g.fill();
    g.fillStyle = '#3a2a4a';
    g.beginPath(); g.moveTo(-2, 1); g.lineTo(-11, wing + 1); g.lineTo(-6, 2); g.closePath(); g.fill();
    g.beginPath(); g.moveTo(2, 1); g.lineTo(11, wing + 1); g.lineTo(6, 2); g.closePath(); g.fill();
    g.fillStyle = '#1a1024'; g.fillRect(-3, -3, 6, 7);
    g.fillRect(-3, -5, 1, 2); g.fillRect(2, -5, 1, 2);
    g.fillStyle = '#ff2a3a'; g.fillRect(0, -2, 1, 1); g.fillRect(2, -2, 1, 1);
    g.restore();
    glow(g, cx, cy, 12, '#ff2a3a', 0.25);
  }

  function drawCharacter(g, id, x, y, s) {
    if (s.bat) { drawBat(g, x, y, s.t || 0, s.facing); return; }
    const ch = CHARS[id], p = partsOf(id);
    const f = s.facing || 1;
    const t = s.t || 0;
    // aim in the character's own frame (facing right): 0 forward, negative up
    const aim = s.aim == null ? 0 : s.aim;
    let local = f > 0 ? aim : Math.PI - aim;
    while (local > Math.PI) local -= Math.PI * 2;
    while (local < -Math.PI) local += Math.PI * 2;
    const run = s.frozen ? 0 : (s.run || 0);
    const phase = t * 12;
    const swingLeg = s.air ? 0 : Math.sin(phase) * 0.6 * run;
    const bob = s.air || run < 0.1 ? 0 : (Math.abs(Math.cos(phase)) > 0.6 ? -1 : 0);
    const breathe = run < 0.1 && !s.air ? (Math.sin(t * 2.4) > 0.3 ? -1 : 0) : 0;

    g.save();
    g.globalAlpha = s.alpha == null ? 1 : s.alpha;
    g.translate(Math.round(x), Math.round(y));
    g.scale(f, 1);
    const by = bob;
    const L = layoutOf(id);
    const hipY = -L.leg;
    const torsoTop = hipY - L.torso + 1 + by;
    const shoulderY = torsoTop + 3;

    if (ch.cape) drawCape(g, t, run, s.air, torsoTop, ch.cape);

    let backLeg = -swingLeg, frontLeg = swingLeg;
    if (s.air) { backLeg = s.vy < 0 ? -0.55 : 0.3; frontLeg = s.vy < 0 ? 0.65 : -0.25; }
    drawPart(g, p.legBack, -2, hipY, 1.5, 0, backLeg);
    const backArm = s.air ? -1.0 : -swingLeg * 0.9;
    drawPart(g, p.armBack, -1, shoulderY - L.armTop, L.armPivot, 1 + L.armTop, backArm);
    drawPart(g, p.torso, 0, torsoTop - L.torsoTop, L.torsoMid, 0, 0);
    drawPart(g, p.leg, 0, hipY, 1.5, 0, frontLeg);

    // head follows the aim a little (the head turns smoothly with the view)
    const tilt = Math.max(-0.3, Math.min(0.3, local * 0.28));
    drawPart(g, p.head, 0, torsoTop + 1 + breathe, L.headMid, ch.head.length - 1, tilt);
    if (id === 'fire') drawFlameHair(g, t, torsoTop + breathe, tilt);
    if (id === 'ice' && hash(Math.floor(t * 6), 3, 9) > 0.75) {
      g.fillStyle = '#ffffff';
      g.fillRect(Math.round(hash(Math.floor(t * 6), 1, 2) * 14 - 7), torsoTop - 12 + Math.round(hash(Math.floor(t * 6), 2, 2) * 8), 1, 1);
    }

    // front arm and weapon
    const weaponId = s.weaponId || (KIT_WEAPONS[id] || [])[s.weapon || 0];
    const wdef = weaponId ? WEAPONS[weaponId] : null;
    const melee = s.melee != null ? s.melee : (s.weapon || 0) === 0;
    const swinging = s.swing != null && s.swing >= 0 && melee;
    let armAngle;          // direction the arm points, own frame (0 forward)
    if (swinging) armAngle = lerp(-2.6, 0.9, easeOut(s.swing));
    else if (!melee) armAngle = local;
    else armAngle = s.air ? -0.5 : 0.95 + swingLeg * 0.5;
    g.save();
    g.translate(1, shoulderY);
    g.rotate(armAngle - Math.PI / 2);
    if (wdef) {
      const img = weaponOf(weaponId);
      g.save();
      g.translate(0, L.hand);
      g.rotate(Math.PI / 2 + (melee && !swinging ? -1.25 : 0));
      g.drawImage(img, -wdef.pivot[0], -wdef.pivot[1]);
      g.restore();
    }
    g.drawImage(p.arm, -L.armPivot - 1, -2 - L.armTop);
    g.restore();

    // swing trail, Terraria style
    if (swinging && s.swing < 0.9) {
      const k = easeOut(s.swing);
      const a1 = lerp(-2.6, 0.9, Math.max(0, k - 0.4)), a2 = lerp(-2.6, 0.9, k);
      g.save();
      g.strokeStyle = ch.color;
      g.globalAlpha *= 0.6 * (1 - s.swing);
      for (const [r, wdt] of [[20, 3], [16, 1]]) {
        g.lineWidth = wdt;
        g.beginPath();
        g.arc(1, shoulderY, r, a1, a2);
        g.stroke();
      }
      g.restore();
    }
    g.restore();

    // status overlays in world space
    if (s.frozen) {
      g.save();
      g.globalAlpha = 0.55;
      g.fillStyle = '#bfefff';
      g.fillRect(Math.round(x) - 9, Math.round(y) - 34, 18, 34);
      g.globalAlpha = 0.9;
      g.strokeStyle = '#ffffff';
      g.strokeRect(Math.round(x) - 9 + 0.5, Math.round(y) - 34 + 0.5, 17, 33);
      g.restore();
    }
    if (s.hurt > 0) {
      g.save();
      g.globalCompositeOperation = 'lighter';
      g.globalAlpha = s.hurt * 0.7;
      g.fillStyle = '#ff3030';
      g.fillRect(Math.round(x) - 8, Math.round(y) - 33, 16, 33);
      g.restore();
    }
    if (s.cast > 0) glow(g, x, y - 16, 28, ch.color, s.cast * 0.7);
  }
  const layouts = {};
  /** Where the parts join, from the sprite sizes: v2 heroes are taller than the first ones. */
  function layoutOf(id) {
    if (layouts[id]) return layouts[id];
    const ch = CHARS[id];
    const armTop = ch.v2 && id === 'ice' ? 1 : 0;
    const torsoTop = ch.v2 ? ch.torso.length - TEMPLATE.torso.length : 0;
    return (layouts[id] = {
      leg: ch.leg.length,
      torso: ch.torso.length - torsoTop,
      torsoTop,
      torsoMid: ch.v2 ? 7 : 6,
      headMid: ch.v2 ? 6 : 6,
      armPivot: ch.v2 ? 2 : 1.5,
      armTop,
      hand: ch.arm.length - armTop - (ch.v2 ? 3 : 2)
    });
  }
  /** The sprite's height above the feet, for name tags and effects. */
  function heightOf(id) { const L = layoutOf(id); return L.leg + L.torso - 1 + CHARS[id].head.length - 1; }

  function easeOut(k) { return 1 - Math.pow(1 - Math.min(1, Math.max(0, k)), 2.2); }

  function drawCape(g, t, run, air, torsoTop, colors) {
    const wave = Math.sin(t * 6) * 1.5 + run * 4 + (air ? 3 : 0);
    const top = torsoTop + 2;
    g.fillStyle = colors[0];
    g.beginPath();
    g.moveTo(-4, top); g.lineTo(2, top);
    g.lineTo(-3 - wave * 0.5, -2); g.lineTo(-10 - wave, 0); g.closePath(); g.fill();
    g.fillStyle = colors[1];
    g.beginPath();
    g.moveTo(-3, top); g.lineTo(1, top);
    g.lineTo(-3 - wave * 0.5, -3); g.lineTo(-8 - wave, -2); g.closePath(); g.fill();
    g.fillStyle = colors[2];
    g.fillRect(-3, top, 3, 2);
  }

  function drawFlameHair(g, t, top, tilt) {
    for (let i = 0; i < 30; i++) {
      const life = (t * 2.0 + hash(i, 1, 5)) % 1;
      const spread = (1 - life * 0.6) * 10;
      const px = Math.round((hash(i, 2, 5) - 0.55) * spread + Math.sin(t * 7 + i * 7.1) * 1.2 - life * 4 + tilt * 10);
      const py = Math.round(top - 6 - life * 12);
      g.fillStyle = life < 0.25 ? '#fff6a8' : life < 0.5 ? '#ffc23a' : life < 0.8 ? '#ff6a1f' : '#9e2a0c';
      const sz = life < 0.45 ? 2 : 1;
      g.fillRect(px, py, sz, sz);
    }
  }

  // ------------------------------------------------------------------ glow and effects
  const glowCache = {};
  function glowSprite(color) {
    if (glowCache[color]) return glowCache[color];
    const c = makeCanvas(64, 64), gg = c.getContext('2d');
    const gr = gg.createRadialGradient(32, 32, 0, 32, 32, 32);
    gr.addColorStop(0, color);
    gr.addColorStop(0.35, color + '88');
    gr.addColorStop(1, color + '00');
    gg.fillStyle = gr;
    gg.fillRect(0, 0, 64, 64);
    return (glowCache[color] = c);
  }
  function glow(g, x, y, r, color, a) {
    g.save();
    g.globalCompositeOperation = 'lighter';
    g.globalAlpha = a == null ? 1 : a;
    g.drawImage(glowSprite(color), x - r, y - r, r * 2, r * 2);
    g.restore();
  }

  function drawLightning(g, x1, y1, x2, y2, seed, color, width) {
    const n = Math.max(3, Math.round(Math.hypot(x2 - x1, y2 - y1) / 10));
    const pts = [[x1, y1]];
    const nx = -(y2 - y1), ny = x2 - x1, len = Math.hypot(nx, ny) || 1;
    for (let i = 1; i < n; i++) {
      const k = i / n, off = (hash(i, seed, 77) - 0.5) * 14;
      pts.push([lerp(x1, x2, k) + nx / len * off, lerp(y1, y2, k) + ny / len * off]);
    }
    pts.push([x2, y2]);
    g.save();
    g.lineJoin = 'miter';
    for (const [w, c] of [[(width || 2) + 3, (color || '#7fe3ff') + '66'], [width || 2, color || '#7fe3ff'], [1, '#ffffff']]) {
      g.strokeStyle = c; g.lineWidth = w;
      g.beginPath();
      pts.forEach(([px, py], i) => (i ? g.lineTo(Math.round(px), Math.round(py)) : g.moveTo(Math.round(px), Math.round(py))));
      g.stroke();
    }
    g.restore();
  }

  const PROJ = {
    spine: { pal: { o: '#3b3320', x: '#f4ecc2', X: '#cbbd86' }, rows: ['oxxxXo'], glow: null },
    cactusbomb: { pal: { o: '#183014', a: '#4fa03c', A: '#2e6e27', h: '#8fd463', x: '#f4ecc2' }, rows: ['..x.x..', '.oaaao.', 'xohhaAx', '.oaaAo.', 'xoaAAox', '.ooooo.', '..x.x..'], glow: null },
    shard: { pal: { o: '#2b4f78', h: '#ffffff', a: '#9fe6ff', A: '#3fb6e8' }, rows: ['..ohho..', 'oaaaahho', '..oAAo..'], glow: '#7fe3ff' },
    fireball: { pal: { r: '#c2410c', x: '#ff8a1f', X: '#ffd23f', w: '#fff7c2' }, rows: ['..rxx..', '.rxXXx.', 'rxXwwXx', 'rxXwwXx', '.rxXXx.', '..rxx..'], glow: '#ff8a1f' },
    blood: { pal: { o: '#4a0410', r: '#c8102e', R: '#ff5a6a' }, rows: ['..orro.', 'orrrRRo', 'orrrrRo', '..orro.'], glow: '#ff2a3a' },
    arrow: { pal: { w: '#8f6a3f', h: '#e6eaf5', f: '#ff5a5a' }, rows: ['f......h.', 'fwwwwwwwh', 'f......h.'], glow: null },
    bubble: { pal: { o: '#7fe3ff', w: '#ffffff', b: 'rgba(191,247,255,0.35)' }, rows: ['..ooo..', '.obbwo.', 'obbbbwo', 'obbbbbo', 'obbbbbo', '.obbbo.', '..ooo..'], glow: '#7fe3ff' },
    cube: { pal: { o: '#2a0a4a', p: '#b06bff', P: '#e0c0ff' }, rows: ['oooooo', 'oPPppo', 'oPpppo', 'opppPo', 'oppPPo', 'oooooo'], glow: '#b06bff' }
  };
  const projSprites = {};
  function drawProjectile(g, kind, x, y, vx, vy, t) {
    const def = PROJ[kind];
    if (!def) return;
    if (!projSprites[kind]) projSprites[kind] = compile(def.rows, def.pal);
    const img = projSprites[kind];
    if (def.glow) glow(g, x, y, kind === 'fireball' ? 18 : 12, def.glow, 0.8);
    g.save();
    g.translate(Math.round(x), Math.round(y));
    if (kind === 'cactusbomb' || kind === 'cube') g.rotate(t * 8);
    else if (kind === 'bubble') g.rotate(0);
    else g.rotate(Math.atan2(vy, vx));
    g.drawImage(img, -Math.floor(img.width / 2), -Math.floor(img.height / 2));
    g.restore();
    if (kind === 'fireball') {
      for (let i = 1; i < 6; i++) {
        const k = i / 6;
        g.fillStyle = i < 2 ? '#ffd23f' : i < 4 ? '#ff8a1f' : '#a8300f';
        const sp = Math.hypot(vx, vy) || 1;
        g.fillRect(Math.round(x - vx / sp * i * 3 + (hash(i, Math.floor(t * 20), 3) - 0.5) * 3), Math.round(y - vy / sp * i * 3 + (hash(i, Math.floor(t * 20), 4) - 0.5) * 3), 2, 2);
      }
    }
  }

  // ------------------------------------------------------------------ HUD sprites
  const HEART = ['.oo.oo.', 'orrorro', 'orwrrro', 'orrrrro', '.orrro.', '..oro..', '...o...'];
  const hud = {};
  function heartSprite(kind) { // full, half, empty
    if (hud[kind]) return hud[kind];
    const pal = kind === 'empty' ? { o: '#2a0f14', r: '#4a2a30', w: '#5a3a40' } : { o: '#4a0d14', r: '#e8283c', w: '#ffb3bb' };
    let rows = HEART;
    if (kind === 'half') rows = HEART.map((r) => r.slice(0, 4) + r.slice(4).replace(/[rw]/g, 'e'));
    const p = Object.assign({ e: '#4a2a30' }, pal);
    return (hud[kind] = compile(rows, p));
  }
  function drawHearts(g, x, y, hp, max, scale) {
    const sc = scale || 1;
    for (let i = 0; i < max / 2; i++) {
      const v = hp - i * 2;
      const img = heartSprite(v >= 2 ? 'full' : v === 1 ? 'half' : 'empty');
      g.drawImage(img, x + i * 8 * sc, y, img.width * sc, img.height * sc);
    }
  }

  // ------------------------------------------------------------------ pixel digits
  // A 3 x 5 pixel font for numbers (timer, score, cooldowns): crisp at any scale, unlike a web font at 8 px.
  const DIGITS = {
    '0': ['111', '101', '101', '101', '111'], '1': ['010', '110', '010', '010', '111'], '2': ['111', '001', '111', '100', '111'],
    '3': ['111', '001', '011', '001', '111'], '4': ['101', '101', '111', '001', '001'], '5': ['111', '100', '111', '001', '111'],
    '6': ['111', '100', '111', '101', '111'], '7': ['111', '001', '010', '010', '010'], '8': ['111', '101', '111', '101', '111'],
    '9': ['111', '101', '111', '001', '111'], ':': ['0', '1', '0', '1', '0'], '-': ['000', '000', '111', '000', '000'], ' ': ['0', '0', '0', '0', '0']
  };
  /** Draws a number string; align 'left' | 'center' | 'right'; scale is pixels per font pixel. */
  function drawDigits(g, text, x, y, scale, color, align) {
    const sc = scale || 1;
    const width = [...text].reduce((w, ch) => w + ((DIGITS[ch] || DIGITS[' '])[0].length + 1) * sc, -sc);
    let cx = Math.round(align === 'center' ? x - width / 2 : align === 'right' ? x - width : x);
    const draw = (col, dx, dy) => {
      g.fillStyle = col;
      for (const ch of text) {
        const glyph = DIGITS[ch] || DIGITS[' '];
        for (let r = 0; r < 5; r++) for (let c = 0; c < glyph[r].length; c++) if (glyph[r][c] === '1') g.fillRect(cx + c * sc + dx, Math.round(y) + r * sc + dy, sc, sc);
        cx += (glyph[0].length + 1) * sc;
      }
    };
    const start = cx;
    draw('#000000', Math.max(1, sc / 2), Math.max(1, sc / 2));
    cx = start;
    draw(color, 0, 0);
  }

  // ------------------------------------------------------------------ tiles
  const THEMES = {
    desert: {
      ground: ['#c98f4a', '#b47a3a', '#d9a35b', '#9e6a33'], top: ['#f2d08f', '#e8be72'], edge: '#5e3d1d', deep: '#7d5228',
      platform: ['#8a5a32', '#6b4224', '#b07a48'], mud: ['#6b4a2e', '#553a22', '#8a6440'],
      sky: ['#3d6fb5', '#8fb6d9', '#f6cf8a'], glowTint: '#ffb84a'
    },
    volcano: {
      ground: ['#3d3238', '#2f262b', '#4a3d42', '#26202a'], top: ['#6b5a5e', '#57484c'], edge: '#140d10', deep: '#1c1418',
      platform: ['#2a2328', '#1a1518', '#ff7a1f'], mud: ['#3a2a20', '#2a1d15', '#5a4030'],
      sky: ['#12060a', '#3a0f10', '#8a2a12'], glowTint: '#ff5a1f'
    },
    antarctica: {
      ground: ['#6f8fae', '#5f7f9e', '#7f9fbe', '#56708c'], top: ['#ffffff', '#e2f2fb'], edge: '#2a3f57', deep: '#4a6280',
      platform: ['#a9d8ef', '#6fb0d6', '#e6f7ff'], mud: ['#5a6a7a', '#4a5a6a', '#7a8a9a'],
      sky: ['#05091c', '#10234a', '#1f4a6a'], glowTint: '#7fe3ff'
    },
    castle: {
      ground: ['#6d6a7a', '#5d5a6a', '#7d7a8a', '#54516a'], top: ['#5f9a3a', '#4a7f2e'], edge: '#24222e', deep: '#3e3b4c',
      platform: ['#8a5a32', '#6b4224', '#b07a48'], mud: ['#4a3a2a', '#3a2a1a', '#6a5a4a'],
      sky: ['#1a1838', '#3a2f63', '#a0688a'], glowTint: '#ffcf6a'
    }
  };
  const SOLID = '#~MPwc';
  function isSolid(c) { return SOLID.indexOf(c) >= 0; }

  /** Pre-renders the static tiles of a map (everything but lava, pads and dynamic blocks) into a canvas. */
  function renderTiles(theme, cells) {
    const th = THEMES[theme];
    const c = makeCanvas(W, H), g = c.getContext('2d');
    const at = (x, y) => (x < 0 || y < 0 || x >= COLS || y >= ROWS ? '#' : cells[y * COLS + x]);
    for (let ty = 0; ty < ROWS; ty++) {
      for (let tx = 0; tx < COLS; tx++) {
        const ch = at(tx, ty);
        const ox = tx * T, oy = ty * T;
        if (ch === '#' || ch === 'M' || ch === 'P') paintGround(g, th, theme, at, tx, ty, ch);
        else if (ch === '~') paintIce(g, at, tx, ty);
        else if (ch === '=') paintPlatform(g, th, theme, ox, oy, at(tx - 1, ty) !== '=', at(tx + 1, ty) !== '=');
        else if (ch === '^') paintSpikes(g, ox, oy, tx);
        else if (ch === 'C') paintCactus(g, ox, oy, tx);
        else if (ch === 'I') paintIcicles(g, ox, oy, tx);
      }
    }
    return c;
  }

  function paintGround(g, th, theme, at, tx, ty, ch) {
    const ox = tx * T, oy = ty * T;
    const up = !isSolid(at(tx, ty - 1)) && at(tx, ty - 1) !== '=' ? true : !isSolid(at(tx, ty - 1));
    const down = !isSolid(at(tx, ty + 1)), left = !isSolid(at(tx - 1, ty)), right = !isSolid(at(tx + 1, ty));
    // depth from the surface darkens the ground
    let depth = 0;
    for (let k = 1; k <= 3; k++) { if (isSolid(at(tx, ty - k)) && isSolid(at(tx - 1, ty - k + 1)) && isSolid(at(tx + 1, ty - k + 1))) depth++; else break; }
    for (let py = 0; py < T; py++) {
      for (let px = 0; px < T; px++) {
        const gx = ox + px, gy = oy + py;
        const n = theme === 'castle' ? hash(gx, gy, 11) : hash(gx >> 2, gy >> 2, 11) * 0.55 + hash(gx >> 1, gy >> 1, 12) * 0.45;
        let col = th.ground[n < 0.45 ? 0 : n < 0.62 ? 1 : n < 0.75 ? 2 : 3];
        if (theme === 'castle') col = brick(gx, gy, th, n);
        if (theme === 'desert') { const band = Math.sin(gy * 0.55 + Math.sin(gx * 0.08) * 2); if (band > 0.75) col = th.ground[2]; else if (band < -0.85) col = th.ground[1]; }
        if (theme === 'volcano' && hash(gx >> 2, gy >> 2, 31) > 0.95 && hash(gx >> 1, gy >> 1, 33) > 0.4) col = '#6a2412';
        if (theme === 'volcano' && hash(gx >> 1, gy >> 1, 32) > 0.993) col = '#ff7a1f';
        if (theme === 'antarctica' && hash(gx >> 2, gy >> 1, 41) > 0.93) col = '#8fb2cc';
        if (depth >= 2) col = mix(col, th.deep, 0.35 + (depth - 2) * 0.25);
        if (ch === 'M') col = th.mud[n < 0.6 ? 0 : n < 0.9 ? 1 : 2];
        if (ch === 'P') col = th.ground[n < 0.5 ? 0 : 1];
        // exposed surfaces
        if (up && py < (ch === 'M' ? 3 : theme === 'antarctica' ? 4 : 3)) {
          col = ch === 'M' ? (hash(gx, gy, 5) > 0.7 ? '#a07a50' : th.mud[2]) : th.top[hash(gx, gy, 3) > 0.7 ? 1 : 0];
          if (theme === 'castle' && ch !== 'M' && py === 2 && hash(gx, 0, 6) > 0.5) col = th.top[1];
        }
        if (up && theme === 'antarctica' && py >= 4 && py < 7 && hash(gx, 0, 8) > (py - 3) * 0.3) col = th.top[1];
        if (up && theme === 'castle' && py === 3 && hash(gx, 0, 9) > 0.6) col = th.top[1];
        if (up && py === 0 && ch !== 'M') col = shade(col, 0.15);
        if ((left && px === 0) || (right && px === T - 1) || (down && py === T - 1)) col = th.edge;
        if (up && py === 0 && (left && px < 2 || right && px > T - 3)) continue; // rounded corners
        if (down && py === T - 1 && (left && px < 2 || right && px > T - 3)) continue;
        g.fillStyle = col;
        g.fillRect(gx, gy, 1, 1);
      }
    }
    // grass blades / sand ripples on top
    if (up && theme === 'castle' && ch !== 'P') {
      for (let px = 1; px < T - 1; px++) if (hash(ox + px, oy, 12) > 0.6) { g.fillStyle = th.top[hash(ox + px, oy, 13) > 0.5 ? 0 : 1]; g.fillRect(ox + px, oy - 1 - (hash(ox + px, oy, 14) > 0.7 ? 1 : 0), 1, 2); }
    }
    if (up && theme === 'antarctica') {
      for (let px = 0; px < T; px++) if (hash(ox + px, oy, 15) > 0.8) { g.fillStyle = '#ffffff'; g.fillRect(ox + px, oy - 1, 1, 1); }
    }
    if (up && ch === 'M') {
      for (let px = 2; px < T - 2; px += 5) { g.fillStyle = '#c2a070'; g.fillRect(ox + px + (hash(ox + px, oy, 21) * 3 | 0), oy + 1, 2, 1); }
    }
  }
  function brick(gx, gy, th, n) {
    const row = Math.floor(gy / 5), off = row % 2 ? 4 : 0;
    const mortarY = gy % 5 === 4, mortarX = (gx + off) % 8 === 7;
    if (mortarY || mortarX) return th.edge;
    const b = hash(Math.floor((gx + off) / 8), row, 17);
    const base = th.ground[b < 0.4 ? 0 : b < 0.7 ? 1 : b < 0.9 ? 2 : 3];
    return n > 0.9 ? shade(base, -0.1) : (gy % 5 === 0 ? shade(base, 0.12) : base);
  }
  function paintIce(g, at, tx, ty) {
    const ox = tx * T, oy = ty * T;
    const up = !isSolid(at(tx, ty - 1)), down = !isSolid(at(tx, ty + 1)), left = !isSolid(at(tx - 1, ty)), right = !isSolid(at(tx + 1, ty));
    for (let py = 0; py < T; py++) for (let px = 0; px < T; px++) {
      const gx = ox + px, gy = oy + py;
      let col = '#7fc8ec';
      const diag = (gx + gy * 2) % 23;
      if (diag < 2) col = '#d8f3ff'; else if (diag < 4) col = '#a8e0f8';
      if (hash(gx >> 1, gy >> 1, 51) > 0.85) col = '#5fb0dc';
      if (up && py < 2) col = py === 0 ? '#ffffff' : '#e8f8ff';
      if ((left && px === 0) || (right && px === T - 1) || (down && py === T - 1)) col = '#2f6f9a';
      g.fillStyle = col;
      g.fillRect(gx, gy, 1, 1);
    }
  }
  function paintPlatform(g, th, theme, ox, oy, capL, capR) {
    const [a, b, c] = th.platform;
    if (theme === 'antarctica') {
      g.fillStyle = b; g.fillRect(ox, oy, T, 5);
      g.fillStyle = a; g.fillRect(ox, oy, T, 3);
      g.fillStyle = c; g.fillRect(ox, oy, T, 1);
      for (let px = 0; px < T; px += 3) { const l = 2 + (hash(ox + px, oy, 61) * 5 | 0); g.fillStyle = a; g.fillRect(ox + px + 1, oy + 5, 1, l); g.fillStyle = c; g.fillRect(ox + px + 1, oy + 5, 1, 1); }
      return;
    }
    g.fillStyle = b; g.fillRect(ox, oy, T, 5);
    g.fillStyle = a; g.fillRect(ox, oy, T, 4);
    g.fillStyle = theme === 'volcano' ? '#4a3d42' : c; g.fillRect(ox, oy, T, 1);
    g.fillStyle = b;
    for (let px = 0; px < T; px += 8) g.fillRect(ox + px, oy + 1, 1, 3);
    if (theme === 'volcano') { g.fillStyle = c; g.fillRect(ox + 4, oy + 2, 1, 1); g.fillRect(ox + 12, oy + 2, 1, 1); }
    // supports under the ends
    if (capL) { g.fillStyle = b; g.fillRect(ox + 1, oy + 5, 2, 3); }
    if (capR) { g.fillStyle = b; g.fillRect(ox + T - 3, oy + 5, 2, 3); }
  }
  function paintSpikes(g, ox, oy, tx) {
    for (let i = 0; i < 4; i++) {
      const bx = ox + i * 4;
      for (let h = 0; h < 9; h++) {
        const w = Math.max(1, Math.round((9 - h) / 9 * 3.5));
        g.fillStyle = h > 6 ? '#e6e6f0' : '#9a9aae';
        g.fillRect(bx + 2 - Math.floor(w / 2), oy + T - 1 - h, w, 1);
        g.fillStyle = '#5a5a6e';
        g.fillRect(bx + 2 - Math.floor(w / 2) + w - 1, oy + T - 1 - h, 1, 1);
      }
    }
    g.fillStyle = '#3a3a48'; g.fillRect(ox, oy + T - 2, T, 2);
  }
  function paintCactus(g, ox, oy, tx) {
    const rows = [
      '......oo........',
      '.....ohao.......',
      '.....ohao.......',
      '.oo..ohao..oo...',
      'ohao.ohao.ohao..',
      'ohao.ohao.ohao..',
      'ohaoooaaoooaAo..',
      '.ohaaaaaaaaaAo..',
      '..oooohaAoooo...',
      '.....ohaAo......',
      '.....ohaAo......',
      '.....ohaAo......',
      '.....ohaAo......',
      '.....ohaAo......',
      '....oohaAoo.....',
      '...oooooooooo...'
    ];
    if (!paintCactus.img) paintCactus.img = compile(rows, { o: '#183014', a: '#4fa03c', A: '#2e6e27', h: '#8fd463' });
    g.drawImage(paintCactus.img, ox + (tx % 2), oy);
    g.fillStyle = '#f4ecc2';
    for (let i = 0; i < 8; i++) g.fillRect(ox + 2 + (hash(i, tx, 71) * 11 | 0), oy + 2 + (hash(i, tx, 72) * 12 | 0), 1, 1);
  }
  function paintIcicles(g, ox, oy, tx) {
    for (let i = 0; i < 3; i++) {
      const bx = ox + 2 + i * 5, hgt = 9 + (hash(tx, i, 81) * 6 | 0);
      for (let h = 0; h < hgt; h++) {
        const w = Math.max(1, Math.round((hgt - h) / hgt * 4));
        g.fillStyle = h > hgt - 4 ? '#ffffff' : h > hgt / 2 ? '#bdeaff' : '#7fc8ec';
        g.fillRect(bx + 2 - Math.floor(w / 2), oy + T - 1 - h, w, 1);
      }
    }
  }

  /** Animated tiles drawn every frame: lava, magic pads (their colour is their effect). */
  const PAD_COLORS = { heal: '#4dff7a', speed: '#4dc3ff', power: '#ff4d4d', haste: '#c77dff' };
  function drawLiveTiles(g, theme, cells, t, padEffects) {
    for (let ty = 0; ty < ROWS; ty++) for (let tx = 0; tx < COLS; tx++) {
      const ch = cells[ty * COLS + tx];
      const ox = tx * T, oy = ty * T;
      if (ch === 'L') {
        const surface = cells[(ty - 1) * COLS + tx] !== 'L';
        for (let px = 0; px < T; px++) {
          const gx = ox + px;
          const wave = surface ? Math.round(Math.sin(gx * 0.35 + t * 3) * 1.2 + Math.sin(gx * 0.11 - t * 1.7) * 1.2) + 3 : 0;
          for (let py = wave; py < T; py++) {
            const n = Math.sin(gx * 0.2 + t * 1.3 + py * 0.3) + Math.sin(gx * 0.07 - t * 0.9 + py * 0.5);
            g.fillStyle = py <= wave + 1 && surface ? '#ffe27a' : n > 1.2 ? '#ffb02e' : n > 0 ? '#ff7a1f' : n > -1 ? '#e8501a' : '#b8300f';
            g.fillRect(gx, oy + py, 1, 1);
          }
        }
        if (surface) {
          glow(g, ox + 8, oy + 4, 20, '#ff6a1f', 0.35);
          if (hash(tx, Math.floor(t * 3), 91) > 0.92) { g.fillStyle = '#ffe27a'; g.fillRect(ox + (hash(tx, Math.floor(t * 3), 92) * 14 | 0), oy + 1, 2, 2); }
        }
      }
    }
    if (padEffects) {
      for (const pad of padEffects) {
        const color = PAD_COLORS[pad.effect] || '#ffffff';
        const pulse = 0.5 + Math.sin(t * 4) * 0.25;
        for (const i of pad.cells) {
          const tx = i % COLS, ty = Math.floor(i / COLS), ox = tx * T, oy = ty * T;
          g.fillStyle = '#2a2838'; g.fillRect(ox, oy, T, 4);
          g.fillStyle = color; g.fillRect(ox + 1, oy + 1, T - 2, 2);
          g.fillStyle = '#ffffff'; g.fillRect(ox + ((t * 20 + tx * 5) % 14 | 0) + 1, oy + 1, 1, 2);
          // rune
          g.fillStyle = color;
          g.fillRect(ox + 6, oy + 7, 4, 1); g.fillRect(ox + 7, oy + 6, 2, 3); g.fillRect(ox + 5, oy + 10, 6, 1);
        }
        const first = pad.cells[0], last = pad.cells[pad.cells.length - 1];
        const cx = ((first % COLS) + (last % COLS) + 1) / 2 * T, cy = Math.floor(first / COLS) * T;
        glow(g, cx, cy, 22 + pad.cells.length * 4, color, pulse);
        for (let k = 0; k < 4; k++) {
          const life = (t * 0.8 + k / 4) % 1;
          g.fillStyle = color;
          g.globalAlpha = 1 - life;
          g.fillRect(Math.round(cx + Math.sin(k * 9 + t) * pad.cells.length * 5), Math.round(cy - life * 18), 1, 2);
          g.globalAlpha = 1;
        }
      }
    }
  }

  // ------------------------------------------------------------------ backgrounds
  function renderBackground(theme) {
    const c = makeCanvas(W, H), g = c.getContext('2d');
    const th = THEMES[theme];
    const sky = g.createLinearGradient(0, 0, 0, H);
    sky.addColorStop(0, th.sky[0]); sky.addColorStop(0.55, th.sky[1]); sky.addColorStop(1, th.sky[2]);
    g.fillStyle = sky; g.fillRect(0, 0, W, H);
    // dither the gradient a little so it reads as pixel art
    for (let y = 0; y < H; y += 2) for (let x = (y / 2) % 2 * 2; x < W; x += 4) { if (hash(x, y, 1) > 0.5) { g.fillStyle = 'rgba(0,0,0,0.04)'; g.fillRect(x, y, 2, 2); } }
    const ridge = (base, amp, freq, color, seed, step) => {
      g.fillStyle = color;
      for (let x = 0; x < W; x += step || 2) {
        const h = base + Math.sin(x * freq + seed) * amp + Math.sin(x * freq * 2.7 + seed * 3) * amp * 0.35 + (hash(x >> 3, 0, seed) - 0.5) * amp * 0.15;
        g.fillRect(x, Math.round(h), step || 2, H - Math.round(h));
      }
    };
    if (theme === 'desert') {
      sun(g, 470, 70, 26, '#fff4c2', '#ffd36b');
      ridge(210, 18, 0.012, '#e9b979', 1);
      pyramid(g, 120, 205, 46, '#d39a5a', '#b67d42');
      pyramid(g, 175, 212, 28, '#c98f52', '#ab7238');
      ridge(240, 14, 0.02, '#d7a062', 4);
      ridge(270, 10, 0.03, '#c58a4e', 7);
      // distant mesa
      g.fillStyle = '#c08458'; g.fillRect(470, 190, 80, 40); g.fillRect(480, 182, 55, 10);
    } else if (theme === 'volcano') {
      ridge(150, 30, 0.01, '#2a0d0f', 2);
      // the volcano
      g.fillStyle = '#1e0a0c';
      g.beginPath(); g.moveTo(220, H); g.lineTo(300, 110); g.lineTo(340, 110); g.lineTo(440, H); g.fill();
      g.fillStyle = '#ff5a1f'; g.fillRect(300, 108, 40, 4);
      g.fillStyle = '#ffb02e'; g.fillRect(308, 108, 24, 2);
      for (let i = 0; i < 40; i++) { g.fillStyle = i % 3 ? '#a8300f' : '#ff5a1f'; g.fillRect(305 + (hash(i, 1, 3) * 30 | 0) - i, 112 + i * 4, 2, 4); }
      const gl = g.createRadialGradient(320, 110, 0, 320, 110, 120);
      gl.addColorStop(0, 'rgba(255,90,31,0.45)'); gl.addColorStop(1, 'rgba(255,90,31,0)');
      g.fillStyle = gl; g.fillRect(180, 0, 280, 260);
      ridge(250, 16, 0.025, '#160709', 9);
    } else if (theme === 'antarctica') {
      for (let i = 0; i < 160; i++) { g.fillStyle = hash(i, 2, 3) > 0.8 ? '#ffffff' : '#9fb7d9'; g.fillRect(hash(i, 0, 1) * W | 0, hash(i, 1, 1) * 200 | 0, 1, 1); }
      ridge(190, 40, 0.009, '#2a4766', 3, 2);
      // snow caps on the far range
      for (let x = 0; x < W; x += 2) {
        const h = 190 + Math.sin(x * 0.009 + 3) * 40 + Math.sin(x * 0.009 * 2.7 + 9) * 14;
        g.fillStyle = '#c9dceb'; g.fillRect(x, Math.round(h), 2, 4);
      }
      ridge(240, 22, 0.017, '#1b3350', 6);
      ridge(275, 12, 0.03, '#142a42', 8);
    } else if (theme === 'castle') {
      for (let i = 0; i < 90; i++) { g.fillStyle = '#d9d0ff'; g.fillRect(hash(i, 0, 5) * W | 0, hash(i, 1, 5) * 150 | 0, 1, 1); }
      sun(g, 520, 64, 20, '#f5f0ff', '#cfc6f0');
      ridge(230, 16, 0.012, '#2c2550', 4);
      tower(g, 60, 150, 34, '#221d40'); tower(g, 140, 120, 26, '#221d40'); tower(g, 470, 135, 30, '#221d40'); tower(g, 560, 160, 38, '#221d40');
      g.fillStyle = '#221d40'; g.fillRect(60, 200, 540, 80);
      for (let i = 0; i < 14; i++) { g.fillStyle = '#ffcf6a'; g.fillRect(80 + (hash(i, 0, 7) * 500 | 0), 210 + (hash(i, 1, 7) * 50 | 0), 2, 3); }
      ridge(268, 10, 0.03, '#17132e', 8);
    }
    return c;
  }
  function sun(g, x, y, r, c1, c2) {
    const gl = g.createRadialGradient(x, y, 0, x, y, r * 3);
    gl.addColorStop(0, c1 + 'aa'); gl.addColorStop(1, c1 + '00');
    g.fillStyle = gl; g.fillRect(x - r * 3, y - r * 3, r * 6, r * 6);
    for (let dy = -r; dy <= r; dy++) {
      const w = Math.round(Math.sqrt(r * r - dy * dy));
      g.fillStyle = dy < -r / 3 ? c1 : c2;
      g.fillRect(x - w, y + dy, w * 2, 1);
    }
  }
  function pyramid(g, x, base, h, c1, c2) {
    for (let i = 0; i < h; i++) {
      g.fillStyle = c1; g.fillRect(x - i, base - h + i, i, 1);
      g.fillStyle = c2; g.fillRect(x, base - h + i, i, 1);
    }
  }
  function tower(g, x, top, w, c) {
    g.fillStyle = c;
    g.fillRect(x, top, w, 200);
    for (let i = 0; i < w; i += 6) g.fillRect(x + i, top - 5, 4, 5);
    g.fillStyle = '#ffcf6a'; g.fillRect(x + w / 2 - 1, top + 14, 3, 5);
  }

  /** Weather and life over the background: dust, embers, snow and aurora, fireflies and torches. */
  function drawAmbient(g, theme, t) {
    if (theme === 'antarctica') {
      for (let b = 0; b < 3; b++) {
        g.save();
        g.globalAlpha = 0.18;
        g.fillStyle = b === 1 ? '#c77dff' : '#4dff9a';
        for (let x = 0; x < W; x += 2) {
          const y = 40 + b * 18 + Math.sin(x * 0.015 + t * 0.4 + b) * 16 + Math.sin(x * 0.04 - t * 0.7) * 6;
          g.fillRect(x, Math.round(y), 2, 10 + Math.round(Math.sin(x * 0.03 + t + b) * 6 + 6));
        }
        g.restore();
      }
      for (let i = 0; i < 70; i++) {
        const sp = 12 + hash(i, 1, 2) * 20;
        const x = (hash(i, 0, 2) * W + Math.sin(t * 0.8 + i) * 10 + t * 8) % W;
        const y = (hash(i, 2, 2) * H + t * sp) % H;
        g.fillStyle = '#ffffff';
        g.fillRect(Math.round(x), Math.round(y), hash(i, 3, 2) > 0.7 ? 2 : 1, hash(i, 3, 2) > 0.7 ? 2 : 1);
      }
    } else if (theme === 'volcano') {
      for (let i = 0; i < 40; i++) {
        const life = (t * (0.15 + hash(i, 1, 4) * 0.2) + hash(i, 2, 4)) % 1;
        const x = hash(i, 0, 4) * W + Math.sin(t * 2 + i) * 6;
        const y = H - life * H * 1.1;
        g.fillStyle = life < 0.5 ? '#ffb02e' : '#ff5a1f';
        g.globalAlpha = 1 - life;
        g.fillRect(Math.round(x), Math.round(y), 1, 1);
        g.globalAlpha = 1;
      }
      for (let i = 0; i < 30; i++) {
        const x = (hash(i, 0, 6) * W + t * 6) % W, y = (hash(i, 1, 6) * H + t * 10) % H;
        g.fillStyle = '#5a4a4e'; g.fillRect(Math.round(x), Math.round(y), 1, 1);
      }
    } else if (theme === 'desert') {
      for (let i = 0; i < 26; i++) {
        const x = (hash(i, 0, 8) * W + t * (30 + hash(i, 1, 8) * 40)) % W;
        const y = 200 + hash(i, 2, 8) * 140 + Math.sin(t * 2 + i) * 3;
        g.fillStyle = '#f2d08f'; g.globalAlpha = 0.6;
        g.fillRect(Math.round(x), Math.round(y), 2, 1);
        g.globalAlpha = 1;
      }
    } else if (theme === 'castle') {
      for (let i = 0; i < 16; i++) {
        const x = hash(i, 0, 9) * W + Math.sin(t * 0.7 + i * 3) * 20;
        const y = 120 + hash(i, 1, 9) * 200 + Math.cos(t * 0.9 + i) * 12;
        const a = 0.4 + Math.sin(t * 3 + i) * 0.4;
        glow(g, x, y, 5, '#d8ff6a', a * 0.6);
        g.fillStyle = '#f2ffb0'; g.globalAlpha = Math.max(0, a);
        g.fillRect(Math.round(x), Math.round(y), 1, 1);
        g.globalAlpha = 1;
      }
    }
  }

  root.ElementalsArt = {
    T, COLS, ROWS, W, H, CHARS, WEAPONS, KIT_WEAPONS, THEMES, PAD_COLORS, ICONS, ABILITY_ICONS, iconOf, shadeSprite,
    hash, shade, mix, makeCanvas, compile, glow,
    drawCharacter, drawProjectile, drawLightning, drawHearts, heartSprite,
    renderTiles, drawLiveTiles, renderBackground, drawAmbient, weaponOf, drawBat, drawDigits, heightOf
  };
})(typeof window !== 'undefined' ? window : globalThis);
