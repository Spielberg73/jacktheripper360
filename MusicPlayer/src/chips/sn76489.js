// Emulación del PSG SN76489 (variante Sega): 3 canales de onda cuadrada + ruido.

const VOL = new Float32Array(16);
for (let i = 0; i < 15; i++) VOL[i] = Math.pow(10, -2 * i / 20);
VOL[15] = 0;

export class SN76489 {
  constructor(clock = 3579545) {
    this.clock = clock;
    this.rate = clock / 16;
    this.reset();
  }

  reset() {
    this.vol = [15, 15, 15, 15];
    this.period = [0, 0, 0, 0];
    this.cnt = [1, 1, 1, 1];
    this.pol = [1, 1, 1, 1];
    this.noise = 0;
    this.lfsr = 0x8000;
    this.noiseFF = 0;
    this.latch = 0; this.latchVol = 0;
    this.frac = 0;
    this.out = 0;
    this.levels = new Float32Array(4);
    this.chOut = new Float32Array(4);
  }

  write(b) {
    b &= 0xFF;
    if (b & 0x80) {
      this.latch = (b >> 5) & 3;
      this.latchVol = (b >> 4) & 1;
      const d = b & 15;
      if (this.latchVol) this.vol[this.latch] = d;
      else if (this.latch < 3) this.period[this.latch] = (this.period[this.latch] & 0x3F0) | d;
      else { this.noise = d & 7; this.lfsr = 0x8000; }
    } else {
      if (this.latchVol) this.vol[this.latch] = b & 15;
      else if (this.latch < 3) this.period[this.latch] = (this.period[this.latch] & 0xF) | ((b & 0x3F) << 4);
      else { this.noise = b & 7; this.lfsr = 0x8000; }
    }
  }

  _step(acc) {
    for (let i = 0; i < 3; i++) {
      if (--this.cnt[i] <= 0) { this.cnt[i] = this.period[i] || 1; this.pol[i] = -this.pol[i]; }
      const v = (this.period[i] <= 1 ? 1 : this.pol[i]) * VOL[this.vol[i]];
      acc[i] += v;
    }
    if (--this.cnt[3] <= 0) {
      const r = this.noise & 3;
      this.cnt[3] = r === 3 ? (this.period[2] || 1) : (0x10 << r);
      this.noiseFF ^= 1;
      if (this.noiseFF) {
        const fb = (this.noise & 4) ? ((this.lfsr ^ (this.lfsr >> 3)) & 1) : (this.lfsr & 1);
        this.lfsr = (this.lfsr >> 1) | (fb << 15);
      }
    }
    acc[3] += ((this.lfsr & 1) ? 1 : -1) * VOL[this.vol[3]];
  }

  // Avanza el equivalente a `ticks` ciclos (/16) y devuelve la salida media (-4..4 aprox).
  run(ticks) {
    this.frac += ticks;
    let n = Math.floor(this.frac);
    this.frac -= n;
    const acc = this.chOut;
    if (n > 0) {
      acc[0] = acc[1] = acc[2] = acc[3] = 0;
      for (let k = 0; k < n; k++) this._step(acc);
      for (let i = 0; i < 4; i++) acc[i] /= n;
    }
    let s = 0;
    for (let i = 0; i < 4; i++) {
      s += acc[i];
      const a = Math.abs(acc[i]);
      if (a > this.levels[i]) this.levels[i] = a;
    }
    this.out = s;
    return s;
  }
}
