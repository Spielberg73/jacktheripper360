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

    draw(name, b, au) {
      const now = performance.now();
      const dt = Math.min(0.1, (now - this.last) / 1000);
      this.last = now;
      const t = now / 1000;
      if (name === 'NIGHT SKY') this.nightSky(b, t, dt, au);
      else if (name === 'HIGHWAY') this.highway(b, t, dt, au);
      else if (name === 'HARBOR') this.harbor(b, t, dt, au);
    }
  }

  window.NDScenes = { Scenes };
})();
