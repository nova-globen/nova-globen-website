/* Bullet Noir sound, all synthesised with Web Audio: gunshots per weapon, impacts, the explosion, bullet time (a
   whoosh down, a heartbeat, everything muffled and lower) and a cold wind. No files, nothing to license. */
(() => {
  'use strict';
  let ctx = null, master = null, sfx = null, muffle = null, wind = null, heart = null, noise = null;
  let muted = false, slow = 1;
  try { muted = localStorage.getItem(LekSite.key('noir-muted')) === '1'; } catch { }

  function unlock() {
    if (ctx) { if (ctx.state === 'suspended') ctx.resume(); return; }
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    ctx = new AC();
    master = ctx.createGain();
    master.gain.value = muted ? 0 : 0.8;
    master.connect(ctx.destination);
    muffle = ctx.createBiquadFilter();
    muffle.type = 'lowpass';
    muffle.frequency.value = 18000;
    muffle.connect(master);
    sfx = ctx.createGain();
    sfx.connect(muffle);
    const len = ctx.sampleRate * 2;
    noise = ctx.createBuffer(1, len, ctx.sampleRate);
    const d = noise.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
    startWind();
  }

  function startWind() {
    const src = ctx.createBufferSource();
    src.buffer = noise; src.loop = true;
    const f = ctx.createBiquadFilter(); f.type = 'bandpass'; f.frequency.value = 420; f.Q.value = 0.6;
    const g = ctx.createGain(); g.gain.value = 0.05;
    const lfo = ctx.createOscillator(); lfo.frequency.value = 0.13;
    const lg = ctx.createGain(); lg.gain.value = 180;
    lfo.connect(lg); lg.connect(f.frequency);
    src.connect(f); f.connect(g); g.connect(muffle);
    src.start(); lfo.start();
    wind = g;
  }

  function burst(t, { dur, freq, q = 0.7, type = 'lowpass', gain, rate = 1, at = 0 }) {
    const src = ctx.createBufferSource();
    src.buffer = noise;
    src.playbackRate.value = rate * slow;
    const f = ctx.createBiquadFilter(); f.type = type; f.frequency.value = freq; f.Q.value = q;
    const g = ctx.createGain();
    const d = dur / slow;
    g.gain.setValueAtTime(0, t + at);
    g.gain.linearRampToValueAtTime(gain, t + at + 0.004);
    g.gain.exponentialRampToValueAtTime(0.0001, t + at + d);
    src.connect(f); f.connect(g); g.connect(sfx);
    src.start(t + at, Math.random() * 1.5);
    src.stop(t + at + d + 0.05);
  }

  function tone(t, { from, to, dur, gain, type = 'sine', at = 0 }) {
    const o = ctx.createOscillator(); o.type = type;
    const d = dur / slow;
    o.frequency.setValueAtTime(from * slow, t + at);
    o.frequency.exponentialRampToValueAtTime(Math.max(20, to * slow), t + at + d);
    const g = ctx.createGain();
    g.gain.setValueAtTime(gain, t + at);
    g.gain.exponentialRampToValueAtTime(0.0001, t + at + d);
    o.connect(g); g.connect(sfx);
    o.start(t + at); o.stop(t + at + d + 0.05);
  }

  const GUNS = [
    (t, v) => { burst(t, { dur: 0.16, freq: 3200, gain: 0.9 * v }); tone(t, { from: 180, to: 60, dur: 0.12, gain: 0.5 * v }); },                      // pistol
    (t, v) => { burst(t, { dur: 0.4, freq: 1400, gain: 1.1 * v, rate: 0.7 }); tone(t, { from: 120, to: 40, dur: 0.3, gain: 0.8 * v }); burst(t, { dur: 0.25, freq: 600, gain: 0.3 * v, at: 0.35 }); }, // shotgun
    (t, v) => { burst(t, { dur: 0.08, freq: 3800, gain: 0.6 * v }); tone(t, { from: 220, to: 90, dur: 0.06, gain: 0.3 * v }); },                     // smg
    (t, v) => { burst(t, { dur: 0.2, freq: 4200, gain: 0.85 * v, q: 1.2 }); tone(t, { from: 160, to: 55, dur: 0.15, gain: 0.55 * v }); },            // rifle
  ];

  function play(name, opt = {}) {
    if (!ctx || muted) return;
    const t = ctx.currentTime;
    const v = opt.volume == null ? 1 : opt.volume;
    if (v < 0.02) return;
    switch (name) {
      case 'shot': GUNS[opt.gun || 0](t, v); break;
      case 'empty': tone(t, { from: 1800, to: 1500, dur: 0.03, gain: 0.25, type: 'square' }); break;
      case 'reload': tone(t, { from: 900, to: 700, dur: 0.04, gain: 0.2, type: 'square' }); tone(t, { from: 600, to: 500, dur: 0.05, gain: 0.25, type: 'square', at: 0.35 }); break;
      case 'reloaded': tone(t, { from: 1200, to: 900, dur: 0.05, gain: 0.25, type: 'square' }); break;
      case 'hitmark': tone(t, { from: 2200, to: 2000, dur: 0.05, gain: 0.25 * v, type: 'triangle' }); break;
      case 'headshot': tone(t, { from: 2600, to: 2500, dur: 0.18, gain: 0.3, type: 'triangle' }); tone(t, { from: 3900, to: 3800, dur: 0.12, gain: 0.15, type: 'sine' }); break;
      case 'hurt': burst(t, { dur: 0.15, freq: 300, gain: 0.7 }); tone(t, { from: 140, to: 70, dur: 0.18, gain: 0.5, type: 'sawtooth' }); break;
      case 'flesh': burst(t, { dur: 0.08, freq: 700, gain: 0.5 * v }); break;
      case 'ric': tone(t, { from: 3200 + Math.random() * 1500, to: 1400, dur: 0.18, gain: 0.12 * v, type: 'sine' }); burst(t, { dur: 0.05, freq: 5000, gain: 0.2 * v, type: 'highpass' }); break;
      case 'boom': burst(t, { dur: 1.6, freq: 500, gain: 1.4 * v, rate: 0.5 }); tone(t, { from: 90, to: 25, dur: 1.2, gain: 1.0 * v }); burst(t, { dur: 0.5, freq: 3000, gain: 0.5 * v }); break;
      case 'dive': burst(t, { dur: 0.35, freq: 900, gain: 0.35, type: 'bandpass', q: 0.8 }); break;
      case 'land': burst(t, { dur: 0.2, freq: 250, gain: 0.6 }); break;
      case 'pick': tone(t, { from: 660, to: 990, dur: 0.12, gain: 0.25, type: 'triangle' }); tone(t, { from: 990, to: 1320, dur: 0.12, gain: 0.2, type: 'triangle', at: 0.08 }); break;
      case 'heal': tone(t, { from: 520, to: 780, dur: 0.25, gain: 0.25, type: 'sine' }); break;
      case 'kill': tone(t, { from: 440, to: 440, dur: 0.12, gain: 0.25, type: 'square' }); tone(t, { from: 660, to: 660, dur: 0.2, gain: 0.2, type: 'square', at: 0.1 }); break;
      case 'die': tone(t, { from: 300, to: 50, dur: 1.2, gain: 0.5, type: 'sawtooth' }); break;
      case 'beep': tone(t, { from: 880, to: 880, dur: 0.12, gain: 0.25, type: 'square' }); break;
      case 'go': tone(t, { from: 1320, to: 1320, dur: 0.35, gain: 0.25, type: 'square' }); break;
      case 'win': [523, 659, 784, 1046].forEach((f, i) => tone(t, { from: f, to: f, dur: 0.3, gain: 0.2, type: 'triangle', at: i * 0.12 })); break;
      case 'lose': [392, 330, 262].forEach((f, i) => tone(t, { from: f, to: f * 0.98, dur: 0.4, gain: 0.2, type: 'triangle', at: i * 0.18 })); break;
      case 'btIn': tone(t, { from: 900, to: 90, dur: 0.6, gain: 0.35, type: 'sine' }); burst(t, { dur: 0.7, freq: 1200, gain: 0.4, type: 'bandpass', q: 1.5, rate: 0.6 }); break;
      case 'btOut': tone(t, { from: 90, to: 900, dur: 0.45, gain: 0.3, type: 'sine' }); break;
    }
  }

  /** Bullet time: everything lower, muffled, and a heartbeat. k = 1 normal, k < 1 slowed. */
  function setSlow(k) {
    if (!ctx) return;
    slow = k;
    const t = ctx.currentTime;
    muffle.frequency.setTargetAtTime(k < 1 ? 1300 : 18000, t, 0.12);
    if (k < 1 && !heart) {
      heart = setInterval(() => {
        if (muted || !ctx) return;
        const n = ctx.currentTime;
        const beat = (at) => { const o = ctx.createOscillator(); o.frequency.value = 55; const g = ctx.createGain(); g.gain.setValueAtTime(0.0001, n + at); g.gain.exponentialRampToValueAtTime(0.55, n + at + 0.02); g.gain.exponentialRampToValueAtTime(0.0001, n + at + 0.18); o.connect(g); g.connect(master); o.start(n + at); o.stop(n + at + 0.25); };
        beat(0); beat(0.24);
      }, 900);
    } else if (k >= 1 && heart) { clearInterval(heart); heart = null; }
  }

  function setMuted(m) {
    muted = m;
    try { localStorage.setItem(LekSite.key('noir-muted'), m ? '1' : '0'); } catch { }
    if (master) master.gain.value = m ? 0 : 0.8;
  }

  window.NoirAudio = { unlock, play, setSlow, setMuted, isMuted: () => muted };
})();
