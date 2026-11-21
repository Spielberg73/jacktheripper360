// NightDrive Player — pantalla de matriz de puntos estilo autorradio 2000
(function () {
  const W = 256, H = 64;          // resolución en "puntos"
  const GFX_W = 96;               // zona de animación a la izquierda
  const MODES = ['SPECTRUM', 'LEVEL METER', 'SCOPE', 'SPEAKER', 'TUNNEL', 'FULL SPECTRUM', 'CLOCK', 'FM CHANNELS', 'NIGHT SKY', 'HIGHWAY', 'HARBOR'];
  const SCENES = new Set(['NIGHT SKY', 'HIGHWAY', 'HARBOR']);
  const FONT = 'bold 12px Consolas, "Lucida Console", "Courier New", monospace';
  const FONT_BIG = 'bold 34px Consolas, "Lucida Console", "Courier New", monospace';

  function logRanges(n, f0 = 30, f1 = 16000) {
    const out = [];
    const l0 = Math.log(f0), l1 = Math.log(f1);
    for (let i = 0; i < n; i++) out.push([Math.exp(l0 + (l1 - l0) * i / n), Math.exp(l0 + (l1 - l0) * (i + 1) / n)]);
    return out;
  }

  class DotDisplay {
    constructor(canvas, engine) {
      this.canvas = canvas;
      this.ctx = canvas.getContext('2d');
      this.engine = engine;
      this.buf = document.createElement('canvas');
      this.buf.width = W; this.buf.height = H;
      this.b = this.buf.getContext('2d', { willReadFrequently: true });
      this.dots = document.createElement('canvas');
      this.dctx = this.dots.getContext('2d');
      this.ghost = document.createElement('canvas');
      this.mode = 0;
      this.colors = ['#ffb000', '#ff5a00'];
      this.rainbow = false;
      this.brightness = 1;
      this.state = { title: '', artist: '', track: 0, total: 0, time: 0, duration: 0, playing: false, hasDisc: false, format: '' };
      this.message = null;
      this.introStart = performance.now();
      this.scroll = { key: '', t0: 0 };
      this.spec16 = new Float32Array(16); this.peak16 = new Float32Array(16); this.ranges16 = logRanges(16);
      this.spec64 = new Float32Array(64); this.peak64 = new Float32Array(64); this.ranges64 = logRanges(64);
      this.lvl = { L: 0, R: 0, pL: 0, pR: 0 };
      this.stars = Array.from({ length: 70 }, () => this._star());
      this.rings = [];
      this.energy = 0;
      this.tunnelZ = 0;
      this.scenes = window.NDScenes ? new window.NDScenes.Scenes() : null;
      this.au = { energy: 0, bass: 0, high: 0, kick: false };
      this.hud = { title: null, at: 0 };
      new ResizeObserver(() => this.resize()).observe(canvas);
      this.resize();
    }

    _star() { return { x: (Math.random() - 0.5) * 2, y: (Math.random() - 0.5) * 2, z: Math.random() }; }

    setColors(a, b, rainbow) { this.colors = [a, b]; this.rainbow = !!rainbow; this._buildGhost(); }
    setMode(i) { this.mode = (i + MODES.length) % MODES.length; this.flash(MODES[this.mode]); }
    nextMode() { this.setMode(this.mode + 1); }
    flash(text, sub = '', ms = 1600) { this.message = { text, sub, until: performance.now() + ms }; }
    intro() { this.introStart = performance.now(); }

    resize() {
      const dpr = window.devicePixelRatio || 1;
      const r = this.canvas.getBoundingClientRect();
      if (!r.width) return;
      this.canvas.width = Math.round(r.width * dpr);
      this.canvas.height = Math.round(r.height * dpr);
      this.dots.width = this.canvas.width; this.dots.height = this.canvas.height;
      this.ghost.width = this.canvas.width; this.ghost.height = this.canvas.height;
      this.px = this.canvas.width / W; this.py = this.canvas.height / H;
      this._buildGhost();
    }

    _buildGhost() {
      if (!this.ghost.width) return;
      const g = this.ghost.getContext('2d');
      g.clearRect(0, 0, this.ghost.width, this.ghost.height);
      g.fillStyle = this.colors[0];
      g.globalAlpha = 0.07;
      const dw = this.px * 0.78, dh = this.py * 0.78;
      for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) g.fillRect(x * this.px, y * this.py, dw, dh);
    }

    // ---------- dibujo en el buffer de baja resolución ----------
    text(str, x, y, align = 'left', font = FONT) {
      const b = this.b;
      b.font = font; b.textAlign = align; b.textBaseline = 'top';
      b.fillText(str, x, y);
    }

    scrollText(str, x, y, w, key) {
      const b = this.b;
      b.font = FONT;
      const tw = b.measureText(str).width;
      b.save();
      b.beginPath(); b.rect(x, y - 1, w, 14); b.clip();
      if (tw <= w) { this.text(str, x, y); }
      else {
        if (this.scroll.key !== key) this.scroll = { key, t0: performance.now() };
        const t = (performance.now() - this.scroll.t0) / 1000;
        const gap = 40, speed = 28, hold = 1.6;
        const cycle = (tw + gap) / speed;
        const tt = Math.max(0, (t % (cycle + hold)) - hold);
        const off = Math.min(tt * speed, tw + gap);
        this.text(str, x - off, y);
        this.text(str, x - off + tw + gap, y);
      }
      b.restore();
    }

    fmt(t) {
      if (!isFinite(t) || t < 0) t = 0;
      const m = Math.floor(t / 60), s = Math.floor(t % 60);
      return `${m < 10 ? '0' : ''}${m}:${s < 10 ? '0' : ''}${s}`;
    }

    update() {
      const e = this.engine;
      if (!e) return;
      e.spectrumBands(this.ranges16, this.spec16);
      e.spectrumBands(this.ranges64, this.spec64);
      for (let i = 0; i < 16; i++) this.peak16[i] = Math.max(this.spec16[i], this.peak16[i] - 0.012);
      for (let i = 0; i < 64; i++) this.peak64[i] = Math.max(this.spec64[i], this.peak64[i] - 0.012);
      const lv = e.levels();
      const db = v => Math.max(0, Math.min(1, (20 * Math.log10(Math.max(v, 1e-5)) + 48) / 48));
      const L = db(lv.L.rms * 1.4), R = db(lv.R.rms * 1.4);
      this.lvl.L = Math.max(L, this.lvl.L - 0.04); this.lvl.R = Math.max(R, this.lvl.R - 0.04);
      this.lvl.pL = Math.max(db(lv.L.peak), this.lvl.pL - 0.008); this.lvl.pR = Math.max(db(lv.R.peak), this.lvl.pR - 0.008);
      let bass = 0; for (let i = 0; i < 4; i++) bass += this.spec16[i];
      bass /= 4;
      const prevE = this.energy;
      this.energy += (bass - this.energy) * 0.35;
      let hi = 0; for (let i = 11; i < 16; i++) hi += this.spec16[i];
      this.au.high += (hi / 5 - this.au.high) * 0.3;
      this.au.bass = this.energy;
      this.au.energy += ((this.lvl.L + this.lvl.R) / 2 - this.au.energy) * 0.05;
      this.au.kick = this.energy - prevE > 0.08;
    }

    // ---------- animaciones ----------
    gfxSpectrum(x0, w, bars, spec, peak, gap = 1) {
      const b = this.b;
      const bw = Math.floor((w - gap * (bars - 1)) / bars);
      for (let i = 0; i < bars; i++) {
        const h = Math.round(spec[i] * (H - 2));
        const x = x0 + i * (bw + gap);
        for (let y = 0; y < h; y += 3) b.fillRect(x, H - 1 - y - 1, bw, 2);
        const ph = Math.round(peak[i] * (H - 2));
        if (ph > 1) b.fillRect(x, H - 1 - ph, bw, 1);
      }
    }

    gfxLevel(x0, w) {
      const b = this.b;
      const drawBar = (y, v, p, label) => {
        this.text(label, x0, y - 1);
        const bx = x0 + 10, bwid = w - 12;
        const segs = Math.floor(bwid / 3);
        const lit = Math.round(v * segs);
        for (let s = 0; s < segs; s++) {
          const hh = s > segs * 0.8 ? 10 : 8;
          if (s < lit) b.fillRect(bx + s * 3, y + (10 - hh) / 2, 2, hh);
          else b.fillRect(bx + s * 3, y + 4, 2, 1);
        }
        b.fillRect(bx + Math.round(p * segs) * 3, y - 1, 2, 12);
      };
      drawBar(12, this.lvl.L, this.lvl.pL, 'L');
      drawBar(40, this.lvl.R, this.lvl.pR, 'R');
      for (let s = 0; s <= 6; s++) this.b.fillRect(x0 + 10 + Math.round(s * (w - 13) / 6), 30, 1, 2);
    }

    gfxScope(x0, w) {
      const b = this.b;
      const d = this.engine.timeL;
      const step = d.length / w;
      b.beginPath();
      for (let i = 0; i < w; i++) {
        const v = d[Math.floor(i * step)] || 0;
        const y = H / 2 - v * (H / 2 - 2) * 1.6;
        if (i === 0) b.moveTo(x0 + i, y); else b.lineTo(x0 + i, y);
      }
      b.strokeStyle = '#fff'; b.lineWidth = 1.4; b.stroke();
      for (let x = x0; x < x0 + w; x += 4) b.fillRect(x, H / 2, 1, 1);
    }

    gfxSpeaker(x0, w) {
      const b = this.b;
      const cx = x0 + w / 2, cy = H / 2;
      const e = this.energy;
      if (e > 0.62 && Math.random() < 0.25) this.rings.push({ r: 16 + e * 8, a: 1 });
      b.strokeStyle = '#fff';
      this.rings = this.rings.filter(r => (r.r += 1.6) < 60);
      b.lineWidth = 1;
      for (const r of this.rings) { b.beginPath(); b.arc(cx, cy, r.r, 0, Math.PI * 2); b.stroke(); }
      const R = 18 + e * 9;
      b.lineWidth = 2;
      b.beginPath(); b.arc(cx, cy, R + 4, 0, Math.PI * 2); b.stroke();
      b.lineWidth = 1;
      b.beginPath(); b.arc(cx, cy, R, 0, Math.PI * 2); b.stroke();
      b.beginPath(); b.arc(cx, cy, R * 0.55, 0, Math.PI * 2); b.stroke();
      b.beginPath(); b.arc(cx, cy, 4 + e * 4, 0, Math.PI * 2); b.fillStyle = '#fff'; b.fill();
    }

    gfxTunnel(x0, w) {
      const b = this.b;
      const cx = x0 + w / 2, cy = H / 2;
      const speed = 0.006 + this.energy * 0.05;
      this.tunnelZ = (this.tunnelZ + speed * 6) % 1;
      b.strokeStyle = '#fff'; b.lineWidth = 1;
      for (let k = 0; k < 6; k++) {
        const z = ((k + this.tunnelZ) / 6);
        const s = Math.pow(z, 2.2);
        const rw = s * w * 0.62, rh = s * H * 0.62;
        if (rw < 2) continue;
        b.strokeRect(Math.round(cx - rw) + 0.5, Math.round(cy - rh) + 0.5, Math.round(rw * 2), Math.round(rh * 2));
      }
      for (const st of this.stars) {
        st.z -= speed;
        if (st.z <= 0.02) Object.assign(st, this._star(), { z: 1 });
        const sx = cx + (st.x / st.z) * w * 0.12, sy = cy + (st.y / st.z) * H * 0.12;
        if (sx < x0 || sx >= x0 + w || sy < 0 || sy >= H) { Object.assign(st, this._star(), { z: 1 }); continue; }
        const sz = st.z < 0.3 ? 2 : 1;
        b.fillRect(Math.round(sx), Math.round(sy), sz, sz);
      }
      b.fillRect(x0, 0, w, 1); b.fillRect(x0, H - 1, w, 1);
    }

    // Medidores por canal del chip (solo VGM): FM1-6 | PSG1-3 + ruido
    gfxChannels(x0, w) {
      const lv = this.engine.media && this.engine.media.levels;
      if (!lv) { this.gfxSpectrum(x0, w, 16, this.spec16, this.peak16, 1); return; }
      if (!this.chPeak) this.chPeak = new Float32Array(10);
      const b = this.b;
      const bw = 7, gap = 2, top = 2, bottom = H - 10;
      let x = x0 + 1;
      for (let i = 0; i < 10; i++) {
        if (i === 6) { for (let y = top; y < bottom; y += 2) b.fillRect(x, y, 1, 1); x += 3; }
        const v = Math.pow(lv[i], 0.6);
        this.chPeak[i] = Math.max(v, this.chPeak[i] - 0.015);
        const h = Math.round(v * (bottom - top));
        for (let y = 0; y < h; y += 3) b.fillRect(x, bottom - y - 2, bw, 2);
        const ph = Math.round(this.chPeak[i] * (bottom - top));
        if (ph > 1) b.fillRect(x, bottom - ph - 1, bw, 1);
        x += bw + gap;
      }
      b.font = 'bold 8px Consolas, monospace'; b.textBaseline = 'top'; b.textAlign = 'left';
      b.fillText('FM', x0 + 20, H - 8);
      b.fillText('PSG', x0 + 66, H - 8);
    }

    drawInfo(x0, w) {
      const s = this.state;
      const now = performance.now();
      const msg = this.message && now < this.message.until ? this.message : null;
      if (!s.hasDisc && !s.radio) {
        this.text('NO DISC', x0 + w / 2, 10, 'center');
        if (Math.floor(now / 600) % 2 === 0) this.text('PRESS OPEN', x0 + w / 2, 30, 'center');
        return;
      }
      const blink = !s.playing && Math.floor(now / 500) % 2 === 1;
      this.text(s.radio ? `FM P${s.track}` : `${s.format || 'MP3'} T${String(s.track).padStart(2, '0')}`, x0, 1);
      if (!blink) this.text(this.fmt(s.time), x0 + w - 1, 1, 'right');
      if (msg) {
        this.text(msg.text, x0 + w / 2, msg.sub ? 19 : 26, 'center');
        if (msg.sub) this.text(msg.sub, x0 + w / 2, 35, 'center');
      } else {
        this.scrollText(s.title || 'UNKNOWN', x0, 20, w, 't' + s.title);
        this.scrollText(s.artist || '', x0, 38, w, 'a' + s.artist + s.title);
      }
      if (s.radio) { // en directo: indicador de señal en lugar de barra de progreso
        this.text('ON AIR', x0, 52);
        const lv = Math.max(this.lvl.L, this.lvl.R);
        for (let i = 0; i < 6; i++) { const h = 2 + i * 2; if (lv * 7 > i || i < 2) this.b.fillRect(x0 + w - 30 + i * 5, 63 - h, 3, h); }
        return;
      }
      const p = s.duration > 0 ? Math.min(1, s.time / s.duration) : 0;
      const pw = Math.round(p * w);
      this.b.fillRect(x0, 58, pw, 3);
      for (let x = pw; x < w; x += 2) this.b.fillRect(x0 + x, 59, 1, 1);
    }

    drawIntro(t) {
      const b = this.b;
      if (t < 0.8) {               // barrido
        const x = (t / 0.8) * W;
        b.fillRect(0, 0, x, H);
      } else if (t < 1.4) {
        const k = (t - 0.8) / 0.6;
        for (let y = 0; y < H; y += 4) b.fillRect(0, y, W * (1 - k), 2);
        this.text('NIGHTDRIVE', W / 2, 14, 'center', FONT_BIG);
      } else {
        const k = Math.min(1, (t - 1.4) / 0.8);
        const word = 'NIGHTDRIVE';
        this.text(word.slice(0, Math.ceil(word.length * k)), W / 2, 6, 'center', FONT_BIG);
        this.text('-  W E L C O M E  -', W / 2, 46, 'center');
      }
    }

    render() {
      const b = this.b;
      b.clearRect(0, 0, W, H);
      b.fillStyle = '#fff';
      const t = (performance.now() - this.introStart) / 1000;
      this.update();
      if (t < 3.0) this.drawIntro(t);
      else {
        const mode = MODES[this.mode];
        if (SCENES.has(mode) && this.scenes) {
          this.scenes.draw(mode, b, this.au);
          this.sceneHud();
        } else if (mode === 'FULL SPECTRUM') {
          this.gfxSpectrum(0, W, 64, this.spec64, this.peak64, 1);
          if (this.message && performance.now() < this.message.until) {
            b.clearRect(40, 14, W - 80, 30);
            this.text(this.message.text, W / 2, 21, 'center');
          }
        } else if (mode === 'CLOCK') {
          const d = new Date();
          const hh = String(d.getHours()).padStart(2, '0'), mm = String(d.getMinutes()).padStart(2, '0');
          const colon = d.getMilliseconds() < 500 ? ':' : ' ';
          this.text(`${hh}${colon}${mm}`, GFX_W / 2 + 2, 10, 'center', FONT_BIG);
          this.text(d.toLocaleDateString('es-ES', { day: '2-digit', month: 'short' }).toUpperCase(), GFX_W / 2, 48, 'center');
          this.drawInfo(GFX_W + 6, W - GFX_W - 8);
        } else {
          if (mode === 'SPECTRUM') this.gfxSpectrum(0, GFX_W, 16, this.spec16, this.peak16, 1);
          else if (mode === 'LEVEL METER') this.gfxLevel(0, GFX_W);
          else if (mode === 'SCOPE') this.gfxScope(0, GFX_W);
          else if (mode === 'SPEAKER') this.gfxSpeaker(0, GFX_W);
          else if (mode === 'TUNNEL') this.gfxTunnel(0, GFX_W);
          else if (mode === 'FM CHANNELS') this.gfxChannels(0, GFX_W);
          b.clearRect(GFX_W, 0, W - GFX_W, H);
          b.fillStyle = '#fff';
          for (let y = 0; y < H; y += 2) b.fillRect(GFX_W + 2, y, 1, 1);
          this.drawInfo(GFX_W + 6, W - GFX_W - 8);
        }
      }
      this.blit();
    }

    // hora arriba a la derecha y el título al cambiar de canción (y cada 30 s)
    sceneHud() {
      const b = this.b, s = this.state, now = performance.now();
      if (s.title !== this.hud.title) { this.hud.title = s.title; this.hud.at = now; }
      b.fillStyle = '#fff';
      if (s.hasDisc || s.radio) {
        b.clearRect(W - 38, 0, 38, 13);
        if (!(!s.playing && Math.floor(now / 500) % 2)) this.text(this.fmt(s.time), W - 2, 1, 'right');
        const since = (now - this.hud.at) / 1000;
        if (since < 7 || since % 30 < 6) {
          b.clearRect(0, 0, 150, 13);
          b.fillStyle = '#fff';
          this.scrollText(s.title || '', 2, 1, 146, 'hud' + s.title);
        }
      }
      const msg = this.message && now < this.message.until ? this.message : null;
      if (msg) { b.clearRect(56, 20, W - 112, 22); b.fillStyle = '#fff'; this.text(msg.text, W / 2, 25, 'center'); }
    }

    blit() {
      const img = this.b.getImageData(0, 0, W, H).data;
      const d = this.dctx, c = this.ctx;
      const cw = this.canvas.width, ch = this.canvas.height;
      d.globalCompositeOperation = 'source-over';
      d.clearRect(0, 0, cw, ch);
      d.fillStyle = '#fff';
      const px = this.px, py = this.py, dw = px * 0.8, dh = py * 0.8;
      for (let y = 0; y < H; y++) {
        const row = y * W * 4;
        for (let x = 0; x < W; x++) {
          const o = row + x * 4;
          if (img[o + 3] > 100) {
            if (img[o + 1] > 100) d.fillRect(x * px, y * py, dw, dh);
            else (this._dim || (this._dim = [])).push(x, y);
          }
        }
      }
      // puntos tenues (dibujados en rojo puro por las escenas)
      if (this._dim && this._dim.length) {
        d.fillStyle = 'rgba(255,255,255,0.3)';
        const dm = this._dim;
        for (let i = 0; i < dm.length; i += 2) d.fillRect(dm[i] * px, dm[i + 1] * py, dw, dh);
        dm.length = 0;
        d.fillStyle = '#fff';
      }
      d.globalCompositeOperation = 'source-in';
      let grad;
      if (this.rainbow) {
        grad = d.createLinearGradient(0, 0, cw, 0);
        const off = (performance.now() / 40) % 360;
        for (let i = 0; i <= 6; i++) grad.addColorStop(i / 6, `hsl(${(off + i * 60) % 360},100%,60%)`);
      } else {
        grad = d.createLinearGradient(0, 0, 0, ch);
        grad.addColorStop(0, this.colors[0]);
        grad.addColorStop(1, this.colors[1]);
      }
      d.fillStyle = grad;
      d.fillRect(0, 0, cw, ch);
      c.clearRect(0, 0, cw, ch);
      c.globalAlpha = 1;
      c.drawImage(this.ghost, 0, 0);
      c.globalAlpha = this.brightness;
      c.drawImage(this.dots, 0, 0);
      c.globalAlpha = 1;
    }
  }

  window.NDDisplay = { DotDisplay, MODES };
})();
