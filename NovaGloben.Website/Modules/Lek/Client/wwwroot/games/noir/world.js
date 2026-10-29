/* Bullet Noir - the 3D world: renderer, the snowy docks built from the map the server sends, Quaternius characters
   (CC0) with their guns and animations, bullets, bubbles of bullet time, and the effects. No game rules here: game.js
   owns the network, input and prediction and tells this module where things are. */
import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';

const LOOKS = ['Character_Enemy', 'Character_Soldier', 'Character_Hazmat'];
const GUN_MESH = ['Pistol', 'Shotgun', 'SMG', 'AK'];
const ALL_GUNS = ['Revolver', 'Sniper', 'Revolver_Small', 'Pistol', 'SMG', 'GrenadeLauncher', 'ShortCannon', 'Shotgun', 'Sniper_2', 'RocketLauncher', 'AK', 'Shovel', 'Knife_2', 'Knife_1'];
const PICKUP_MODEL = { '-1': 'Health', 0: 'Pistol', 1: 'Shotgun', 2: 'Smg', 3: 'Ak47' };

export class World {
  constructor(canvas) {
    this.canvas = canvas;
    const r = this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
    r.setPixelRatio(Math.min(window.devicePixelRatio, 1.75));
    r.shadowMap.enabled = true;
    r.shadowMap.type = THREE.PCFSoftShadowMap;
    r.toneMapping = THREE.ACESFilmicToneMapping;
    r.toneMappingExposure = 1.3;
    r.outputColorSpace = THREE.SRGBColorSpace;
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x0a0f18);
    this.scene.fog = new THREE.FogExp2(0x0d1320, 0.026);
    this.camera = new THREE.PerspectiveCamera(62, 16 / 9, 0.1, 400);
    this.loader = new GLTFLoader();
    this.models = new Map();
    this.kit = 0.82;
    this.solids = [];
    this.players = new Map();
    this.bullets = [];
    this.bubbles = new Map();
    this.flashes = [];
    this.shake = 0;
    this.time = 0;
    this.lights();
    this.particlesInit();
    this.snowInit();
    this.resize();
    window.addEventListener('resize', () => this.resize());
  }

  resize() {
    const w = this.canvas.clientWidth || window.innerWidth, h = this.canvas.clientHeight || window.innerHeight;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  // ------------------------------------------------------------------ assets
  async load(names, onProgress) {
    let done = 0;
    await Promise.all(names.map(async (n) => {
      if (this.models.has(n)) { onProgress && onProgress(++done, names.length); return; }
      const gltf = await this.loader.loadAsync('models/' + n + '.glb');
      gltf.scene.traverse((o) => {
        if (o.isMesh) {
          o.castShadow = true; o.receiveShadow = true;
          const ms = Array.isArray(o.material) ? o.material : [o.material];
          for (const m of ms) { if (m.metalness !== undefined) { m.metalness = Math.min(m.metalness, 0.2); m.roughness = Math.max(m.roughness, 0.6); } }
        }
      });
      this.models.set(n, gltf);
      onProgress && onProgress(++done, names.length);
    }));
  }

  /** A copy of a static model, centred on its footprint, standing on y = 0, at kit scale times s. */
  prop(name, s = 1) {
    const g = this.models.get(name);
    if (!g) return new THREE.Group();
    const inner = g.scene.clone(true);
    const box = new THREE.Box3().setFromObject(inner);
    const c = box.getCenter(new THREE.Vector3());
    inner.position.set(-c.x, -box.min.y, -c.z);
    const outer = new THREE.Group();
    outer.add(inner);
    outer.scale.setScalar(this.kit * s);
    return outer;
  }

  // ------------------------------------------------------------------ lights and weather
  lights() {
    const s = this.scene;
    s.add(new THREE.HemisphereLight(0x6678a8, 0x1a1820, 1.9));
    const moon = this.moon = new THREE.DirectionalLight(0xb4c6ff, 2.4);
    moon.position.set(-18, 40, -14);
    moon.castShadow = true;
    moon.shadow.mapSize.set(2048, 2048);
    Object.assign(moon.shadow.camera, { left: -34, right: 34, top: 34, bottom: -34, near: 1, far: 120 });
    moon.shadow.bias = -0.0006;
    moon.shadow.normalBias = 0.03;
    s.add(moon, moon.target);
    // the moon itself, low and big, behind the skyline
    const disc = new THREE.Sprite(new THREE.SpriteMaterial({ map: moonTexture(), color: 0xe8ecff, fog: false, transparent: true, depthWrite: false }));
    disc.position.set(-70, 60, -100);
    disc.scale.setScalar(16);
    s.add(disc);
    // a pool of flash lights for muzzles and explosions, always in the scene so shaders never recompile
    for (let i = 0; i < 4; i++) {
      const l = new THREE.PointLight(0xffc070, 0, 9, 2);
      s.add(l);
      this.flashes.push({ light: l, t: 1, life: 1, peak: 0 });
    }
  }

  flash(x, y, z, color, peak, life, range = 9) {
    const f = this.flashes.reduce((a, b) => (a.t / a.life > b.t / b.life ? a : b));
    f.light.position.set(x, y, z);
    f.light.color.set(color);
    f.light.distance = range;
    f.peak = peak; f.t = 0; f.life = life;
  }

  snowInit() {
    const n = 2600;
    const pos = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) { pos[i * 3] = Math.random() * 60 - 30; pos[i * 3 + 1] = Math.random() * 22; pos[i * 3 + 2] = Math.random() * 60 - 30; }
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    const mat = new THREE.PointsMaterial({ color: 0xdfe6ff, size: 0.07, transparent: true, opacity: 0.8, depthWrite: false });
    this.snow = new THREE.Points(geo, mat);
    this.snow.frustumCulled = false;
    this.scene.add(this.snow);
  }

  snowStep(dt, slow) {
    const p = this.snow.geometry.attributes.position;
    const a = p.array, cx = this.camera.position.x, cz = this.camera.position.z;
    for (let i = 0; i < a.length; i += 3) {
      a[i + 1] -= dt * (1.1 + (i % 7) * 0.08) * slow;
      a[i] += Math.sin(this.time * 0.7 + i) * dt * 0.25 * slow;
      if (a[i + 1] < 0) a[i + 1] += 22;
      if (a[i] - cx > 30) a[i] -= 60; else if (a[i] - cx < -30) a[i] += 60;
      if (a[i + 2] - cz > 30) a[i + 2] -= 60; else if (a[i + 2] - cz < -30) a[i + 2] += 60;
    }
    p.needsUpdate = true;
  }

  // ------------------------------------------------------------------ the map
  build(map) {
    this.map = map;
    this.kit = map.kit;
    this.solids = map.solids;
    const s = this.scene;
    const group = this.level = new THREE.Group();
    s.add(group);
    // ground: snowy asphalt inside, deep snow outside
    const yard = new THREE.Mesh(new THREE.PlaneGeometry(map.w, map.d), new THREE.MeshStandardMaterial({ map: groundTexture(map.w, map.d), roughness: 0.92 }));
    yard.rotation.x = -Math.PI / 2;
    yard.position.set(map.w / 2, 0, map.d / 2);
    yard.receiveShadow = true;
    group.add(yard);
    const outside = new THREE.Mesh(new THREE.PlaneGeometry(400, 400), new THREE.MeshStandardMaterial({ color: 0x8f97ab, roughness: 1 }));
    outside.rotation.x = -Math.PI / 2;
    outside.position.set(map.w / 2, -0.02, map.d / 2);
    outside.receiveShadow = true;
    group.add(outside);
    // brick walls with snow on top
    const brick = new THREE.MeshStandardMaterial({ map: brickTexture(), roughness: 0.95 });
    const snowCap = new THREE.MeshStandardMaterial({ color: 0xe6ebf5, roughness: 1 });
    for (const w of map.walls) {
      const [x0, z0, x1, z1, h] = w;
      const sx = x1 - x0, sz = z1 - z0;
      const m = new THREE.Mesh(tiledBox(sx, h, sz, 1.6), brick);
      m.position.set((x0 + x1) / 2, h / 2, (z0 + z1) / 2);
      m.castShadow = true; m.receiveShadow = true;
      group.add(m);
      const cap = new THREE.Mesh(new THREE.BoxGeometry(sx + 0.06, 0.08, sz + 0.06), snowCap);
      cap.position.set(m.position.x, h + 0.04, m.position.z);
      cap.receiveShadow = true;
      group.add(cap);
    }
    for (const p of map.props) group.add(this.place(p));
    this.lamps = [];
    for (const p of map.decor) {
      const o = this.place(p);
      group.add(o);
      if (p.m === 'Street_Light') this.lamp(p);
    }
    this.barrels = map.barrels.map(([x, z]) => {
      const o = this.prop('Exploding_Barrel');
      o.position.set(x, 0, z);
      group.add(o);
      return o;
    });
    this.pickups = map.pickups.map((pk) => {
      const o = this.prop(PICKUP_MODEL[pk.k], pk.k < 0 ? 0.9 : 1.1);
      const holder = new THREE.Group();
      holder.add(o);
      holder.position.set(pk.x, 0.55, pk.z);
      const ring = new THREE.Mesh(new THREE.RingGeometry(0.45, 0.6, 32), new THREE.MeshBasicMaterial({ color: pk.k < 0 ? 0x7dff9a : 0xffd27a, transparent: true, opacity: 0.55, side: THREE.DoubleSide, depthWrite: false }));
      ring.rotation.x = -Math.PI / 2;
      ring.position.set(pk.x, 0.03, pk.z);
      group.add(holder, ring);
      return { holder, ring, kind: pk.k };
    });
  }

  place(p) {
    const o = this.prop(p.m, p.s);
    o.position.set(p.x, 0, p.z);
    o.rotation.y = p.r * Math.PI / 2;
    return o;
  }

  /** A street light: a warm spot pointing down from the end of the arm, and a glow. */
  lamp(p) {
    const turn = p.r * Math.PI / 2;
    const arm = 1.35;
    const lx = p.x + Math.sin(turn) * arm, lz = p.z + Math.cos(turn) * arm, ly = 4.45;
    const spot = new THREE.SpotLight(0xffb565, 55, 18, 0.95, 0.65, 1.4);
    spot.position.set(lx, ly, lz);
    spot.target.position.set(lx + Math.sin(turn) * 1.5, 0, lz + Math.cos(turn) * 1.5);
    this.scene.add(spot, spot.target);
    const glow = new THREE.Sprite(new THREE.SpriteMaterial({ map: glowTexture(), color: 0xffc27a, transparent: true, blending: THREE.AdditiveBlending, depthWrite: false }));
    glow.position.set(lx, ly - 0.1, lz);
    glow.scale.setScalar(1.6);
    this.scene.add(glow);
    this.lamps.push({ spot, glow });
  }

  setBarrels(alive) { (this.barrels || []).forEach((o, i) => { o.visible = !!alive[i]; }); }
  setPickups(avail) {
    (this.pickups || []).forEach((p, i) => {
      p.holder.visible = !!avail[i];
      p.ring.visible = !!avail[i];
      p.holder.rotation.y = this.time * 1.4 + i;
      p.holder.position.y = 0.55 + Math.sin(this.time * 2.2 + i) * 0.08;
    });
  }

  // ------------------------------------------------------------------ characters
  character(id, look, isMe) {
    let c = this.players.get(id);
    if (c) return c;
    const gltf = this.models.get(LOOKS[look % LOOKS.length]);
    const model = SkeletonUtils.clone(gltf.scene);
    model.scale.setScalar(this.kit);
    const root = new THREE.Group();
    const tilt = new THREE.Group();   // the dive rotates this one
    tilt.add(model);
    root.add(tilt);
    const guns = {};
    let torso = null;
    model.traverse((o) => {
      if (ALL_GUNS.includes(o.name)) { guns[o.name] = o; o.visible = false; }
      if (o.isBone && o.name === 'Torso') torso = o;
      if (o.isMesh) { o.castShadow = true; o.frustumCulled = false; }
    });
    const mixer = new THREE.AnimationMixer(model);
    const actions = {};
    for (const clip of gltf.animations) {
      const name = clip.name.replace(/^.*\|/, '');
      actions[name] = mixer.clipAction(clip);
    }
    for (const n of ['Death', 'HitReact', 'Jump_Land']) if (actions[n]) { actions[n].setLoop(THREE.LoopOnce); actions[n].clampWhenFinished = true; }
    let tag = null;
    if (!isMe) { tag = new THREE.Sprite(new THREE.SpriteMaterial({ transparent: true, depthWrite: false, fog: false })); tag.position.y = 2.25; tag.scale.set(1.6, 0.4, 1); root.add(tag); }
    this.scene.add(root);
    c = { id, root, tilt, model, guns, torso, mixer, actions, current: null, gun: -1, tag, tagText: '', alive: true, lastShot: -10, isMe };
    this.players.set(id, c);
    return c;
  }

  removeCharacter(id) {
    const c = this.players.get(id);
    if (!c) return;
    this.scene.remove(c.root);
    this.players.delete(id);
  }

  play(c, name, fade = 0.15, speed = 1) {
    const a = c.actions[name] || c.actions.Idle;
    if (c.current === a) { a.timeScale = speed; return; }
    a.reset();
    a.timeScale = speed;
    a.enabled = true;
    a.setEffectiveWeight(1);
    if (c.current) a.crossFadeFrom(c.current, fade, false);
    a.play();
    c.current = a;
  }

  /** Puts one character where game.js says: position, aim, dive, gun, motion. */
  pose(c, s, dt, slow) {
    c.root.position.set(s.x, 0, s.z);
    c.root.rotation.y = s.aimYaw;
    if (s.gun !== c.gun) {
      for (const n in c.guns) c.guns[n].visible = false;
      const g = c.guns[GUN_MESH[s.gun]];
      if (g) g.visible = true;
      c.gun = s.gun;
    }
    // the shootdodge: the body lays flat along the dive, in the air and on the floor, then stands up
    let lay = 0, lift = 0;
    const P = window.NoirPhysics;
    if (s.diveT >= 0) {
      if (s.diveT < P.DiveAir) { const k = s.diveT / P.DiveAir; lay = Math.min(1, k * 4); lift = Math.sin(k * Math.PI) * 0.55 + 0.25 * lay; }
      else if (s.diveT < P.DiveAir + P.DiveLie) { lay = 1; lift = 0.18; }
      else { const k = (s.diveT - P.DiveAir - P.DiveLie) / P.DiveRise; lay = 1 - k; lift = 0.18 * (1 - k); }
    }
    // the tilt axis is horizontal and perpendicular to the dive, expressed in the root's (aim-rotated) frame
    const rel = Math.atan2(s.diveX, s.diveZ) - s.aimYaw;
    c.tilt.rotation.set(0, 0, 0);
    if (lay > 0) {
      const q = new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(Math.cos(rel), 0, -Math.sin(rel)), lay * 1.35);
      c.tilt.quaternion.copy(q);
    }
    c.tilt.position.y = lift;
    if (!s.alive) {
      if (c.alive) { this.play(c, 'Death', 0.1); c.alive = false; }
    } else {
      c.alive = true;
      const shooting = this.time - c.lastShot < 0.6;
      if (s.diveT >= 0) this.play(c, 'Idle_Shoot', 0.08);
      else if (s.moving) {
        const back = s.moveDot < -0.3;
        this.play(c, shooting ? (c.actions.Run_Shoot ? 'Run_Shoot' : 'Run_Gun') : 'Run_Gun', 0.15, back ? -1 : 1);
      } else this.play(c, shooting ? 'Idle_Shoot' : 'Idle', 0.2);
    }
    // undo last frame's aim tilt first: clips without a torso track never reset it, so it would add up
    if (c.torso && c.tilted) { c.torso.rotation.x += c.tilted; c.tilted = 0; }
    c.mixer.update(dt * slow);
    if (c.torso && s.alive && s.diveT < 0) { c.tilted = s.aimPitch * 0.7; c.torso.rotation.x -= c.tilted; }
    if (c.tag) {
      c.tag.visible = s.alive;
      const text = s.name + (s.protect ? ' ✦' : '');
      if (text !== c.tagText) { c.tag.material.map = textTexture(text); c.tag.material.needsUpdate = true; c.tagText = text; }
    }
  }

  // ------------------------------------------------------------------ bullets and bubbles
  bulletMesh() {
    if (!this._bulletGeo) {
      this._bulletGeo = new THREE.CylinderGeometry(0.018, 0.018, 0.22, 6).rotateX(Math.PI / 2);
      this._bulletMat = new THREE.MeshBasicMaterial({ color: 0xfff1c0 });
      this._trailMat = new THREE.MeshBasicMaterial({ color: 0xffd890, transparent: true, opacity: 0.35, blending: THREE.AdditiveBlending, depthWrite: false });
      this._trailGeo = new THREE.CylinderGeometry(0.006, 0.02, 1, 6).translate(0, -0.5, 0).rotateX(-Math.PI / 2);
    }
    const g = new THREE.Group();
    g.add(new THREE.Mesh(this._bulletGeo, this._bulletMat));
    const trail = new THREE.Mesh(this._trailGeo, this._trailMat);
    g.add(trail);
    g.userData.trail = trail;
    this.scene.add(g);
    return g;
  }

  bubble(id, x, z, r, on) {
    let b = this.bubbles.get(id);
    if (!b) {
      const mat = new THREE.ShaderMaterial({
        uniforms: { uTime: { value: 0 }, uAlpha: { value: 0 } },
        vertexShader: 'varying vec3 vN; varying vec3 vV; void main(){ vec4 mv = modelViewMatrix * vec4(position,1.0); vN = normalize(normalMatrix*normal); vV = normalize(-mv.xyz); gl_Position = projectionMatrix*mv; }',
        fragmentShader: 'uniform float uTime; uniform float uAlpha; varying vec3 vN; varying vec3 vV; void main(){ float f = pow(1.0 - abs(dot(vN, vV)), 2.5); float w = 0.5 + 0.5*sin(uTime*3.0 + vN.y*8.0); gl_FragColor = vec4(vec3(1.0,0.78,0.45)*(f*0.9 + 0.05*w), (f*0.55 + 0.04)*uAlpha); }',
        transparent: true, depthWrite: false, side: THREE.DoubleSide, blending: THREE.AdditiveBlending
      });
      b = { mesh: new THREE.Mesh(new THREE.SphereGeometry(1, 32, 20), mat), a: 0 };
      this.scene.add(b.mesh);
      this.bubbles.set(id, b);
    }
    b.a += ((on ? 1 : 0) - b.a) * 0.18;
    b.mesh.visible = b.a > 0.02;
    b.mesh.position.set(x, 0.2, z);
    b.mesh.scale.setScalar(r * (0.6 + 0.4 * b.a));
    b.mesh.material.uniforms.uTime.value = this.time;
    b.mesh.material.uniforms.uAlpha.value = b.a;
  }

  // ------------------------------------------------------------------ particles
  particlesInit() {
    const mk = (n, blending, size) => {
      const geo = new THREE.BufferGeometry();
      geo.setAttribute('position', new THREE.BufferAttribute(new Float32Array(n * 3), 3));
      geo.setAttribute('color', new THREE.BufferAttribute(new Float32Array(n * 3), 3));
      const mat = new THREE.PointsMaterial({ size, vertexColors: true, transparent: true, depthWrite: false, blending, map: glowTexture(), sizeAttenuation: true });
      const pts = new THREE.Points(geo, mat);
      pts.frustumCulled = false;
      this.scene.add(pts);
      return { pts, n, list: [] };
    };
    this.glowParts = mk(900, THREE.AdditiveBlending, 0.16);
    this.dustParts = mk(900, THREE.NormalBlending, 0.22);
  }

  burst(sys, x, y, z, color, n, speed, life, up = 1.5, grav = -9) {
    const c = new THREE.Color(color);
    const s = sys === 'glow' ? this.glowParts : this.dustParts;
    for (let i = 0; i < n && s.list.length < s.n; i++) {
      const a = Math.random() * Math.PI * 2, e = (Math.random() - 0.3) * 1.4, v = speed * (0.3 + Math.random() * 0.7);
      s.list.push({ x, y, z, vx: Math.cos(a) * Math.cos(e) * v, vy: Math.sin(e) * v + up, vz: Math.sin(a) * Math.cos(e) * v, t: 0, life: life * (0.5 + Math.random() * 0.8), r: c.r, g: c.g, b: c.b, grav });
    }
  }

  stepParticles(dt) {
    for (const s of [this.glowParts, this.dustParts]) {
      const pos = s.pts.geometry.attributes.position.array, col = s.pts.geometry.attributes.color.array;
      let j = 0;
      for (let i = s.list.length - 1; i >= 0; i--) {
        const p = s.list[i];
        p.t += dt;
        if (p.t >= p.life) { s.list.splice(i, 1); continue; }
        p.vy += p.grav * dt;
        p.x += p.vx * dt; p.y += p.vy * dt; p.z += p.vz * dt;
        if (p.y < 0.02) { p.y = 0.02; p.vy *= -0.2; p.vx *= 0.6; p.vz *= 0.6; }
      }
      for (const p of s.list) {
        const k = 1 - p.t / p.life;
        pos[j * 3] = p.x; pos[j * 3 + 1] = p.y; pos[j * 3 + 2] = p.z;
        col[j * 3] = p.r * k; col[j * 3 + 1] = p.g * k; col[j * 3 + 2] = p.b * k;
        j++;
      }
      for (; j < s.n; j++) pos[j * 3 + 1] = -100;
      s.pts.geometry.attributes.position.needsUpdate = true;
      s.pts.geometry.attributes.color.needsUpdate = true;
    }
  }

  impact(x, y, z, what) {
    if (what === 'flesh') { this.burst('dust', x, y, z, 0x8a0f12, 14, 2.5, 0.6, 1); this.burst('dust', x, y, z, 0x5a0508, 6, 1.2, 1.2, 0.5); }
    else if (what === 'floor') this.burst('dust', x, 0.05, z, 0xd8e0f0, 10, 1.6, 0.7, 2, -5);
    else if (what === 'wall') { this.burst('glow', x, y, z, 0xffc070, 10, 4, 0.25, 1); this.burst('dust', x, y, z, 0x77706a, 6, 1.2, 0.8, 0.6, -3); }
  }

  explosion(x, z) {
    this.flash(x, 1.2, z, 0xff8a30, 260, 0.7, 16);
    this.burst('glow', x, 0.8, z, 0xffb040, 120, 8, 0.6, 3, -4);
    this.burst('glow', x, 0.8, z, 0xff5010, 60, 5, 0.9, 2, -2);
    this.burst('dust', x, 0.8, z, 0x2a2622, 70, 3, 1.8, 2.5, 0.6);
    this.shake = Math.max(this.shake, 0.6);
  }

  muzzle(x, y, z, big) {
    this.flash(x, y, z, 0xffc070, big ? 40 : 22, 0.07, 8);
    this.burst('glow', x, y, z, 0xffd27a, big ? 8 : 4, 2.5, 0.08, 0, 0);
  }

  // ------------------------------------------------------------------ frame
  frame(dt, slow) {
    this.time += dt;
    for (const f of this.flashes) {
      f.t = Math.min(f.life, f.t + dt * slow);
      const k = 1 - f.t / f.life;
      f.light.intensity = f.peak * k * k;
    }
    this.stepParticles(dt * slow);
    this.snowStep(dt, slow);
    for (const l of this.lamps || []) l.glow.material.opacity = 0.85 + Math.sin(this.time * 13 + l.spot.position.x) * 0.03;
    if (this.shake > 0) {
      this.camera.position.x += (Math.random() - 0.5) * this.shake * 0.25;
      this.camera.position.y += (Math.random() - 0.5) * this.shake * 0.25;
      this.shake = Math.max(0, this.shake - dt * 2.5);
    }
    this.renderer.render(this.scene, this.camera);
  }

  // ------------------------------------------------------------------ geometry queries (camera and aim)
  /** Nearest hit of a ray against the solid boxes (and anything in extra), as a distance, or Infinity. */
  ray(ox, oy, oz, dx, dy, dz, extra) {
    let best = Infinity;
    const test = (b) => {
      const t = rayBox(ox, oy, oz, dx, dy, dz, b[0], 0, b[1], b[2], b[4], b[3]);
      if (t < best) best = t;
    };
    for (const b of this.solids) test(b);
    if (extra) for (const b of extra) test(b);
    if (dy < 0) { const t = -oy / dy; if (t > 0 && t < best) best = t; }
    return best;
  }
}

