// NightDrive Player — ecualizador gráfico con analizador en tiempo real
(function () {
  const F_MIN = 16, F_MAX = 22000, DB = 12;

  class GraphicEQ {
    constructor({ canvas, fadersEl, engine, onChange }) {
      this.canvas = canvas;
      this.ctx = canvas.getContext('2d');
      this.fadersEl = fadersEl;
      this.engine = engine;
      this.onChange = onChange || (() => {});
      this.gains = new Array(engine.freqs.length).fill(0);
      this.levels = new Float32Array(31);
      this.peaks = new Float32Array(31);
      this.peakHold = new Float32Array(31);
      this.curveFreqs = new Float32Array(240);
      const l0 = Math.log(F_MIN), l1 = Math.log(F_MAX);
      for (let i = 0; i < 240; i++) this.curveFreqs[i] = Math.exp(l0 + (l1 - l0) * i / 239);
      this.curve = null;
      this.curveDirty = true;
      this.color = '#ff3b30';
      this.color2 = '#ffb000';
      this.painting = false;
      new ResizeObserver(() => this.resize()).observe(canvas);
      this._bindCanvas();
      this.buildFaders();
      this.resize();
    }

    get freqs() { return this.engine.freqs; }

    setColors(a, b) { this.color = a; this.color2 = b; }

    resize() {
      const dpr = window.devicePixelRatio || 1;
      const r = this.canvas.getBoundingClientRect();
      if (!r.width) return;
      this.canvas.width = Math.round(r.width * dpr);
      this.canvas.height = Math.round(r.height * dpr);
      this.dpr = dpr;
    }

    // ---- geometría ----
    fx(f) { const w = this.canvas.width; const p = 40 * this.dpr; return p + (Math.log(f / F_MIN) / Math.log(F_MAX / F_MIN)) * (w - p * 1.6); }
    xf(x) { const w = this.canvas.width; const p = 40 * this.dpr; return F_MIN * Math.pow(F_MAX / F_MIN, (x - p) / (w - p * 1.6)); }
    gy(db) { const h = this.canvas.height; const t = 18 * this.dpr, bt = 26 * this.dpr; return t + (1 - (db + DB) / (2 * DB)) * (h - t - bt); }
    yg(y) { const h = this.canvas.height; const t = 18 * this.dpr, bt = 26 * this.dpr; return (1 - (y - t) / (h - t - bt)) * 2 * DB - DB; }

    bandRanges() {
      const f = this.freqs, n = f.length;
      return f.map((c, i) => {
        const lo = i === 0 ? c / 1.12 : Math.sqrt(f[i - 1] * c);
        const hi = i === n - 1 ? Math.min(c * 1.12, 22000) : Math.sqrt(f[i + 1] * c);
        return [lo, hi];
      });
    }

    // ---- faders ----
    buildFaders() {
      const el = this.fadersEl;
      el.innerHTML = '';
      el.dataset.count = this.freqs.length;
      this.faders = this.freqs.map((f, i) => {
        const wrap = document.createElement('div');
        wrap.className = 'fader';
        wrap.innerHTML = `<span class="fv">0</span><div class="ft"><div class="fz"></div><div class="ff"></div><div class="fc"></div></div><span class="fl">${f >= 1000 ? (f / 1000) + 'k' : f}</span>`;
        const track = wrap.querySelector('.ft');
        const setFromY = y => {
          const r = track.getBoundingClientRect();
          const t = 1 - (y - r.top) / r.height;
          this.setGain(i, Math.round((t * 2 * DB - DB) * 2) / 2, true);
        };
        track.addEventListener('pointerdown', e => {
          track.setPointerCapture(e.pointerId);
          setFromY(e.clientY);
          const move = ev => setFromY(ev.clientY);
          const up = () => { track.removeEventListener('pointermove', move); track.removeEventListener('pointerup', up); track.removeEventListener('pointercancel', up); };
          track.addEventListener('pointermove', move);
          track.addEventListener('pointerup', up);
          track.addEventListener('pointercancel', up);
        });
        track.addEventListener('wheel', e => { e.preventDefault(); this.setGain(i, this.gains[i] + (e.deltaY < 0 ? 0.5 : -0.5), true); }, { passive: false });
        track.addEventListener('dblclick', () => this.setGain(i, 0, true));
        el.appendChild(wrap);
        return { wrap, val: wrap.querySelector('.fv'), fill: wrap.querySelector('.ff'), cap: wrap.querySelector('.fc') };
      });
      this.gains.forEach((g, i) => this._paintFader(i));
    }

    _paintFader(i) {
      const f = this.faders[i]; if (!f) return;
      const g = this.gains[i];
      const t = (g + DB) / (2 * DB);
      f.cap.style.bottom = `calc(${t * 100}% - 7px)`;
      const from = Math.min(t, 0.5), to = Math.max(t, 0.5);
      f.fill.style.bottom = `${from * 100}%`;
      f.fill.style.height = `${(to - from) * 100}%`;
      f.val.textContent = (g > 0 ? '+' : '') + (Number.isInteger(g) ? g : g.toFixed(1));
      f.wrap.classList.toggle('pos', g > 0);
      f.wrap.classList.toggle('neg', g < 0);
    }

    setGain(i, db, user) {
      db = Math.max(-DB, Math.min(DB, db));
      this.gains[i] = db;
      this.engine.setBandGain(i, db);
      this._paintFader(i);
      this.curveDirty = true;
      if (user) this.onChange('band');
    }

    setAll(gains, user) {
      gains.forEach((g, i) => this.setGain(i, g, false));
      if (user) this.onChange('preset');
    }

    gainAt(f) { // interpola la curva actual de deslizadores
      const fr = this.freqs, g = this.gains;
      return window.NDAudio.interpPoints(fr.map((x, i) => [x, g[i]]), f);
    }

    setBandCount(n) {
      if (n === this.freqs.length) return;
      const newFreqs = n === 10 ? window.NDAudio.ISO10 : window.NDAudio.ISO31;
      const gains = newFreqs.map(f => Math.round(this.gainAt(f) * 2) / 2);
      this.engine.setBandCount(n, gains);
      this.gains = gains;
      this.buildFaders();
      this.curveDirty = true;
      this.onChange('bands');
    }

    applyPreset(points) {
      this.setAll(this.freqs.map(f => Math.round(window.NDAudio.interpPoints(points, f) * 2) / 2), true);
    }

    // ---- pintar la curva con el ratón ----
    _bindCanvas() {
      const c = this.canvas;
      let last = null;
      const apply = e => {
        const r = c.getBoundingClientRect();
        const x = (e.clientX - r.left) * this.dpr, y = (e.clientY - r.top) * this.dpr;
        const db = Math.round(Math.max(-DB, Math.min(DB, this.yg(y))) * 2) / 2;
        const idx = this._nearest(x);
        if (last && last.idx !== idx) {
          const a = Math.min(last.idx, idx), b = Math.max(last.idx, idx);
          for (let k = a; k <= b; k++) {
            const t = (k - last.idx) / (idx - last.idx);
            this.setGain(k, Math.round((last.db + (db - last.db) * t) * 2) / 2, false);
          }
        } else this.setGain(idx, db, false);
        last = { idx, db };
        this.onChange('band');
      };
      c.addEventListener('pointerdown', e => { c.setPointerCapture(e.pointerId); this.painting = true; last = null; apply(e); });
      c.addEventListener('pointermove', e => { this.hover = e; if (this.painting) apply(e); });
      c.addEventListener('pointerleave', () => { this.hover = null; });
      c.addEventListener('pointerup', () => { this.painting = false; last = null; });
      c.addEventListener('pointercancel', () => { this.painting = false; last = null; });
    }

    _nearest(x) {
      let best = 0, bd = Infinity;
      this.freqs.forEach((f, i) => { const d = Math.abs(this.fx(f) - x); if (d < bd) { bd = d; best = i; } });
      return best;
    }

    markDirty() { this.curveDirty = true; }

    // ---- render ----
    render() {
      const c = this.ctx, w = this.canvas.width, h = this.canvas.height, dpr = this.dpr || 1;
      if (!w) return;
      const n = this.freqs.length;
      const ranges = this.bandRanges();
      const lv = this.engine.spectrumBands(ranges, this.levels.subarray(0, n));
      const now = performance.now();
      for (let i = 0; i < n; i++) {
        if (lv[i] >= this.peaks[i]) { this.peaks[i] = lv[i]; this.peakHold[i] = now + 600; }
        else if (now > this.peakHold[i]) this.peaks[i] = Math.max(0, this.peaks[i] - 0.008);
      }
      if (this.curveDirty) { this.curve = this.engine.response(this.curveFreqs); this.curveDirty = false; }

      c.clearRect(0, 0, w, h);
      const top = this.gy(DB), bottom = this.gy(-DB);

      // rejilla
      c.lineWidth = 1;
      c.font = `${10 * dpr}px Consolas, monospace`;
      c.textBaseline = 'middle';
      for (let db = -DB; db <= DB; db += 3) {
        const y = Math.round(this.gy(db)) + 0.5;
        c.strokeStyle = db === 0 ? 'rgba(255,255,255,0.28)' : 'rgba(255,255,255,0.07)';
        c.beginPath(); c.moveTo(this.fx(F_MIN), y); c.lineTo(this.fx(F_MAX), y); c.stroke();
        if (db % 6 === 0) { c.fillStyle = 'rgba(255,255,255,0.45)'; c.textAlign = 'right'; c.fillText((db > 0 ? '+' : '') + db, this.fx(F_MIN) - 8 * dpr, y); }
      }
      c.textAlign = 'center';
      for (const f of [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000]) {
        const x = Math.round(this.fx(f)) + 0.5;
        c.strokeStyle = 'rgba(255,255,255,0.06)';
        c.beginPath(); c.moveTo(x, top); c.lineTo(x, bottom); c.stroke();
        c.fillStyle = 'rgba(255,255,255,0.45)';
        c.fillText(f >= 1000 ? f / 1000 + 'k' : f, x, h - 11 * dpr);
      }

      // barras LED del analizador
      const segH = 4 * dpr, segGap = 2 * dpr;
      const avail = bottom - top;
      const segs = Math.floor(avail / (segH + segGap));
      for (let i = 0; i < n; i++) {
        const x0 = this.fx(ranges[i][0]), x1 = this.fx(ranges[i][1]);
        const bw = Math.max(2, (x1 - x0) * 0.72), bx = (x0 + x1) / 2 - bw / 2;
        const lit = Math.round(lv[i] * segs);
        for (let s = 0; s < segs; s++) {
          const y = bottom - (s + 1) * (segH + segGap);
          const k = s / segs;
          if (s < lit) {
            c.fillStyle = k > 0.85 ? '#ff2a2a' : k > 0.62 ? '#ffd000' : this.color2;
            c.globalAlpha = 0.85;
          } else { c.fillStyle = '#ffffff'; c.globalAlpha = 0.035; }
          c.fillRect(bx, y, bw, segH);
        }
        const ps = Math.round(this.peaks[i] * segs);
        if (ps > 0) { c.globalAlpha = 1; c.fillStyle = '#fff'; c.fillRect(bx, bottom - ps * (segH + segGap), bw, segH * 0.7); }
      }
      c.globalAlpha = 1;

      // curva de respuesta
      if (this.curve) {
        const pts = [];
        for (let i = 0; i < this.curveFreqs.length; i++) pts.push([this.fx(this.curveFreqs[i]), this.gy(Math.max(-DB * 1.1, Math.min(DB * 1.1, this.curve[i])))]);
        const zero = this.gy(0);
        const grad = c.createLinearGradient(0, top, 0, bottom);
        grad.addColorStop(0, this.color + '66'); grad.addColorStop(0.5, this.color + '14'); grad.addColorStop(1, this.color + '66');
        c.beginPath(); c.moveTo(pts[0][0], zero);
        pts.forEach(p => c.lineTo(p[0], p[1]));
        c.lineTo(pts[pts.length - 1][0], zero); c.closePath();
        c.fillStyle = grad; c.fill();
        c.beginPath(); pts.forEach((p, i) => i ? c.lineTo(p[0], p[1]) : c.moveTo(p[0], p[1]));
        c.shadowColor = this.color; c.shadowBlur = 14 * dpr;
        c.strokeStyle = this.color; c.lineWidth = 2.5 * dpr; c.stroke();
        c.shadowBlur = 0;
        c.strokeStyle = 'rgba(255,255,255,0.85)'; c.lineWidth = 1 * dpr; c.stroke();
      }

      // nodos de banda
      for (let i = 0; i < n; i++) {
        const x = this.fx(this.freqs[i]), y = this.gy(this.gains[i]);
        c.beginPath(); c.arc(x, y, (n === 10 ? 5 : 3.2) * dpr, 0, Math.PI * 2);
        c.fillStyle = this.gains[i] === 0 ? 'rgba(255,255,255,0.35)' : '#fff';
        c.fill();
      }

      // cursor
      if (this.hover) {
        const r = this.canvas.getBoundingClientRect();
        const x = (this.hover.clientX - r.left) * dpr, y = (this.hover.clientY - r.top) * dpr;
        const f = this.xf(x), db = this.yg(y);
        if (f > F_MIN && f < F_MAX && db >= -DB && db <= DB) {
          c.strokeStyle = 'rgba(255,255,255,0.25)';
          c.beginPath(); c.moveTo(x + 0.5, top); c.lineTo(x + 0.5, bottom); c.stroke();
          c.fillStyle = '#fff'; c.textAlign = 'left';
          c.fillText(`${f >= 1000 ? (f / 1000).toFixed(2) + ' kHz' : Math.round(f) + ' Hz'}  ${db > 0 ? '+' : ''}${db.toFixed(1)} dB`, x + 8 * dpr, top + 4 * dpr);
        }
      }
    }
  }

  window.NDEQ = { GraphicEQ };
})();
