// NightDrive Player — escenas animadas para la pantalla de puntos (256×64)
// Se dibujan en blanco (punto encendido) y en rojo puro (punto tenue): el
// volcado a la matriz convierte el rojo en brillo bajo para dar profundidad.
(function () {
  const W = 256, H = 64;
  const ON = '#fff', DIM = '#f00';

  function rng(seed) { return () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; }; }
  const frac = v => v - Math.floor(v);

  class Scenes {
    constructor() {
      const r = rng(20261120);
      this.stars = Array.from({ length: 120 }, () => ({ x: Math.floor(r() * W), y: Math.floor(r() * 44), ph: r() * 6.28, sp: 0.6 + r() * 2.2, big: r() < 0.07 }));
      // montañas: perfil suave con varias senoides
      this.ridge = new Int16Array(W);
      for (let x = 0; x < W; x++) this.ridge[x] = Math.round(49 + 5 * Math.sin(x * 0.041 + 0.6) + 3 * Math.sin(x * 0.11 + 2) + 1.5 * Math.sin(x * 0.29));
      this.cabins = [[38, 0], [146, 1], [183, 0]];
      // ciudad al fondo de la carretera
      this.city = [];
      for (let x = 0; x < W;) {
        const w = 6 + Math.floor(r() * 12), h = 3 + Math.floor(r() * 12);
        const wins = [];
        for (let wy = 2; wy < h - 1; wy += 2) for (let wx = 1; wx < w - 1; wx += 2) if (r() < 0.35) wins.push([wx, wy, r() * 6.28]);
        this.city.push({ x, w, h, wins });
        x += w + Math.floor(r() * 3);
      }
      this.gulls = Array.from({ length: 4 }, (_, i) => ({ x: r() * W, y: 6 + r() * 16, sp: 6 + r() * 8, ph: r() * 6 }));
      this.shoot = null;
      this.roadZ = 0;
      this.last = performance.now();
      this.boatX = 150;
    }

    // ------------------------------------------------------------
    sky(b, t, au, onlyAbove = 64, count = 120, off = 0) {
      const hi = au.high;
      for (let i = 0; i < count; i++) {
        const s0 = this.stars[i];
        if (s0.y >= onlyAbove) continue;
        const s = off ? { ...s0, x: Math.round((((s0.x + off) % W) + W) % W) } : s0;
        const tw = Math.sin(t * s.sp + s.ph) + hi * 1.6;
        if (tw > 0.55) {
          b.fillStyle = ON; b.fillRect(s.x, s.y, 1, 1);
          if (s.big && tw > 1.1) { b.fillStyle = DIM; b.fillRect(s.x - 1, s.y, 1, 1); b.fillRect(s.x + 1, s.y, 1, 1); b.fillRect(s.x, s.y - 1, 1, 1); b.fillRect(s.x, s.y + 1, 1, 1); }
        } else if (tw > -0.5) { b.fillStyle = DIM; b.fillRect(s.x, s.y, 1, 1); }
      }
    }

    moon(b, cx, cy, r, crescent) {
      for (let y = -r - 4; y <= r + 4; y++) for (let x = -r - 4; x <= r + 4; x++) {
        const d = Math.hypot(x, y);
        if (d <= r) {
          if (crescent && Math.hypot(x - r * 0.55, y + r * 0.25) < r * 0.92) continue;
          // cráteres tenues
          const crater = (Math.hypot(x + r * 0.35, y + r * 0.2) < r * 0.22) || (Math.hypot(x + r * 0.1, y - r * 0.45) < r * 0.16);
          b.fillStyle = crater ? DIM : ON;
          b.fillRect(cx + x, cy + y, 1, 1);
        } else if (d <= r + 3.2 && ((x + y) & 1) === 0 && !(crescent && x > 0)) {
          b.fillStyle = DIM; b.fillRect(cx + x, cy + y, 1, 1); // halo
        }
      }
    }

    // ---------------- cielo estrellado ----------------
    nightSky(b, t, dt, au) {
      this.sky(b, t, au, 56);
      this.moon(b, 206, 17, 11, true);
      // estrella fugaz (más probable con golpes de bajo)
      if (!this.shoot && (Math.random() < 0.004 || (au.kick && Math.random() < 0.08))) {
        this.shoot = { x: 40 + Math.random() * 120, y: 2 + Math.random() * 10, vx: 70 + Math.random() * 50, vy: 22 + Math.random() * 12, life: 1 };
      }
      if (this.shoot) {
        const s = this.shoot;
        s.x += s.vx * dt; s.y += s.vy * dt; s.life -= dt * 1.4;
        for (let k = 0; k < 14; k++) {
          const tx = Math.round(s.x - s.vx * k * 0.012), ty = Math.round(s.y - s.vy * k * 0.012);
          b.fillStyle = k < 3 ? ON : DIM; if (k < 3 || k % 2 === 0) b.fillRect(tx, ty, 1, 1);
        }
        if (s.life <= 0 || s.y > 50) this.shoot = null;
      }
      // montañas y cabañas con luz
      for (let x = 0; x < W; x++) {
        const y = this.ridge[x];
        b.clearRect(x, y, 1, H - y);
        b.fillStyle = ON; b.fillRect(x, y, 1, 1);
        if (x % 3 === 0) { b.fillStyle = DIM; b.fillRect(x, y + 3 + (x % 2), 1, 1); }
      }
      for (const [cx, k] of this.cabins) {
        const y = this.ridge[cx] + 4;
        b.fillStyle = ON; b.fillRect(cx, y, 5, 1); b.fillRect(cx, y + 1, 1, 3); b.fillRect(cx + 4, y + 1, 1, 3); b.fillRect(cx + 1, y - 1, 3, 1); b.fillRect(cx + 2, y - 2, 1, 1);
        if (Math.sin(t * 0.7 + k) > -0.8) { b.fillStyle = ON; b.fillRect(cx + 2, y + 2, 1, 1); b.fillStyle = DIM; b.fillRect(cx + 1, y + 2, 1, 1); b.fillRect(cx + 3, y + 2, 1, 1); }
      }
    }

    // ---------------- carretera de noche ----------------
    highway(b, t, dt, au) {
      const hy = 26, vx = 128;
      const drive = window.NDDrive;
      const realSpeed = !!(drive && drive.gpsOk);
      const kmh = realSpeed ? drive.speed : 80 + au.energy * 90 + Math.sin(t) * 3;
      // avance: con GPS a la velocidad real (parado en un semáforo, la carretera se para)
      this.roadZ += dt * (realSpeed ? kmh / 3.6 / 45 : 0.35 + au.energy * 1.6);
      // curvas: giroscopio del móvil (grados/s) o curvas suaves de demostración
      const target = drive && drive.sensorOk ? drive.yaw : 7 * Math.sin(t * 0.09) + 4 * Math.sin(t * 0.037 + 1);
      this.curve = (this.curve || 0) + (target - (this.curve || 0)) * Math.min(1, dt * 3);
      this.skyOff = (this.skyOff || 0) + this.curve * dt * 2.84; // ~90° de campo de visión en 256 puntos
      const off = ((this.skyOff % W) + W) % W;
      const shift = p => Math.max(-110, Math.min(110, -this.curve * (1 - p) * (1 - p) * 2.6));
      this.sky(b, t, au, hy - 12, 60, off);
      this.moon(b, Math.round((((34 + off) % W) + W) % W), 8, 5, false);
      // ciudad en el horizonte (se desplaza al girar)
      for (const c of this.city) {
        const top = hy - c.h;
        for (const base of [((c.x + off) % W + W) % W, ((c.x + off) % W + W) % W - W]) {
          if (base + c.w < 0 || base >= W) continue;
          b.fillStyle = DIM;
          for (let x = base; x < base + c.w; x++) b.fillRect(x, top, 1, 1);
          b.fillRect(base, top, 1, c.h); b.fillRect(base + c.w - 1, top, 1, c.h);
          b.fillStyle = ON;
          for (const [wx, wy, ph] of c.wins) if (Math.sin(t * 0.25 + ph) > -0.6) b.fillRect(base + wx, top + wy, 1, 1);
        }
      }
      b.fillStyle = ON; b.fillRect(0, hy, W, 1);
      b.clearRect(0, hy + 1, W, H - hy - 1);
      // bordes de la carretera (curvados según el giro)
      const edge = (p, side) => vx + shift(p) + side * (5 + p * 150);
      for (let y = hy + 1; y < H; y++) {
        const p = (y - hy) / (H - hy);
        b.fillStyle = ON; b.fillRect(Math.round(edge(p, -1)), y, 1, 1); b.fillRect(Math.round(edge(p, 1)), y, 1, 1);
      }
      // líneas de velocidad en el asfalto y en el arcén
      for (let k = 0; k < 7; k++) {
        const z = frac(k / 7 + this.roadZ * 0.8), p = z * z, y = Math.round(hy + 1 + (H - hy - 1) * p);
        b.fillStyle = DIM;
        const l = Math.round(edge(p, -1)), rr = Math.round(edge(p, 1));
        for (let x = 0; x < l - 2; x += 3) b.fillRect(x, y, 1, 1);
        for (let x = rr + 3; x < W; x += 3) b.fillRect(x, y, 1, 1);
      }
      // líneas centrales
      for (let k = 0; k < 8; k++) {
        const z0 = frac(k / 8 + this.roadZ), p0 = z0 * z0, p1 = Math.min(1, (z0 + 0.05) * (z0 + 0.05));
        const y0 = Math.round(hy + 1 + (H - hy - 1) * p0), y1 = Math.round(hy + 1 + (H - hy - 1) * p1);
        const w = Math.max(1, Math.round(1 + p0 * 4));
        b.fillStyle = ON; b.fillRect(Math.round(vx + shift(p0)) - (w >> 1), y0, w, Math.max(1, y1 - y0));
      }
      // farolas a ambos lados
      for (let k = 0; k < 6; k++) {
        const z = frac(k / 6 + this.roadZ * 0.5), p = z * z;
        const yg = hy + (H - hy) * p, h = 3 + p * 34;
        for (const side of [-1, 1]) {
          const x = Math.round(edge(p, side) + side * (3 + p * 14));
          const top = Math.round(yg - h);
          b.fillStyle = ON; b.fillRect(x, top, 1, Math.max(1, Math.round(h)));
          const arm = Math.round(1 + p * 7);
          b.fillRect(side < 0 ? x : x - arm, top, arm + 1, 1);
          const lx = side < 0 ? x + arm : x - arm;
          b.fillRect(lx, top + 1, 1, 1);
          if (p > 0.15) { b.fillStyle = DIM; for (let g = 2; g < 2 + p * 9; g += 2) b.fillRect(lx - (g >> 1), top + 1 + g, (g | 1), 1); }
        }
      }
      // coche delante con las luces traseras: sigue la curva y bota con el bajo
      const bob = Math.round(Math.sin(t * 9) * (kmh > 2 ? 0.4 : 0) + au.bass * 1.5);
      const cp = (40 - hy) / (H - hy);
      const cx = Math.round(vx + shift(cp) + Math.sin(t * 0.4) * 3), cy = 40 - bob;
      b.clearRect(cx - 9, cy - 6, 19, 10);
      b.fillStyle = DIM; b.fillRect(cx - 8, cy - 4, 17, 1); b.fillRect(cx - 9, cy - 3, 1, 5); b.fillRect(cx + 9, cy - 3, 1, 5); b.fillRect(cx - 9, cy + 2, 19, 1);
      b.fillRect(cx - 6, cy - 6, 13, 1); b.fillRect(cx - 7, cy - 5, 1, 1); b.fillRect(cx + 7, cy - 5, 1, 1);
      b.fillStyle = ON; b.fillRect(cx - 8, cy - 1, 3, 2); b.fillRect(cx + 6, cy - 1, 3, 2);
      if (realSpeed && kmh < 3) { b.fillRect(cx - 9, cy - 2, 4, 3); b.fillRect(cx + 6, cy - 2, 4, 3); } // luces de freno
      b.fillRect(cx - 2, cy + 1, 5, 1); // matrícula
      // velocímetro: real con GPS; "---" mientras busca satélites
      b.clearRect(2, H - 12, 62, 12);
      b.fillStyle = ON; b.font = 'bold 11px Consolas, "Lucida Console", monospace'; b.textBaseline = 'top'; b.textAlign = 'left';
      if (realSpeed) {
        const txt = Math.round(kmh) + 'km/h';
        b.fillText(txt, 3, H - 11);
        const tw = b.measureText(txt).width;
        b.fillStyle = DIM; b.font = 'bold 8px Consolas, monospace'; b.fillText('GPS', 6 + tw, H - 9);
      } else if (drive && drive.active) {
        if (Math.floor(t * 2) % 2) b.fillText('---km/h', 3, H - 11);
      } else b.fillText(Math.round(kmh) + 'km/h', 3, H - 11);
    }

    // ---------------- puerto pesquero ----------------
    harbor(b, t, dt, au) {
      const sea = 44;
      this.sky(b, t, au, sea - 4, 70);
      this.moon(b, 112, 12, 7, false);
      // gaviotas
      for (const g of this.gulls) {
        g.x += g.sp * dt; if (g.x > W + 4) { g.x = -4; g.y = 6 + Math.random() * 16; }
        const up = Math.sin(t * 7 + g.ph) > 0;
        const x = Math.round(g.x), y = Math.round(g.y + Math.sin(t * 1.3 + g.ph) * 1.5);
        b.fillStyle = ON; b.fillRect(x, y, 1, 1);
        if (up) { b.fillRect(x - 1, y - 1, 1, 1); b.fillRect(x + 1, y - 1, 1, 1); b.fillRect(x - 2, y - 1, 1, 1); b.fillRect(x + 2, y - 1, 1, 1); }
        else { b.fillRect(x - 1, y - 1, 1, 1); b.fillRect(x + 1, y - 1, 1, 1); b.fillRect(x - 2, y, 1, 1); b.fillRect(x + 2, y, 1, 1); }
      }
      // mar: olas que se mueven, más altas con los graves
      b.clearRect(0, sea, W, H - sea);
      b.fillStyle = DIM; for (let x = 0; x < W; x += 2) b.fillRect(x, sea, 1, 1);
      const amp = 0.75 - au.bass * 0.35;
      for (let y = sea + 2; y < H; y += 3) {
        const k = (y - sea) / (H - sea);
        for (let x = 0; x < W; x++) {
          const w = Math.sin(x * (0.18 - k * 0.08) - t * (1.3 + k) + y * 1.7) + Math.sin(x * 0.05 + t * 0.6 + y) * 0.4;
          if (w > amp + 0.25) { b.fillStyle = k > 0.5 ? ON : DIM; b.fillRect(x, y, 1, 1); }
          else if (w > amp) { b.fillStyle = DIM; b.fillRect(x, y + 1, 1, 1); }
        }
      }
      // reflejo de la luna
      for (let y = sea + 1; y < H; y++) {
        const w = 1 + (y - sea) * 0.35, sx = 112 + Math.sin(t * 2.4 + y * 0.9) * (1 + (y - sea) * 0.12);
        if ((y + Math.floor(t * 6)) % 2) { b.fillStyle = (y % 3) ? DIM : ON; b.fillRect(Math.round(sx - w / 2), y, Math.max(1, Math.round(w)), 1); }
      }
      // muelle de madera con postes, farol y cajas
      b.fillStyle = ON; b.fillRect(0, sea - 3, 74, 1); b.fillStyle = DIM; b.fillRect(0, sea - 2, 74, 1);
      for (let x = 2; x < 74; x += 9) { b.fillStyle = ON; b.fillRect(x, sea - 2, 1, 9); }
      b.fillStyle = ON; b.fillRect(6, sea - 7, 4, 4); b.fillRect(11, sea - 6, 3, 3); b.fillStyle = DIM; b.fillRect(7, sea - 6, 2, 2);
      b.fillStyle = ON; b.fillRect(60, sea - 16, 1, 13); b.fillRect(58, sea - 17, 5, 1); b.fillRect(60, sea - 18, 1, 1);
      if (Math.sin(t * 13) > -0.9) { b.fillStyle = DIM; for (let g = 1; g < 7; g++) b.fillRect(60 - (g >> 1), sea - 17 + g, (g | 1), 1); b.fillStyle = ON; b.fillRect(60, sea - 16, 1, 1); }
      b.fillStyle = ON; b.fillRect(68, sea - 5, 2, 2); // noray
      // barcas: una amarrada que se mece y otra que cruza
      this.boat(b, 84, sea, t, 0, au, 1);
      this.boatX += dt * 6; if (this.boatX > 205) this.boatX = -30;
      this.boat(b, Math.round(this.boatX), sea + 6, t, 1.7, au, -1);
      // faro sobre las rocas con haz giratorio
      const lx = 232, ltop = 12;
      b.fillStyle = DIM; for (let x = 212; x < W; x++) { const h = Math.round(5 + 3 * Math.sin(x * 0.5) + (x - 212) * 0.15); for (let y = sea - h + 4; y < sea + 4; y += 2) b.fillRect(x, y, 1, 1); }
      b.fillStyle = ON;
      for (let y = ltop + 6; y < sea - 2; y++) {
        const half = Math.round(2 + (y - ltop) * 0.08);
        b.fillRect(lx - half, y, 1, 1); b.fillRect(lx + half, y, 1, 1);
        if (Math.floor((y - ltop) / 5) % 2) { b.fillStyle = DIM; b.fillRect(lx - half + 1, y, half * 2 - 1, 1); b.fillStyle = ON; }
      }
      b.fillRect(lx - 3, ltop + 5, 7, 1); b.fillRect(lx - 2, ltop + 1, 5, 4); b.fillRect(lx - 1, ltop, 3, 1); b.fillRect(lx, ltop - 1, 1, 1);
      const ang = t * 1.1, c = Math.cos(ang), s = Math.sin(ang);
      if (Math.abs(s) < 0.25) { b.fillStyle = ON; b.fillRect(lx - 3, ltop + 1, 7, 3); } // destello hacia nosotros
      const len = 170 * Math.abs(c), dir = c > 0 ? -1 : 1;
      if (len > 8) {
        for (let i = 5; i < len; i++) {
          const spread = 1 + i * 0.07;
          const x = Math.round(lx + dir * i);
          if (x < 0 || x >= W) break;
          b.fillStyle = DIM;
          b.fillRect(x, Math.round(ltop + 2 - spread), 1, 1);
          b.fillRect(x, Math.round(ltop + 2 + spread), 1, 1);
          if (i % 4 === 0) for (let y = Math.round(ltop + 3 - spread); y < ltop + 2 + spread; y += 2) b.fillRect(x, y, 1, 1);
        }
      }
    }

    boat(b, x0, y0, t, ph, au, facing) {
      const bob = Math.sin(t * 1.4 + ph) * 1.2 + au.bass * 1.2;
      const roll = Math.sin(t * 1.1 + ph) * 0.06;
      const y = Math.round(y0 - 2 + bob);
      b.clearRect(x0 - 12, y - 16, 26, 20);
      b.fillStyle = ON;
      for (let i = -11; i <= 11; i++) b.fillRect(x0 + i, Math.round(y + i * roll), 1, 1);           // cubierta
      for (let i = -9; i <= 9; i++) b.fillRect(x0 + i, Math.round(y + 3 + i * roll), 1, 1);         // quilla
      b.fillRect(x0 - 11, Math.round(y - 11 * roll), 1, 2); b.fillRect(x0 + 11, Math.round(y + 11 * roll), 1, 2);
      b.fillRect(x0 - 10, y + 2, 1, 1); b.fillRect(x0 + 10, y + 2, 1, 1);
      b.fillStyle = DIM; for (let i = -9; i <= 9; i += 2) b.fillRect(x0 + i, Math.round(y + 1 + i * roll), 1, 2);
      // cabina con ventana encendida
      const cx = x0 - 6 * facing;
      b.fillStyle = ON; b.fillRect(cx - 3, y - 5, 7, 1); b.fillRect(cx - 3, y - 4, 1, 4); b.fillRect(cx + 3, y - 4, 1, 4);
      b.fillRect(cx - 1, y - 3, 2, 1);
      // mástil, farol y aparejos
      const mx = x0 + 3 * facing, top = y - 14;
      b.fillRect(mx, top, 1, 14);
      b.fillRect(mx, top - 1, 1, 1);
      if (Math.sin(t * 3 + ph) > -0.5) { b.fillStyle = DIM; b.fillRect(mx - 1, top - 1, 3, 1); b.fillRect(mx, top - 2, 1, 1); }
      b.fillStyle = DIM;
      for (let i = 0; i < 10; i++) b.fillRect(Math.round(mx + facing * i * 0.9), top + i + 1, 1, 1);
      for (let i = 0; i < 8; i++) b.fillRect(Math.round(mx - facing * i * 0.6), top + 2 + i, 1, 1);
      // reflejo de la barca en el agua
      if (Math.floor(t * 4 + ph) % 2) for (let i = -8; i <= 8; i += 3) b.fillRect(x0 + i, y + 6, 1, 1);
    }

    // ---------------- lluvia en el parabrisas ----------------
    rain(b, t, dt, au) {
      if (!this.drops) {
        this.drops = [];
        const r = rng(77);
        this.bokeh = Array.from({ length: 12 }, () => ({ x: r() * W, y: 6 + r() * 34, r: 2 + r() * 4, ph: r() * 6, on: r() < 0.3 }));
      }
      // luces de la ciudad desenfocadas al otro lado del cristal
      for (const k of this.bokeh) {
        const pulse = k.on ? Math.sin(t * 1.5 + k.ph) > -0.3 : true;
        if (!pulse) continue;
        for (let a = 0; a < 6.28; a += 0.5) { b.fillStyle = DIM; b.fillRect(Math.round(k.x + Math.cos(a) * k.r), Math.round(k.y + Math.sin(a) * k.r * 0.8), 1, 1); }
        if (k.on) { b.fillStyle = ON; b.fillRect(Math.round(k.x), Math.round(k.y), 1, 1); }
      }
      // semáforo a lo lejos
      b.fillStyle = DIM; b.fillRect(200, 12, 1, 20); b.fillStyle = ON; b.fillRect(199, 10 + (Math.floor(t / 4) % 3) * 2, 3, 2);
      // gotas nuevas (más con la música)
      const n = Math.random() < 0.6 + au.high ? 2 + Math.floor(au.energy * 4) : 0;
      for (let i = 0; i < n && this.drops.length < 260; i++) this.drops.push({ x: Math.random() * W, y: Math.random() * 54, slide: Math.random() < 0.08 ? 8 + Math.random() * 16 : 0, big: Math.random() < 0.25 });
      // limpiaparabrisas: barrido cada 3,2 s
      const cyc = (t % 3.2) / 3.2;
      const ease = x => 0.5 - Math.cos(Math.PI * x) / 2;
      const ang = cyc < 0.35 ? ease(cyc / 0.35) : cyc < 0.7 ? ease(1 - (cyc - 0.35) / 0.35) : 0;
      const prev = this.wiperAng === undefined ? 0 : this.wiperAng;
      this.wiperAng = ang;
      const wipers = [[72, 70], [176, 70]];
      const lo = Math.min(prev, ang), hi = Math.max(prev, ang);
      this.drops = this.drops.filter(d => {
        for (const [px, len] of wipers) {
          const dx = d.x - px, dy = 66 - d.y, dist = Math.hypot(dx, dy);
          if (dist > len) continue;
          const a = Math.atan2(dy, -dx) / Math.PI; // 0 = izquierda, 1 = derecha
          if (a >= lo - 0.03 && a <= hi + 0.03 && hi - lo > 0.001) return false;
        }
        return true;
      });
      for (const d of this.drops) {
        if (d.slide > 0) { d.y += dt * d.slide; if (d.y > 56) d.slide = 0; b.fillStyle = DIM; b.fillRect(Math.round(d.x), Math.round(d.y) - 3, 1, 3); }
        b.fillStyle = ON; b.fillRect(Math.round(d.x), Math.round(d.y), d.big ? 2 : 1, 1);
        if (d.big) { b.fillStyle = DIM; b.fillRect(Math.round(d.x), Math.round(d.y) + 1, 2, 1); }
      }
      // escobillas
      for (const [px, len] of wipers) {
        const a = Math.PI * ang;
        const ex = px - Math.cos(a) * len, ey = 66 - Math.sin(a) * len;
        for (let i = 0; i <= len; i += 1) {
          const x = Math.round(px + (ex - px) * i / len), y = Math.round(66 + (ey - 66) * i / len);
          b.fillStyle = ON; b.fillRect(x, y, 2, 1);
          if (i > len * 0.3) { b.fillRect(x, y - 1, 1, 1); b.fillStyle = DIM; b.fillRect(x + 1, y - 1, 1, 1); }
        }
      }
      // salpicadero
      b.clearRect(0, 58, W, 6);
      b.fillStyle = ON;
      for (let x = 0; x < W; x++) b.fillRect(x, 58 + Math.round(Math.abs(x - 128) / 64), 1, 1);
    }

    // ---------------- túnel con luces naranjas ----------------
    tunnelLights(b, t, dt, au) {
      const vx = 128, vy = 30;
      this.tunZ = (this.tunZ || 0) + dt * (0.5 + au.energy * 1.4);
      // anillos del túnel acercándose
      for (let k = 0; k < 7; k++) {
        const z = frac(k / 7 + this.tunZ * 0.5), p = z * z;
        const w = 10 + p * 150, h = 6 + p * 40;
        b.fillStyle = p > 0.35 ? ON : DIM;
        for (let x = -w; x <= w; x += p > 0.35 ? 1 : 2) { const yy = vy - h * Math.sqrt(Math.max(0, 1 - (x / w) ** 2)); b.fillRect(Math.round(vx + x), Math.round(yy), 1, 1); }
      }
      // paredes y suelo
      for (let i = 0; i < 60; i++) {
        const p = i / 60;
        b.fillStyle = ON; b.fillRect(Math.round(vx - 10 - p * 150), Math.round(vy + p * 40), 1, 1); b.fillRect(Math.round(vx + 10 + p * 150), Math.round(vy + p * 40), 1, 1);
        if (i % 2) { b.fillStyle = DIM; b.fillRect(Math.round(vx - 10 - p * 150), Math.round(vy + p * 40) - 2, 1, 1); b.fillRect(Math.round(vx + 10 + p * 150), Math.round(vy + p * 40) - 2, 1, 1); }
      }
      // luces en las paredes, a ras de suelo
      for (let k = 0; k < 8; k++) {
        const z = frac(k / 8 + this.tunZ * 0.9), p = z * z;
        for (const side of [-1, 1]) { b.fillStyle = ON; b.fillRect(Math.round(vx + side * (10 + p * 150)) - (side > 0 ? 2 : 0), Math.round(vy + p * 40) - 4 - Math.round(p * 4), Math.max(1, Math.round(1 + p * 3)), 1); }
      }
      // dos filas de luces en el techo
      for (let k = 0; k < 10; k++) {
        const z = frac(k / 10 + this.tunZ), p = z * z;
        for (const side of [-1, 1]) {
          const x = vx + side * (4 + p * 70), y = vy - 4 - p * 36;
          const len = Math.max(2, Math.round(2 + p * 14)), th = p > 0.4 ? 2 : 1;
          b.fillStyle = ON; b.fillRect(Math.round(x - len / 2), Math.round(y), len, th);
          if (p > 0.2) { b.fillStyle = DIM; b.fillRect(Math.round(x - len / 2) - 1, Math.round(y) + th, len + 2, 1); }
        }
      }
      // líneas del carril
      for (let k = 0; k < 8; k++) {
        const z0 = frac(k / 8 + this.tunZ * 1.2), p0 = z0 * z0, p1 = Math.min(1, (z0 + 0.05) ** 2);
        const y0 = Math.round(vy + 2 + 32 * p0), y1 = Math.round(vy + 2 + 32 * p1);
        b.fillStyle = ON; b.fillRect(vx, y0, Math.max(1, Math.round(1 + p0 * 3)), Math.max(1, y1 - y0));
      }
      // salida del túnel al fondo (más brillante con el bajo)
      b.fillStyle = au.bass > 0.6 ? ON : DIM; b.fillRect(vx - 4, vy - 3, 9, 5);
    }

    // ---------------- ciudad de neón ----------------
    neonCity(b, t, dt, au) {
      if (!this.neon) {
        const r = rng(4242);
        const mk = (n, hMin, hMax) => { const a = []; let x = 0; while (x < W * 2) { const w = 10 + Math.floor(r() * 18), h = hMin + Math.floor(r() * (hMax - hMin)); a.push({ x, w, h, sign: r() < 0.35 ? ['BAR', 'HOTEL', '24H', 'NIGHT', 'CLUB', 'RADIO'][Math.floor(r() * 6)] : null, ph: r() * 6, wins: Array.from({ length: 12 }, () => [Math.floor(r() * (w - 2)) + 1, Math.floor(r() * (h - 3)) + 2]) }); x += w + 2 + Math.floor(r() * 4); } return a; };
        this.neon = { far: mk(0, 14, 34), near: mk(0, 10, 26), carsZ: 0 };
      }
      const N = this.neon;
      N.off = (N.off || 0) + dt * 6;
      // edificios lejanos (tenues) y cercanos (contorno brillante)
      const layer = (list, speed, bright, base) => {
        const span = list[list.length - 1].x + list[list.length - 1].w;
        for (const bl of list) {
          let x = Math.round(bl.x - (N.off * speed) % span);
          if (x + bl.w < 0) x += span;
          if (x >= W) continue;
          const top = base - bl.h;
          b.clearRect(x, top, bl.w, bl.h);
          b.fillStyle = bright ? ON : DIM;
          b.fillRect(x, top, bl.w, 1); b.fillRect(x, top, 1, bl.h); b.fillRect(x + bl.w - 1, top, 1, bl.h);
          b.fillStyle = DIM;
          for (const [wx, wy] of bl.wins) if (Math.sin(t * 0.3 + wx * wy + bl.ph) > -0.2) b.fillRect(x + wx, top + wy, 1, 1);
          if (bright && bl.sign) {
            const blink = Math.sin(t * 3 + bl.ph) > -0.6 || au.kick;
            if (blink) {
              b.fillStyle = ON; b.font = 'bold 8px Consolas, monospace'; b.textBaseline = 'top'; b.textAlign = 'center';
              b.fillText(bl.sign, x + bl.w / 2, top + 2);
            }
          }
        }
      };
      this.sky(b, t, au, 20, 40);
      layer(N.far, 0.4, false, 52);
      layer(N.near, 1, true, 54);
      // pantalla gigante con el espectro de la música
      const sx = 104, sy = 8, sw = 48, sh = 22;
      b.clearRect(sx - 1, sy - 1, sw + 2, sh + 2);
      b.fillStyle = ON; b.fillRect(sx - 1, sy - 1, sw + 2, 1); b.fillRect(sx - 1, sy + sh, sw + 2, 1); b.fillRect(sx - 1, sy, 1, sh); b.fillRect(sx + sw, sy, 1, sh);
      b.fillRect(sx + sw / 2 - 1, sy + sh + 1, 2, 54 - sy - sh - 1);
      for (let i = 0; i < 12; i++) {
        const v = au.spec ? au.spec[i] : 0, h = Math.round(v * (sh - 2));
        b.fillStyle = ON; b.fillRect(sx + 1 + i * 4, sy + sh - 1 - h, 3, h);
      }
      // calle con faros que pasan
      b.clearRect(0, 55, W, 9);
      b.fillStyle = DIM; for (let x = 0; x < W; x += 2) b.fillRect(x, 55, 1, 1);
      N.carsZ += dt * (40 + au.energy * 80);
      for (let k = 0; k < 4; k++) {
        const x = Math.round((N.carsZ * (k % 2 ? 1 : -1.3) + k * 70) % (W + 40));
        const xx = x < -20 ? x + W + 40 : x;
        b.fillStyle = ON; b.fillRect(xx, 58 + (k % 2) * 3, 8, 1);
        b.fillStyle = DIM; b.fillRect(xx - (k % 2 ? 6 : -8), 58 + (k % 2) * 3, 6, 1);
      }
    }

    // ---------------- atardecer en la playa ----------------
    sunset(b, t, dt, au) {
      const hz = 34, cx = 128;
      // sol "retro" con franjas, medio hundido en el mar
      const R = 18;
      for (let y = -R; y <= 0; y++) {
        const band = (y + R) % 5;
        if (y > -R * 0.55 && band === 0) continue; // franjas
        const half = Math.round(Math.sqrt(R * R - y * y));
        b.fillStyle = ON; b.fillRect(cx - half, hz + y, half * 2 + 1, 1);
      }
      // estrellas que empiezan a salir
      this.sky(b, t, au, 16, 30);
      // pájaros
      for (let i = 0; i < 3; i++) {
        const x = Math.round((t * (6 + i * 2) + i * 90) % (W + 20)) - 10, y = Math.round(8 + i * 4 + Math.sin(t * 1.2 + i) * 2);
        const up = Math.sin(t * 6 + i * 2) > 0;
        b.fillStyle = ON; b.fillRect(x, y, 1, 1); b.fillRect(x - 1, y - (up ? 1 : 0), 1, 1); b.fillRect(x + 1, y - (up ? 1 : 0), 1, 1);
      }
      // horizonte y reflejo del sol en el agua
      b.fillStyle = ON; b.fillRect(0, hz, W, 1);
      b.clearRect(0, hz + 1, W, H - hz - 1);
      for (let y = hz + 2; y < 52; y += 2) {
        const w = Math.round((R - (y - hz) * 0.6) * (0.8 + 0.4 * Math.sin(t * 2 + y))), sh = Math.round(Math.sin(t * 1.5 + y * 0.7) * 2);
        if (w > 0) { b.fillStyle = (y % 4) ? DIM : ON; b.fillRect(cx - w + sh, y, w * 2, 1); }
        for (let x = (y * 7) % 11; x < W; x += 11 + (y % 3)) { b.fillStyle = DIM; if (Math.abs(x - cx) > R + 2) b.fillRect(x + Math.round(Math.sin(t + y) * 2), y, 3, 1); }
      }
      // olas que llegan a la orilla (más grandes con el bajo)
      const shore = 55 + Math.round(Math.sin(t * 0.8) * 2 - au.bass * 2);
      for (let x = 0; x < W; x++) {
        const y = shore + Math.round(Math.sin(x * 0.12 + t * 1.6) * 1.2);
        b.fillStyle = ON; b.fillRect(x, y, 1, 1);
        if ((x + Math.floor(t * 8)) % 3 === 0) { b.fillStyle = DIM; b.fillRect(x, y + 1, 1, 1); }
      }
      // arena
      b.fillStyle = DIM; for (let y = shore + 3; y < H; y += 2) for (let x = (y % 4); x < W; x += 4) b.fillRect(x, y, 1, 1);
      // palmeras que se mecen
      this.palm(b, 26, 63, 34, t, 0);
      this.palm(b, 222, 63, 28, t, 1.7);
      this.palm(b, 244, 63, 22, t, 0.9);
    }

    palm(b, x0, y0, h, t, ph) {
      const sway = Math.sin(t * 0.9 + ph) * 3;
      let tx = x0, ty = y0;
      b.fillStyle = ON;
      for (let i = 0; i <= h; i++) {
        const k = i / h;
        tx = x0 + sway * k * k + k * 6 * (x0 < 128 ? 1 : -1);
        ty = y0 - i;
        b.fillRect(Math.round(tx), ty, 2, 1);
        if (i % 3 === 0) { b.fillStyle = DIM; b.fillRect(Math.round(tx) - 1, ty, 1, 1); b.fillStyle = ON; }
      }
      // hojas
      for (let f = 0; f < 6; f++) {
        const a = -Math.PI / 2 + (f - 2.5) * 0.55 + Math.sin(t * 1.3 + ph + f) * 0.08;
        const len = 10 + (f % 2) * 4;
        for (let i = 1; i <= len; i++) {
          const k = i / len;
          const x = tx + Math.cos(a) * i * 1.3, y = ty + Math.sin(a) * i * 0.6 + k * k * 7;
          b.fillStyle = i > len * 0.7 ? DIM : ON;
          b.fillRect(Math.round(x), Math.round(y), 1, 1);
        }
      }
      b.fillStyle = ON; b.fillRect(Math.round(tx) - 1, ty + 1, 2, 2); b.fillRect(Math.round(tx) + 2, ty + 2, 2, 2); // cocos
    }

    draw(name, b, au) {
      const now = performance.now();
      const dt = Math.min(0.1, (now - this.last) / 1000);
      this.last = now;
      const t = now / 1000;
      if (name === 'NIGHT SKY') this.nightSky(b, t, dt, au);
      else if (name === 'HIGHWAY') this.highway(b, t, dt, au);
      else if (name === 'HARBOR') this.harbor(b, t, dt, au);
      else if (name === 'RAIN') this.rain(b, t, dt, au);
      else if (name === 'TUNNEL LIGHTS') this.tunnelLights(b, t, dt, au);
      else if (name === 'NEON CITY') this.neonCity(b, t, dt, au);
      else if (name === 'SUNSET') this.sunset(b, t, dt, au);
    }
  }

  window.NDScenes = { Scenes };
})();
