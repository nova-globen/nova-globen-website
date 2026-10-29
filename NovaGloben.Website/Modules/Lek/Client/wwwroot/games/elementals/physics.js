/* Elementals movement, a line-for-line copy of src/Server/Elementals/ElementalsPhysics.cs so the browser can predict
   its own character and replay unacknowledged inputs. Change both together. Loads in the browser and in Node. */
(function (root) {
  'use strict';
  const T = 16;
  const P = {
    Dt: 1 / 30, Left: 1, Right: 2, Jump: 4, Down: 8, Attack: 16,
    Gravity: 1100, MaxFall: 620, Run: 135,
    GroundAccel: 1500, GroundFriction: 1800, AirAccel: 1000, AirFriction: 320,
    IceAccel: 360, IceFriction: 80,
    JumpSpeed: 410, JumpCut: 0.45, CoyoteTime: 0.1,
    MudRun: 0.55, MudJump: 0.7, FlySpeed: 1.6,
    LavaRun: 0.6, LavaJump: 0.75, LavaGravity: 0.45, LavaMaxFall: 110
  };
  const isSolid = (c) => c === '#' || c === '~' || c === 'M' || c === 'P' || c === 'w' || c === 'c';
  const isIcy = (c) => c === '~' || c === 'w';

  function body(o) {
    return Object.assign({ x: 0, y: 0, vx: 0, vy: 0, w: 14, h: 30, grounded: false, ground: '.', coyote: 0, prevJump: false, jumping: false, dashT: 0, dashVX: 0, flyT: 0, lockT: 0, speed: 1, landV: 0 }, o || {});
  }

  function step(b, bits, cell) {
    const Dt = P.Dt;
    const locked = b.lockT > 0;
    if (locked) b.lockT = Math.max(0, b.lockT - Dt);
    const left = !locked && (bits & P.Left) !== 0;
    const right = !locked && (bits & P.Right) !== 0;
    const jump = !locked && (bits & P.Jump) !== 0;
    const down = !locked && (bits & P.Down) !== 0;
    const inLava = touches(b, cell, 'L');

    if (b.dashT > 0) {
      b.dashT = Math.max(0, b.dashT - Dt);
      b.vx = b.dashVX;
      b.vy = 0;
      b.prevJump = jump;
      move(b, b.vx * Dt, 0, down, cell);
      if (b.dashT <= 0) b.vx *= 0.35;
      return;
    }

    if (b.flyT > 0) {
      b.flyT = Math.max(0, b.flyT - Dt);
      const fx = (right ? 1 : 0) - (left ? 1 : 0);
      const fy = (down ? 1 : 0) - (jump ? 1 : 0);
      b.vx = approach(b.vx, fx * P.Run * P.FlySpeed * b.speed, P.AirAccel * 1.6 * Dt);
      b.vy = approach(b.vy, fy * P.Run * P.FlySpeed * 0.8 * b.speed, P.AirAccel * 1.6 * Dt);
      b.prevJump = jump;
      b.grounded = false;
      b.landV = 0;
      move(b, b.vx * Dt, b.vy * Dt, true, cell);
      return;
    }

    const g = b.grounded ? b.ground : '.';
    const run = P.Run * b.speed * (g === 'M' ? P.MudRun : 1) * (inLava ? P.LavaRun : 1);
    let accel, friction;
    if (b.grounded && isIcy(g)) { accel = P.IceAccel; friction = P.IceFriction; }
    else if (b.grounded) { accel = P.GroundAccel; friction = P.GroundFriction; }
    else { accel = P.AirAccel; friction = P.AirFriction; }
    const dir = (right ? 1 : 0) - (left ? 1 : 0);
    if (dir !== 0) {
      const target = dir * run;
      const over = Math.sign(b.vx) === dir && Math.abs(b.vx) > run;
      b.vx = approach(b.vx, target, (over ? friction : accel) * Dt);
    } else b.vx = approach(b.vx, 0, friction * Dt);

    const pressed = jump && !b.prevJump;
    b.prevJump = jump;
    if (pressed && (b.grounded || b.coyote > 0 || inLava)) {
      const mul = (g === 'M' ? P.MudJump : 1) * (inLava ? P.LavaJump : 1);
      b.vy = -P.JumpSpeed * mul;
      b.grounded = false;
      b.coyote = 0;
      b.jumping = true;
    }
    if (b.jumping && !jump && b.vy < -P.JumpSpeed * P.JumpCut) b.vy = -P.JumpSpeed * P.JumpCut;
    if (b.vy >= 0) b.jumping = false;

    b.vy += P.Gravity * (inLava ? P.LavaGravity : 1) * Dt;
    const maxFall = inLava ? P.LavaMaxFall : P.MaxFall;
    if (b.vy > maxFall) b.vy = maxFall;

    const wasGrounded = b.grounded;
    const vyBefore = b.vy;
    b.grounded = false;
    b.landV = 0;
    move(b, b.vx * Dt, b.vy * Dt, down, cell);
    if (b.grounded) {
      b.coyote = P.CoyoteTime;
      if (!wasGrounded) b.landV = vyBefore;
    } else b.coyote = Math.max(0, b.coyote - Dt);
  }

  function approach(v, target, delta) { return v < target ? Math.min(v + delta, target) : Math.max(v - delta, target); }

  function move(b, dx, dy, down, cell) {
    let n = Math.ceil(Math.max(Math.abs(dx), Math.abs(dy)) / 4);
    if (n < 1) n = 1;
    const sx = dx / n, sy = dy / n;
    for (let i = 0; i < n; i++) {
      if (sx !== 0) moveX(b, sx, cell);
      if (sy !== 0) moveY(b, sy, down, cell);
    }
  }

  function moveX(b, d, cell) {
    b.x += d;
    const r0 = Math.floor((b.y - b.h + 0.01) / T), r1 = Math.floor((b.y - 0.01) / T);
    if (d > 0) {
      const c = Math.floor((b.x + b.w / 2) / T);
      for (let r = r0; r <= r1; r++) {
        if (!isSolid(cell(c, r))) continue;
        b.x = c * T - b.w / 2 - 0.01; b.vx = 0;
        if (b.dashT > 0) b.dashVX = 0;
        return;
      }
    } else {
      const c = Math.floor((b.x - b.w / 2) / T);
      for (let r = r0; r <= r1; r++) {
        if (!isSolid(cell(c, r))) continue;
        b.x = (c + 1) * T + b.w / 2 + 0.01; b.vx = 0;
        if (b.dashT > 0) b.dashVX = 0;
        return;
      }
    }
  }

  function moveY(b, d, down, cell) {
    const prevY = b.y;
    b.y += d;
    const c0 = Math.floor((b.x - b.w / 2 + 0.01) / T), c1 = Math.floor((b.x + b.w / 2 - 0.01) / T);
    if (d > 0) {
      const r = Math.floor(b.y / T);
      for (let c = c0; c <= c1; c++) {
        const ch = cell(c, r);
        if (isSolid(ch) || (ch === '=' && !down && prevY <= r * T + 0.01)) {
          b.y = r * T; b.vy = 0; b.grounded = true; b.ground = ch;
          return;
        }
      }
    } else {
      const r = Math.floor((b.y - b.h) / T);
      for (let c = c0; c <= c1; c++) {
        if (!isSolid(cell(c, r))) continue;
        b.y = (r + 1) * T + b.h; b.vy = 0; b.jumping = false;
        return;
      }
    }
  }

  function touches(b, cell, kind) {
    const c0 = Math.floor((b.x - b.w / 2 + 1) / T), c1 = Math.floor((b.x + b.w / 2 - 1) / T);
    const r0 = Math.floor((b.y - b.h + 1) / T), r1 = Math.floor((b.y - 1) / T);
    for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) if (cell(c, r) === kind) return true;
    return false;
  }

  const api = Object.assign({}, P, { body, step, touches, isSolid, isIcy });
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  root.ElementalsPhysics = api;
})(typeof window !== 'undefined' ? window : globalThis);