export function rayBox(ox, oy, oz, dx, dy, dz, x0, y0, z0, x1, y1, z1) {
  let tmin = 0, tmax = Infinity;
  const axes = [[ox, dx, x0, x1], [oy, dy, y0, y1], [oz, dz, z0, z1]];
  for (const [o, d, lo, hi] of axes) {
    if (Math.abs(d) < 1e-9) { if (o < lo || o > hi) return Infinity; continue; }
    let t1 = (lo - o) / d, t2 = (hi - o) / d;
    if (t1 > t2) [t1, t2] = [t2, t1];
    tmin = Math.max(tmin, t1); tmax = Math.min(tmax, t2);
    if (tmin > tmax) return Infinity;
  }
  return tmin;
}

// ------------------------------------------------------------------ procedural textures
function canvasTex(w, h, draw, repeat) {
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  draw(c.getContext('2d'), w, h);
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.anisotropy = 8;
  if (repeat) t.repeat.set(repeat[0], repeat[1]);
  return t;
}

function brickTexture() {
  return canvasTex(256, 256, (g, w, h) => {
    g.fillStyle = '#2b2522'; g.fillRect(0, 0, w, h);
    const bh = 32, bw = 64;
    for (let row = 0; row < h / bh; row++) {
      for (let col = -1; col < w / bw + 1; col++) {
        const x = col * bw + (row % 2 ? bw / 2 : 0), y = row * bh;
        const v = 70 + Math.random() * 40;
        g.fillStyle = `rgb(${v + 30},${v * 0.42},${v * 0.36})`;
        g.fillRect(x + 3, y + 3, bw - 6, bh - 6);
        for (let k = 0; k < 30; k++) { g.fillStyle = `rgba(0,0,0,${Math.random() * 0.18})`; g.fillRect(x + 3 + Math.random() * (bw - 8), y + 3 + Math.random() * (bh - 8), 3, 3); }
      }
    }
    const grad = g.createLinearGradient(0, 0, 0, h);
    grad.addColorStop(0, 'rgba(0,0,0,0)'); grad.addColorStop(1, 'rgba(0,0,0,0.25)');
    g.fillStyle = grad; g.fillRect(0, 0, w, h);
  });
}

