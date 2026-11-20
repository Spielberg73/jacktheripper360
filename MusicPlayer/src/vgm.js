// NightDrive Player — soporte VGM/VGZ (música de Mega Drive emulada)
(function () {
  const VGM_RE = /\.(vgm|vgz)$/i;
  let core = null;
  const loadCore = () => core || (core = import('./chips/vgm-core.js'));

  async function decompress(buf) {
    const u8 = buf instanceof Uint8Array ? buf : new Uint8Array(buf);
    if (u8[0] === 0x1F && u8[1] === 0x8B) {
      const ds = new DecompressionStream('gzip');
      const out = await new Response(new Blob([u8]).stream().pipeThrough(ds)).arrayBuffer();
      return new Uint8Array(out);
    }
    return u8;
  }

  // Lee etiquetas GD3 y duración
  async function info(buf) {
    const d = await decompress(buf);
    const { parseHeader, parseGD3 } = await loadCore();
    const h = parseHeader(d);
    const g = parseGD3(d, h);
    return {
      title: g.track || null, artist: g.author || null, album: g.game || null,
      system: g.system || 'Mega Drive', date: g.date, duration: h.duration, loop: h.hasLoop,
      codec: 'VGM · YM2612 + SN76489'
    };
  }

  // Objeto con la misma interfaz básica que un <audio>
  class VgmMedia extends EventTarget {
    constructor(ctx, dest) {
      super();
      this.ctx = ctx;
      this.paused = true;
      this.duration = NaN;
      this.loaded = false;
      this.levels = new Float32Array(10);
      this._t = 0; this._tCtx = 0; this._id = 0;
      this._pending = new Map();
      this.ready = ctx.audioWorklet.addModule('vgm-worklet.js').then(() => {
        this.node = new AudioWorkletNode(ctx, 'vgm-processor', { numberOfInputs: 0, numberOfOutputs: 1, outputChannelCount: [2] });
        this.node.connect(dest);
        this.node.port.onmessage = e => this._onMessage(e.data);
      });
    }

    _onMessage(m) {
      if (m.type === 'pos') {
        this._t = m.t; this._tCtx = this.ctx.currentTime;
        for (let i = 0; i < 10; i++) this.levels[i] = Math.max(m.levels[i], this.levels[i] * 0.6);
      } else if (m.type === 'loaded' || m.type === 'error') {
        const p = this._pending.get(m.id);
        if (p) { this._pending.delete(m.id); m.type === 'loaded' ? p.resolve(m) : p.reject(new Error(m.message)); }
      } else if (m.type === 'ended') {
        this.paused = true;
        this._t = this.duration;
        this.dispatchEvent(new Event('pause'));
        this.dispatchEvent(new Event('ended'));
      }
    }

    get src() { return this.loaded ? 'vgm' : ''; }

    async load(buf) {
      await this.ready;
      const d = await decompress(buf);
      const copy = d.slice().buffer;
      const id = ++this._id;
      this.paused = true;
      this.loaded = false;
      this._t = 0;
      const res = await new Promise((resolve, reject) => {
        this._pending.set(id, { resolve, reject });
        this.node.port.postMessage({ type: 'load', id, data: copy }, [copy]);
      }).catch(err => { this.dispatchEvent(new Event('error')); throw err; });
      if (id !== this._id) return;
      this.loaded = true;
      this.duration = res.duration;
      setTimeout(() => this.dispatchEvent(new Event('loadedmetadata')), 0);
    }

    unload() {
      if (this.node) this.node.port.postMessage({ type: 'unload' });
      if (!this.paused) { this.paused = true; this.dispatchEvent(new Event('pause')); }
      this.loaded = false;
      this.levels.fill(0);
    }

    async play() {
      if (!this.loaded) return;
      if (this.ctx.state !== 'running') await this.ctx.resume();
      if (this._t >= this.duration - 0.05) this.currentTime = 0;
      this.node.port.postMessage({ type: 'play' });
      this._tCtx = this.ctx.currentTime;
      if (this.paused) {
        this.paused = false;
        this.dispatchEvent(new Event('play'));
        this.dispatchEvent(new Event('playing'));
      }
    }

    pause() {
      if (!this.node) return;
      this.node.port.postMessage({ type: 'pause' });
      this._t = this.currentTime;
      if (!this.paused) { this.paused = true; this.dispatchEvent(new Event('pause')); }
    }

    get currentTime() {
      if (this.paused) return this._t;
      return Math.min(this.duration || Infinity, this._t + Math.max(0, this.ctx.currentTime - this._tCtx));
    }
    set currentTime(v) {
      if (!this.node || !this.loaded) return;
      v = Math.max(0, Math.min(v, (this.duration || 0) - 0.05));
      this._t = v; this._tCtx = this.ctx.currentTime;
      this.node.port.postMessage({ type: 'seek', time: v });
    }
  }

  window.NDVgm = { isVgm: name => VGM_RE.test(name || ''), info, decompress, VgmMedia };
})();
