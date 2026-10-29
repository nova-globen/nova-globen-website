/* Starter safehouses: what a new player is handed before they design their own. Drawn on the 9 x 9 grid; '#' wall,
   '.' floor, 'S' start (two), 'O' OUT (two), 'V' vault. Guards are listed separately: a path of cells and a facing
   (0 up, 1 right, 2 down, 3 left); a one-cell path is a sentry. tools/check-insidejob.mjs proves each is beatable. */
(function (root, factory) {
  if (typeof module === 'object' && module.exports) module.exports = factory();
  else root.IJ_STARTERS = factory();
})(typeof self !== 'undefined' ? self : this, function () {
  'use strict';
  const W = 9;
  const id = (x, y) => y * W + x;
  function fromRows(rows, guards) {
    const wall = [], starts = [], exits = []; let intel = -1;
    for (let y = 0; y < 9; y++) for (let x = 0; x < 9; x++) {
      const ch = rows[y][x]; wall.push(ch === '#' ? 1 : 0);
      if (ch === 'S') starts.push(id(x, y)); if (ch === 'O') exits.push(id(x, y)); if (ch === 'V') intel = id(x, y);
    }
    return { wall, starts, exits, intel, guards };
  }
  // a horizontal route from (x, y) of n cells; vertical when vertical is true
  const route = (x, y, n, vertical, phase) => ({ path: Array.from({ length: n }, (_, i) => vertical ? id(x, y + i) : id(x + i, y)), dir: vertical ? 2 : 1, phase: phase || 0 });
  const sentry = (x, y, dir) => ({ path: [id(x, y)], dir, phase: 0 });

  const STARTERS = {
    townhouse: fromRows([
      '.......OO',
      '.##......',
      '....#....',
      '.#..#.##.',
      '....V....',
      '.##...#..',
      '....#....',
      '.........',
      'SS.......',
    ], [route(0, 2, 4, false, 0), route(8, 2, 4, true, 3), sentry(5, 6, 1)]),
    warehouse: fromRows([
      'SS.......',
      '...#.....',
      '.#.#.###.',
      '.#.......',
      '.#.##.#..',
      '...#..V..',
      '.#.#.####',
      '.#.......',
      '.......OO',
    ], [route(4, 1, 5, false, 2), route(2, 3, 5, false, 4), sentry(1, 8, 1)]),
  };
  return { STARTERS };
});
