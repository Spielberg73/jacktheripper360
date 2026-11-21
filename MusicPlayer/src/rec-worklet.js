// Captura el audio que suena (antes del EQ) en bloques para grabarlo
class RecTap extends AudioWorkletProcessor {
  constructor() {
    super();
    this.on = false;
    this.size = 8192;
    this.L = new Float32Array(this.size); this.R = new Float32Array(this.size); this.n = 0;
    this.port.onmessage = e => {
      if (e.data.type === 'on') { this.on = true; this.n = 0; }
      else if (e.data.type === 'off') { this.flush(); this.on = false; this.port.postMessage({ type: 'flushed' }); }
    };
  }
  flush() {
    if (!this.n) return;
    const l = this.L.slice(0, this.n), r = this.R.slice(0, this.n);
    this.port.postMessage({ type: 'pcm', l, r }, [l.buffer, r.buffer]);
    this.n = 0;
  }
  process(inputs) {
    if (!this.on) return true;
    const inp = inputs[0];
    if (!inp || !inp.length) return true;
    const a = inp[0], b = inp[1] || inp[0];
    for (let i = 0; i < a.length; i++) {
      this.L[this.n] = a[i]; this.R[this.n] = b[i];
      if (++this.n === this.size) this.flush();
    }
    return true;
  }
}
registerProcessor('rec-tap', RecTap);
