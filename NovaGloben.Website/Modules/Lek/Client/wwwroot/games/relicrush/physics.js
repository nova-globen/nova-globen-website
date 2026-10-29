/* Relic Rush movement, a line-for-line copy of src/Server/RelicRush/RelicPhysics.cs so the browser can predict its
   own runner and replay unacknowledged inputs. Change both together. Loads in the browser and in Node. */
(function (root) {
  'use strict';
  const T = 16;
  const P = { Dt: 1 / 30, Up: 1, Down: 2, Left: 4, Right: 8, Dash: 16, Run: 92, DashSpeed: 290, DashTime: 0.17, DashCooldown: 3.0, Half: 5 };

  function body(o) {
    return Object.assign({ x: 0, y: 0, dashT: 0, dashX: 0, dashY: 0, dashCd: 0, faceX: 1, faceY: 0, speed: 1, prevDash: false }, o || {});
  }

  function step(b, bits, wall) {
    const Dt = P.Dt;
    b.dashCd = Math.max(0, b.dashCd - Dt);
    let dx = ((bits & P.Right) !== 0 ? 1 : 0) - ((bits & P.Left) !== 0 ? 1 : 0);
    let dy = ((bits & P.Down) !== 0 ? 1 : 0) - ((bits & P.Up) !== 0 ? 1 : 0);
    if (dx !== 0 && dy !== 0) { dx *= 0.7071; dy *= 0.7071; }
    if (dx !== 0 || dy !== 0) { b.faceX = dx; b.faceY = dy; }
    const dash = (bits & P.Dash) !== 0;
    if (dash && !b.prevDash && b.dashCd <= 0 && b.dashT <= 0) {
      b.dashT = P.DashTime;
      b.dashCd = P.DashCooldown;
      const len = Math.sqrt(b.faceX * b.faceX + b.faceY * b.faceY);
      b.dashX = len > 0 ? b.faceX / len : 1;
      b.dashY = len > 0 ? b.faceY / len : 0;
    }
    b.prevDash = dash;
    let vx, vy;
    if (b.dashT > 0) {
      b.dashT = Math.max(0, b.dashT - Dt);
      vx = b.dashX * P.DashSpeed;
      vy = b.dashY * P.DashSpeed;
    } else {
      vx = dx * P.Run * b.speed;
      vy = dy * P.Run * b.speed;
    }
    let n = Math.ceil(Math.max(Math.abs(vx), Math.abs(vy)) * Dt / 4);
    if (n < 1) n = 1;
    for (let i = 0; i < n; i++) {
      moveX(b, vx * Dt / n, wall);
      moveY(b, vy * Dt / n, wall);
    }
  }

  function moveX(b, d, wall) {
    if (d === 0) return;
    b.x += d;
    const r0 = Math.floor((b.y - P.Half + 0.01) / T), r1 = Math.floor((b.y + P.Half - 0.01) / T);
    const c = Math.floor((b.x + (d > 0 ? P.Half : -P.Half)) / T);
    for (let r = r0; r <= r1; r++) {
      if (!wall(c, r)) continue;
      b.x = d > 0 ? c * T - P.Half - 0.01 : (c + 1) * T + P.Half + 0.01;
      return;
    }
  }

  function moveY(b, d, wall) {
    if (d === 0) return;
    b.y += d;
    const c0 = Math.floor((b.x - P.Half + 0.01) / T), c1 = Math.floor((b.x + P.Half - 0.01) / T);
    const r = Math.floor((b.y + (d > 0 ? P.Half : -P.Half)) / T);
    for (let c = c0; c <= c1; c++) {
      if (!wall(c, r)) continue;
      b.y = d > 0 ? r * T - P.Half - 0.01 : (r + 1) * T + P.Half + 0.01;
      return;
    }
  }

  const api = Object.assign({}, P, { body, step });
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  root.RelicPhysics = api;
})(typeof window !== 'undefined' ? window : globalThis);
