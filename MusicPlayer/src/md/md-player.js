// Reproduce música de una ROM de Mega Drive emulando la consola (para el AudioWorklet)
import { MegaDrive, parseRom } from './megadrive.js';
import { applyTemplate } from './scan.js';

const FM_GAIN = 0.2 / 8192, PSG_GAIN = 0.11, FADE = 8;

export class MdPlayer {
  constructor(outRate) { this.outRate = outRate; this.loaded = false; }

  // cfg: { rom, snapshot?, template?, value?, duration, jingle }
  load(cfg) {
    this.cfg = cfg;
    this.info = parseRom(cfg.rom);
    this.md = new MegaDrive(this.info);
    this.ym = this.md.ym; this.psg = this.md.psg;
    this.h = { duration: cfg.duration };
    this.ymStep = this.md.ym.rate / this.outRate;
    this.loaded = true;
    this.restart();
  }

  restart() {
    const md = this.md, c = this.cfg;
    if (c.snapshot) {
      md.load(c.snapshot);
      md.frozen68k = true;
      md.z80BusReq = false;
      applyTemplate(md, c.template, c.value);
      // el arranque se hizo sin síntesis: dejamos el FM en silencio limpio
      for (const ch of md.ym.ch) for (const o of ch.ops) { o.state = 0; o.vol = 1023; o.key = 0; }
    } else {
      md.powerOn();
      md.frozen68k = false;
    }
    md.fast = false;
    this.samples = 0; this.frac = 0;
    this.prevL = this.prevR = this.curL = this.curR = 0;
    this.dcXL = this.dcXR = this.dcYL = this.dcYR = 0;
    this.ended = false; this.heard = false; this.quiet = 0;
  }

  get time() { return this.samples / this.md.ym.rate; }

  _tick() {
    const md = this.md;
    md.tick();
    this.samples++;
    const p = md.psg.out * PSG_GAIN;
    this.prevL = this.curL; this.prevR = this.curR;
    this.curL = md.ym.outL * FM_GAIN + p;
    this.curR = md.ym.outR * FM_GAIN + p;
    // detección de silencio (jingles y canciones que terminan)
    const a = Math.abs(this.curL) + Math.abs(this.curR);
    if (a > 0.002) { this.heard = true; this.quiet = 0; } else this.quiet++;
  }

  render(L, R) {
    if (!this.loaded) return false;
    const dur = this.h.duration, rate = this.md.ym.rate;
    for (let i = 0; i < L.length; i++) {
      this.frac += this.ymStep;
      while (this.frac >= 1) { this.frac -= 1; this._tick(); }
      let l = this.prevL + (this.curL - this.prevL) * this.frac;
      let r = this.prevR + (this.curR - this.prevR) * this.frac;
      const t = this.samples / rate;
      if (!this.cfg.jingle && t > dur - FADE) { const g = Math.max(0, (dur - t) / FADE); l *= g * g; r *= g * g; }
      const yl = l - this.dcXL + 0.9995 * this.dcYL, yr = r - this.dcXR + 0.9995 * this.dcYR;
      this.dcXL = l; this.dcXR = r; this.dcYL = yl; this.dcYR = yr;
      L[i] = yl; R[i] = yr;
    }
    const t = this.samples / rate;
    if (t >= dur) return false;
    if (this.heard && this.quiet > rate * 2.5 && t > 3) return false; // la canción ha terminado
    return true;
  }

  seek(seconds) {
    const target = Math.max(0, seconds) * this.md.ym.rate;
    if (target < this.samples) this.restart();
    this.md.fast = true;
    const settle = Math.max(this.samples, target - this.md.ym.rate * 0.3);
    while (this.samples < settle) { this.md.tick(); this.samples++; }
    this.md.fast = false;
    while (this.samples < target) this._tick();
  }
}
