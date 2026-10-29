/* Elementals sound: recorded effects from sfx/ (CC0 packs, see music/CREDITS.md) where we have them, effects
   synthesised with Web Audio for the rest (fire, lightning, explosions), and music from music/<slot>.mp3. A missing
   file just falls back to the synthesised sound, or to silence for music. */
(function (root) {
  'use strict';
  // the sounds and music live with Elementals; other games point here at them
  const BASE = root.AUDIO_BASE || '';
  let ctx = null, master = null, sfxBus = null, noise = null;
  let muted = false;
  try { muted = localStorage.getItem(LekSite.key('elementals-muted')) === '1'; } catch { }
  const music = { el: null, slot: null };

  // recorded effects: each name has a few takes, one picked at random and pitched a little for variety
  const SAMPLES = {
    swing: ['swing1', 'swing2', 'swing3'], heavy: ['heavy1', 'heavy2'], hit: ['hit1', 'hit2', 'hit3'], hurt: ['hurt1', 'hurt2'],
    block: ['block1'], pot: ['pot1', 'pot2'], land: ['land1', 'land2'], death: ['death1'], ice: ['ice1'], freeze: ['freeze1'],
    shoot: ['shoot1'], claw: ['claw1']
  };
  const buffers = {};
  function loadSamples() {
    for (const list of Object.values(SAMPLES)) {
      for (const f of list) {
        fetch(BASE + 'sfx/' + f + '.mp3').then((r) => (r.ok ? r.arrayBuffer() : Promise.reject(r.status)))
          .then((b) => ctx.decodeAudioData(b)).then((buf) => { buffers[f] = buf; }).catch(() => { });
      }
    }
  }
  function sample(name, t) {
    const list = (SAMPLES[name] || []).filter((f) => buffers[f]);
    if (!list.length) return false;
    const src = ctx.createBufferSource();
    src.buffer = buffers[list[Math.floor(Math.random() * list.length)]];
    src.playbackRate.value = 0.93 + Math.random() * 0.14;
    const gn = ctx.createGain();
    gn.gain.value = name === 'land' ? 0.35 : 0.9;
    src.connect(gn); gn.connect(sfxBus);
    src.start(t);
    return true;
  }

  function unlock() {
    if (ctx) { if (ctx.state === 'suspended') ctx.resume(); return; }
    const AC = root.AudioContext || root.webkitAudioContext;
    if (!AC) return;
    ctx = new AC();
    master = ctx.createGain();
    master.gain.value = muted ? 0 : 0.8;
    master.connect(ctx.destination);
    sfxBus = ctx.createGain();
    sfxBus.gain.value = 0.55;
    sfxBus.connect(master);
    const len = ctx.sampleRate;
    noise = ctx.createBuffer(1, len, ctx.sampleRate);
    const d = noise.getChannelData(0);
    for (let i = 0; i < len; i++) d[i] = Math.random() * 2 - 1;
    loadSamples();
  }

  function noiseBurst(t, dur, f1, f2, q, gain, type) {
    const src = ctx.createBufferSource();
    src.buffer = noise;
    const flt = ctx.createBiquadFilter();
    flt.type = type || 'bandpass';
    flt.Q.value = q || 1;
    flt.frequency.setValueAtTime(f1, t);
    flt.frequency.exponentialRampToValueAtTime(Math.max(30, f2), t + dur);
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(gain, t + Math.min(0.02, dur / 4));
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    src.connect(flt); flt.connect(g); g.connect(sfxBus);
    src.start(t, Math.random() * 0.5, dur + 0.05);
  }
  function tone(t, dur, f1, f2, gain, type) {
    const o = ctx.createOscillator();
    o.type = type || 'sine';
    o.frequency.setValueAtTime(f1, t);
    o.frequency.exponentialRampToValueAtTime(Math.max(20, f2), t + dur);
    const g = ctx.createGain();
    g.gain.setValueAtTime(0.0001, t);
    g.gain.exponentialRampToValueAtTime(gain, t + 0.01);
    g.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    o.connect(g); g.connect(sfxBus);
    o.start(t); o.stop(t + dur + 0.05);
  }

  const SFX = {
    swing: (t) => noiseBurst(t, 0.16, 2400, 700, 1.2, 0.5),
    heavy: (t) => { noiseBurst(t, 0.24, 1400, 300, 1.0, 0.6); tone(t, 0.12, 140, 70, 0.2); },
    hit: (t) => { noiseBurst(t, 0.12, 1800, 400, 0.8, 0.7); tone(t, 0.1, 220, 90, 0.35, 'triangle'); },
    hurt: (t) => { noiseBurst(t, 0.18, 900, 200, 0.7, 0.8); tone(t, 0.16, 180, 60, 0.4, 'sawtooth'); },
    death: (t) => { noiseBurst(t, 0.6, 1200, 80, 0.6, 0.9); tone(t, 0.5, 200, 40, 0.4, 'sawtooth'); },
    shoot: (t) => noiseBurst(t, 0.12, 3000, 1200, 2, 0.35),
    fire: (t) => { noiseBurst(t, 0.35, 900, 300, 0.5, 0.6, 'lowpass'); },
    ice: (t) => { tone(t, 0.25, 2400, 3200, 0.12); tone(t + 0.03, 0.25, 3100, 4200, 0.08); noiseBurst(t, 0.15, 6000, 3000, 3, 0.25); },
    zap: (t) => { noiseBurst(t, 0.18, 5000, 1500, 0.4, 0.5, 'highpass'); tone(t, 0.12, 900, 300, 0.12, 'square'); },
    thunder: (t) => { noiseBurst(t, 1.1, 2500, 60, 0.4, 1.0, 'lowpass'); tone(t, 0.6, 90, 35, 0.5); },
    boom: (t) => { noiseBurst(t, 0.7, 1500, 50, 0.5, 1.0, 'lowpass'); tone(t, 0.4, 110, 30, 0.6); },
    thorns: (t) => { for (let i = 0; i < 4; i++) noiseBurst(t + i * 0.02, 0.08, 4000, 2000, 4, 0.25); },
    grow: (t) => { noiseBurst(t, 0.4, 300, 900, 1.5, 0.4); tone(t, 0.35, 120, 260, 0.2, 'triangle'); },
    jump: (t) => noiseBurst(t, 0.08, 900, 1600, 1, 0.12),
    land: (t) => noiseBurst(t, 0.08, 600, 200, 1, 0.18, 'lowpass'),
    pot: (t) => { for (let i = 0; i < 5; i++) noiseBurst(t + i * 0.03, 0.08, 3000 + i * 400, 1500, 3, 0.3); },
    pickup: (t) => { tone(t, 0.12, 660, 660, 0.15, 'triangle'); tone(t + 0.1, 0.2, 990, 990, 0.15, 'triangle'); },
    heal: (t) => { tone(t, 0.3, 520, 780, 0.1, 'triangle'); },
    blink: (t) => { tone(t, 0.18, 300, 2400, 0.12, 'sine'); noiseBurst(t, 0.12, 4000, 8000, 2, 0.2); },
    beep: (t) => tone(t, 0.12, 440, 440, 0.2, 'triangle'),
    go: (t) => { tone(t, 0.35, 660, 660, 0.25, 'triangle'); tone(t, 0.35, 990, 990, 0.12, 'triangle'); noiseBurst(t, 0.5, 1200, 200, 0.5, 0.4, 'lowpass'); },
    win: (t) => { [523, 659, 784, 1046].forEach((f, i) => tone(t + i * 0.12, 0.5, f, f, 0.18, 'triangle')); },
    lose: (t) => { [392, 349, 311, 262].forEach((f, i) => tone(t + i * 0.16, 0.5, f, f * 0.98, 0.16, 'triangle')); },
    click: (t) => tone(t, 0.05, 1200, 900, 0.08, 'triangle')
  };

  function play(name, delay) {
    if (!ctx || muted) return;
    const t = ctx.currentTime + (delay || 0);
    try {
      if (sample(name, t)) { if (name === 'death') SFX.death(t); return; }
      if (SFX[name]) SFX[name](t);
      else if (name === 'block') SFX.hit(t);
      else if (name === 'freeze') SFX.ice(t);
      else if (name === 'claw') SFX.swing(t);
    } catch { }
  }

  function playMusic(slot) {
    if (music.slot === slot) return;
    music.slot = slot;
    if (music.el) { music.el.pause(); music.el = null; }
    if (!slot) return;
    const el = new Audio(BASE + 'music/' + slot + '.mp3');
    el.loop = slot !== 'victory';
    el.volume = 0.45;
    el.muted = muted;
    el.addEventListener('error', () => { if (music.el === el) music.el = null; });
    music.el = el;
    el.play().catch(() => { });
  }

  function setMuted(value) {
    muted = value;
    try { localStorage.setItem(LekSite.key('elementals-muted'), value ? '1' : '0'); } catch { }
    if (master) master.gain.value = muted ? 0 : 0.8;
    if (music.el) music.el.muted = muted;
  }

  root.ElementalsAudio = { unlock, play, playMusic, setMuted, isMuted: () => muted };
})(typeof window !== 'undefined' ? window : globalThis);