function groundTexture(w, d) {
  const t = canvasTex(512, 512, (g, W, H) => {
    g.fillStyle = '#3a3e47'; g.fillRect(0, 0, W, H);
    for (let i = 0; i < 9000; i++) { const v = 40 + Math.random() * 40; g.fillStyle = `rgba(${v},${v + 3},${v + 10},0.5)`; g.fillRect(Math.random() * W, Math.random() * H, 2, 2); }
    // drifts of snow
    for (let i = 0; i < 45; i++) {
      const x = Math.random() * W, y = Math.random() * H, r = 16 + Math.random() * 55;
      const grad = g.createRadialGradient(x, y, 0, x, y, r);
      grad.addColorStop(0, 'rgba(205,214,232,0.6)'); grad.addColorStop(1, 'rgba(205,214,232,0)');
      g.fillStyle = grad; g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2); g.fill();
    }
    g.strokeStyle = 'rgba(20,20,24,0.6)'; g.lineWidth = 2;
    for (let i = 0; i < 14; i++) { g.beginPath(); let x = Math.random() * W, y = Math.random() * H; g.moveTo(x, y); for (let k = 0; k < 6; k++) { x += (Math.random() - 0.5) * 60; y += (Math.random() - 0.5) * 60; g.lineTo(x, y); } g.stroke(); }
  });
  t.repeat.set(w / 10, d / 10);
  return t;
}

