// NightDrive Player — motor de audio (Web Audio API)
(function () {
  const ISO31 = [20, 25, 31.5, 40, 50, 63, 80, 100, 125, 160, 200, 250, 315, 400, 500, 630, 800, 1000,
    1250, 1600, 2000, 2500, 3150, 4000, 5000, 6300, 8000, 10000, 12500, 16000, 20000];
  const ISO10 = [31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
  const LOUDNESS = [[0, 0], [4, 2], [7, 4], [10, 6]]; // [graves, agudos] dB por nivel OFF/LOW/MID/HIGH

  // Presets definidos como puntos de control (Hz, dB) => sirven para 10 o 31 bandas
  const PRESETS = {
    'FLAT': [],
    'POWERFUL': [[20, 6], [60, 7], [150, 3], [400, -1], [1000, 0], [3000, 2], [8000, 4], [16000, 5]],
    'NATURAL': [[20, 2], [100, 1], [1000, 0], [4000, 1], [12000, 2]],
    'VOCAL': [[20, -3], [100, -2], [300, 1], [1000, 4], [3000, 4.5], [6000, 2], [12000, -1]],
    'SUPER BASS': [[20, 10], [50, 11], [100, 8], [200, 3], [400, 0], [16000, 0]],
    'ROCK': [[20, 5], [80, 4], [250, -1], [800, -2], [2500, 2], [6000, 4], [16000, 5]],
    'CLUB': [[20, 8], [63, 9], [160, 4], [500, -2], [2000, 1], [8000, 5], [16000, 6]],
    'V-SHAPE': [[20, 8], [100, 5], [500, -2], [1000, -3], [2000, -1], [6000, 5], [16000, 8]],
    'TREBLE': [[20, 0], [1000, 0], [4000, 4], [10000, 7], [20000, 8]],
    'BRUTAL': [[20, 12], [40, 12], [80, 9], [160, 2], [500, -4], [1500, -2], [4000, 6], [10000, 10], [20000, 12]]
  };

  function interpPoints(points, f) {
    if (!points.length) return 0;
    if (f <= points[0][0]) return points[0][1];
    const last = points[points.length - 1];
    if (f >= last[0]) return last[1];
    for (let i = 0; i < points.length - 1; i++) {
      const [f0, g0] = points[i], [f1, g1] = points[i + 1];
      if (f >= f0 && f <= f1) {
        const t = (Math.log(f) - Math.log(f0)) / (Math.log(f1) - Math.log(f0));
        return g0 + (g1 - g0) * t;
      }
    }
    return 0;
  }

  class AudioEngine {
    constructor() {
      const AC = window.AudioContext || window.webkitAudioContext;
      this.ctx = new AC({ latencyHint: 'playback' });
      const ctx = this.ctx;
      this.el = new Audio();
      this.el.preload = 'auto';
      this.source = ctx.createMediaElementSource(this.el);

      this.preamp = ctx.createGain();
      this.bassBoost = this._shelf('lowshelf', 90);
      this.loudLow = this._shelf('lowshelf', 60);
      this.loudHigh = this._shelf('highshelf', 10000);
      this.panner = ctx.createStereoPanner();
      this.limiter = ctx.createDynamicsCompressor();
      this.limiter.threshold.value = -1.5;
      this.limiter.knee.value = 0;
      this.limiter.ratio.value = 20;
      this.limiter.attack.value = 0.002;
      this.limiter.release.value = 0.12;
      this.master = ctx.createGain();

      this.analyser = ctx.createAnalyser();
      this.analyser.fftSize = 8192;
      this.analyser.smoothingTimeConstant = 0.55;
      this.analyser.minDecibels = -100;
      this.analyser.maxDecibels = -10;
      this.splitter = ctx.createChannelSplitter(2);
      this.anL = ctx.createAnalyser(); this.anL.fftSize = 2048;
      this.anR = ctx.createAnalyser(); this.anR.fftSize = 2048;

      this.source.connect(this.preamp);
      this.bassBoost.connect(this.loudLow);
      this.loudLow.connect(this.loudHigh);
      this.loudHigh.connect(this.panner);
      this.panner.connect(this.limiter);
      this.limiter.connect(this.master);
      this.master.connect(ctx.destination);
      this.limiter.connect(this.analyser);
      this.limiter.connect(this.splitter);
      this.splitter.connect(this.anL, 0);
      this.splitter.connect(this.anR, 1);

      this.freqData = new Float32Array(this.analyser.frequencyBinCount);
      this.timeL = new Float32Array(this.anL.fftSize);
      this.timeR = new Float32Array(this.anR.fftSize);
      this.bands = [];
      this.freqs = [];
      this.preampDb = 0;
      this.setBandCount(31);
      this.setVolume(20);
      this.media = new MediaSwitch(this);
    }

    _shelf(type, f) {
      const b = this.ctx.createBiquadFilter();
      b.type = type; b.frequency.value = f; b.gain.value = 0;
      return b;
    }

    resume() { if (this.ctx.state !== 'running') return this.ctx.resume(); }

    setBandCount(n, gains) {
      const ctx = this.ctx;
      this.preamp.disconnect();
      this.bands.forEach(b => b.disconnect());
      this.freqs = n === 10 ? ISO10 : ISO31;
      const q = n === 10 ? 1.41 : 4.32;
      const nyq = ctx.sampleRate / 2;
      this.bands = this.freqs.map((f, i) => {
        const b = ctx.createBiquadFilter();
        b.type = 'peaking';
        b.frequency.value = Math.min(f, nyq * 0.95);
        b.Q.value = q;
        b.gain.value = gains ? gains[i] || 0 : 0;
        return b;
      });
      let node = this.preamp;
      for (const b of this.bands) { node.connect(b); node = b; }
      node.connect(this.bassBoost);
    }

    setBandGain(i, db) {
      const b = this.bands[i];
      if (b) b.gain.setTargetAtTime(db, this.ctx.currentTime, 0.015);
    }
    setPreamp(db) { this.preampDb = db; this.preamp.gain.setTargetAtTime(Math.pow(10, db / 20), this.ctx.currentTime, 0.02); }
    setBassBoost(db) { this.bassBoost.gain.setTargetAtTime(db, this.ctx.currentTime, 0.02); }
    setLoudness(level) {
      const [lo, hi] = LOUDNESS[level] || LOUDNESS[0];
      this.loudLow.gain.setTargetAtTime(lo, this.ctx.currentTime, 0.02);
      this.loudHigh.gain.setTargetAtTime(hi, this.ctx.currentTime, 0.02);
    }
    setBalance(v) { this.panner.pan.setTargetAtTime(v, this.ctx.currentTime, 0.02); }
    setVolume(step) { // 0..40 como una autorradio
      this.volumeStep = step;
      const g = step <= 0 ? 0 : Math.pow(step / 40, 2.2);
      this.master.gain.setTargetAtTime(g, this.ctx.currentTime, 0.03);
    }

    // Respuesta total de la cadena de EQ (dB) para un array de frecuencias
    response(freqs) {
      const n = freqs.length;
      const total = new Float32Array(n).fill(this.preampDb);
      const mag = new Float32Array(n), ph = new Float32Array(n);
      const filters = [...this.bands, this.bassBoost, this.loudLow, this.loudHigh];
      for (const f of filters) {
        f.getFrequencyResponse(freqs, mag, ph);
        for (let i = 0; i < n; i++) total[i] += 20 * Math.log10(Math.max(mag[i], 1e-6));
      }
      return total;
    }

    // Toma el espectro y devuelve un nivel (0..1) por cada rango [fLo, fHi]
    spectrumBands(ranges, out) {
      this.analyser.getFloatFrequencyData(this.freqData);
      const binHz = this.ctx.sampleRate / this.analyser.fftSize;
      const min = this.analyser.minDecibels, span = this.analyser.maxDecibels - min;
      const n = this.freqData.length;
      for (let i = 0; i < ranges.length; i++) {
        const lo = Math.max(1, Math.floor(ranges[i][0] / binHz));
        const hi = Math.min(n - 1, Math.max(lo, Math.ceil(ranges[i][1] / binHz)));
        let m = -Infinity;
        for (let k = lo; k <= hi; k++) if (this.freqData[k] > m) m = this.freqData[k];
        // ligera compensación de pendiente para que los agudos se vean
        const tilt = Math.log2(Math.max(ranges[i][0], 20) / 1000) * 1.5;
        out[i] = Math.max(0, Math.min(1, (m + tilt - min) / span));
      }
      return out;
    }

    levels() {
      this.anL.getFloatTimeDomainData(this.timeL);
      this.anR.getFloatTimeDomainData(this.timeR);
      const calc = d => {
        let sum = 0, peak = 0;
        for (let i = 0; i < d.length; i++) { const v = d[i]; sum += v * v; const a = Math.abs(v); if (a > peak) peak = a; }
        return { rms: Math.sqrt(sum / d.length), peak };
      };
      return { L: calc(this.timeL), R: calc(this.timeR) };
    }
  }

  // Fachada que hace que <audio> y el reproductor VGM se comporten como un solo medio
  const EVENTS = ['play', 'pause', 'playing', 'ended', 'loadedmetadata', 'error'];
  class MediaSwitch extends EventTarget {
    constructor(engine) {
      super();
      this.engine = engine;
      this.html = engine.el;
      this.vgm = null;
      this.active = this.html;
      this._forward(this.html);
    }
    _forward(b) { EVENTS.forEach(ev => b.addEventListener(ev, () => { if (this.active === b) this.dispatchEvent(new Event(ev)); })); }
    _vgm() {
      if (!this.vgm) { this.vgm = new window.NDVgm.VgmMedia(this.engine.ctx, this.engine.preamp); this._forward(this.vgm); }
      return this.vgm;
    }
    get isVgm() { return this.active === this.vgm; }
    get paused() { return this.active.paused; }
    get duration() { return this.active.duration; }
    get currentTime() { return this.active.currentTime; }
    set currentTime(v) { this.active.currentTime = v; }
    get src() { return this.active.src; }
    get levels() { return this.isVgm ? this.vgm.levels : null; }
    play() { return this.active.play(); }
    pause() { return this.active.pause(); }
    loadUrl(url) {
      if (this.vgm) { const v = this.vgm; this.active = this.html; v.unload(); }
      this.active = this.html;
      this.html.src = url;
    }
    async loadVgm(buf) {
      const v = this._vgm();
      const wasHtml = this.active === this.html;
      this.active = v;
      if (wasHtml) { this.html.pause(); this.html.removeAttribute('src'); this.html.load(); }
      await v.load(buf);
    }
  }

  window.NDAudio = { AudioEngine, MediaSwitch, PRESETS, interpPoints, ISO31, ISO10 };
})();
