/* Bullet Noir movement, mirrored line for line from Server/Noir/NoirPhysics.cs; change both together.
   Metres, fixed 30 Hz: run relative to the camera's yaw, and the shootdodge (air, floor, getting up). */
(() => {
  'use strict';
  const Dt = 1 / 30;
  const Fwd = 1, Back = 2, Left = 4, Right = 8, Dive = 16, Fire = 32;
  const Run = 5.2, Half = 0.35;
  const DiveSpeed = 7.8, DiveAir = 0.55, DiveLie = 0.5, DiveRise = 0.35, DiveCooldown = 0.5;
  const DiveEnd = DiveAir + DiveLie + DiveRise;

  function body(o) {
    return Object.assign({ x: 0, z: 0, diveT: -1, diveX: 0, diveZ: 1, diveCd: 0, speed: 1, prevDive: false }, o || {});
  }

  // yaw: 0 looks down +Z, positive turns toward +X. Forward is (sin yaw, cos yaw), right is (-cos yaw, sin yaw).
  function step(b, bits, yaw, boxes) {
    b.diveCd = Math.max(0, b.diveCd - Dt);
    const f = ((bits & Fwd) ? 1 : 0) - ((bits & Back) ? 1 : 0);
    const s = ((bits & Right) ? 1 : 0) - ((bits & Left) ? 1 : 0);
    const sy = Math.sin(yaw), cy = Math.cos(yaw);
    let mx = f * sy - s * cy;
    let mz = f * cy + s * sy;
    const len = Math.sqrt(mx * mx + mz * mz);
    if (len > 0) { mx /= len; mz /= len; }
    const dive = (bits & Dive) !== 0;
    if (dive && !b.prevDive && b.diveT < 0 && b.diveCd <= 0) {
      b.diveT = 0;
      if (len > 0) { b.diveX = mx; b.diveZ = mz; } else { b.diveX = sy; b.diveZ = cy; }
    }
    b.prevDive = dive;
    let vx, vz;
    if (b.diveT >= 0) {
      const air = b.diveT < DiveAir;
      vx = air ? b.diveX * DiveSpeed * b.speed : 0;
      vz = air ? b.diveZ * DiveSpeed * b.speed : 0;
      b.diveT += Dt * b.speed;
      if (b.diveT >= DiveEnd) { b.diveT = -1; b.diveCd = DiveCooldown; }
    } else {
      vx = mx * Run * b.speed;
      vz = mz * Run * b.speed;
    }
    let n = Math.ceil(Math.max(Math.abs(vx), Math.abs(vz)) * Dt / 0.1);
    if (n < 1) n = 1;
    for (let i = 0; i < n; i++) {
      moveX(b, vx * Dt / n, boxes);
      moveZ(b, vz * Dt / n, boxes);
    }
  }

  function height(b) {
    if (b.diveT < 0) return 1.8;
    if (b.diveT < DiveAir) return 1.05;
    if (b.diveT < DiveAir + DiveLie) return 0.55;
    return 1.2;
  }

  // boxes: [x0, z0, x1, z1, height]
  function moveX(b, d, boxes) {
    if (d === 0) return;
    b.x += d;
    for (const box of boxes) {
      if (b.z + Half <= box[1] || b.z - Half >= box[3] || b.x + Half <= box[0] || b.x - Half >= box[2]) continue;
      b.x = d > 0 ? box[0] - Half - 0.001 : box[2] + Half + 0.001;
    }
  }

  function moveZ(b, d, boxes) {
    if (d === 0) return;
    b.z += d;
    for (const box of boxes) {
      if (b.x + Half <= box[0] || b.x - Half >= box[2] || b.z + Half <= box[1] || b.z - Half >= box[3]) continue;
      b.z = d > 0 ? box[1] - Half - 0.001 : box[3] + Half + 0.001;
    }
  }

  window.NoirPhysics = { Dt, Fwd, Back, Left, Right, Dive, Fire, Run, Half, DiveAir, DiveLie, DiveRise, DiveEnd, body, step, height };
})();