function moonTexture() {
  const t = canvasTex(128, 128, (g, w, h) => {
    const grad = g.createRadialGradient(64, 64, 20, 64, 64, 64);
    grad.addColorStop(0, 'rgba(255,255,255,1)'); grad.addColorStop(0.42, 'rgba(250,250,255,1)'); grad.addColorStop(0.47, 'rgba(200,210,255,0.35)'); grad.addColorStop(1, 'rgba(160,180,255,0)');
    g.fillStyle = grad; g.fillRect(0, 0, w, h);
    g.fillStyle = 'rgba(150,155,175,0.35)';
    for (const [x, y, r] of [[52, 54, 7], [72, 70, 5], [60, 78, 4], [76, 50, 3]]) { g.beginPath(); g.arc(x, y, r, 0, Math.PI * 2); g.fill(); }
  });
  t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
  return t;
}

let _glow = null;
function glowTexture() {
  if (_glow) return _glow;
  _glow = canvasTex(64, 64, (g, w, h) => {
    const grad = g.createRadialGradient(32, 32, 0, 32, 32, 32);
    grad.addColorStop(0, 'rgba(255,255,255,1)'); grad.addColorStop(0.3, 'rgba(255,255,255,0.6)'); grad.addColorStop(1, 'rgba(255,255,255,0)');
    g.fillStyle = grad; g.fillRect(0, 0, w, h);
  });
  _glow.wrapS = _glow.wrapT = THREE.ClampToEdgeWrapping;
  return _glow;
}

function textTexture(text) {
  const t = canvasTex(256, 64, (g, w, h) => {
    g.font = '600 30px Oswald, Arial Narrow, sans-serif';
    g.textAlign = 'center'; g.textBaseline = 'middle';
    g.lineWidth = 6; g.strokeStyle = 'rgba(0,0,0,0.8)'; g.strokeText(text, w / 2, h / 2);
    g.fillStyle = '#f2e6cf'; g.fillText(text, w / 2, h / 2);
  });
  t.wrapS = t.wrapT = THREE.ClampToEdgeWrapping;
  return t;
}

/** A box whose UVs are in metres / tile, so one brick texture tiles evenly on walls of every size. */
function tiledBox(w, h, d, tile) {
  const geo = new THREE.BoxGeometry(w, h, d);
  const uv = geo.attributes.uv;
  const faces = [[d, h], [d, h], [w, d], [w, d], [w, h], [w, h]];
  for (let f = 0; f < 6; f++) for (let v = 0; v < 4; v++) {
    const i = f * 4 + v;
    uv.setXY(i, uv.getX(i) * faces[f][0] / tile, uv.getY(i) * faces[f][1] / tile);
  }
  return geo;
}
