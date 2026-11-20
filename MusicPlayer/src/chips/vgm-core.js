// Reproductor de archivos VGM (Mega Drive / Genesis: YM2612 + SN76489).
// Independiente del entorno: lo usan el AudioWorklet y las pruebas en Node.
import { YM2612 } from './ym2612.js';
import { SN76489 } from './sn76489.js';

const VGM_RATE = 44100;
export const MAX_LOOPS = 2;
export const FADE_SECONDS = 8;
const FM_GAIN = 0.2 / 8192;
const PSG_GAIN = 0.11;

function u32(d, o) { return (d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24)) >>> 0; }

export function parseHeader(d) {
  if (d.length < 0x40 || u32(d, 0) !== 0x206D6756) throw new Error('No es un archivo VGM');
  const version = u32(d, 0x08);
  const h = {
    version,
    snClock: u32(d, 0x0C) & 0x3FFFFFFF,
    gd3: u32(d, 0x14) ? 0x14 + u32(d, 0x14) : 0,
    totalSamples: u32(d, 0x18),
    loopOffset: u32(d, 0x1C) ? 0x1C + u32(d, 0x1C) : 0,
    loopSamples: u32(d, 0x20),
    ymClock: version >= 0x110 ? u32(d, 0x2C) & 0x3FFFFFFF : u32(d, 0x10) & 0x3FFFFFFF,
    dataOffset: version >= 0x150 && u32(d, 0x34) ? 0x34 + u32(d, 0x34) : 0x40
  };
  h.hasLoop = !!(h.loopOffset && h.loopSamples);
  h.duration = (h.totalSamples + (h.hasLoop ? h.loopSamples * (MAX_LOOPS - 1) : 0)) / VGM_RATE + (h.hasLoop ? FADE_SECONDS : 0);
  return h;
}

export function parseGD3(d, h) {
  const out = {};
  if (!h.gd3 || h.gd3 + 12 > d.length || u32(d, h.gd3) !== 0x20336447) return out;
  const len = u32(d, h.gd3 + 8);
  let p = h.gd3 + 12;
  const end = Math.min(d.length, p + len);
  const strs = [];
  while (p < end && strs.length < 11) {
    let s = '';
    while (p + 1 < end) {
      const c = d[p] | (d[p + 1] << 8);
      p += 2;
      if (!c) break;
      s += String.fromCharCode(c);
    }
    strs.push(s.trim());
  }
  const [track, trackJ, game, gameJ, system, systemJ, author, authorJ, date, ripper, notes] = strs;
  return { track: track || trackJ || '', game: game || gameJ || '', system: system || systemJ || '', author: author || authorJ || '', date: date || '', ripper: ripper || '', notes: notes || '' };
}

export class VgmPlayer {
  constructor(outRate) {
    this.outRate = outRate;
    this.loaded = false;
  }

  load(bytes) {
    this.d = bytes;
    this.h = parseHeader(bytes);
    this.ym = new YM2612(this.h.ymClock || 7670453);
    this.psg = new SN76489(this.h.snClock || 3579545);
    this.ymStep = this.ym.rate / this.outRate;        // ticks del YM por muestra de salida
    this.vgmPerTick = VGM_RATE / this.ym.rate;        // muestras VGM por tick del YM
    this.psgPerTick = this.psg.rate / this.ym.rate;
    this.loaded = true;
    this.restart();
  }

  restart() {
    this.ym.reset(); this.psg.reset();
    this.pos = this.h.dataOffset;
    this.wait = 0;
    this.played = 0;            // muestras VGM reproducidas (incluye bucles)
    this.loops = 0;
    this.fading = false; this.fadePos = 0;
    this.ended = false;
    this.pcm = []; this.pcmBank = new Uint8Array(0); this.pcmPos = 0;
    this.banks = {};            // tipo -> { data, blocks: [{start,len}] }
    this.streams = {};
    this.frac = 0;
    this.prevL = 0; this.prevR = 0; this.curL = 0; this.curR = 0;
    this._seenBlocks = new Set();
    this._tail = 0;
    this.dcXL = this.dcXR = this.dcYL = this.dcYR = 0;
  }

  get time() { return this.played / VGM_RATE; }

  _bank(type) {
    let b = this.banks[type];
    if (!b) b = this.banks[type] = { chunks: [], data: new Uint8Array(0), blocks: [], size: 0 };
    return b;
  }

  _addBlock(type, src, off, len) {
    const b = this._bank(type);
    const nd = new Uint8Array(b.size + len);
    nd.set(b.data, 0);
    nd.set(src.subarray(off, off + len), b.size);
    b.blocks.push({ start: b.size, len });
    b.data = nd; b.size += len;
    if (type === 0) this.pcmBank = b.data;
  }

  // Ejecuta comandos hasta que haya una espera. `fast` = sin DAC (para buscar)
  _commands(fast) {
    const d = this.d;
    let guard = 0;
    while (this.wait <= 0 && !this.ended) {
      if (++guard > 200000) { this.ended = true; return; }
      if (this.pos >= d.length) { this._end(); continue; }
      const c = d[this.pos];
      if (c >= 0x70 && c <= 0x7F) { this.wait += (c & 15) + 1; this.pos++; continue; }
      if (c >= 0x80 && c <= 0x8F) {
        if (!fast) this.ym.write(0, 0x2A, this.pcmBank[this.pcmPos] || 0x80);
        this.pcmPos++;
        this.wait += c & 15; this.pos++; continue;
      }
      switch (c) {
        case 0x50: this.psg.write(d[this.pos + 1]); this.pos += 2; break;
        case 0x52: this.ym.write(0, d[this.pos + 1], d[this.pos + 2]); this.pos += 3; break;
        case 0x53: this.ym.write(1, d[this.pos + 1], d[this.pos + 2]); this.pos += 3; break;
        case 0x61: this.wait += d[this.pos + 1] | (d[this.pos + 2] << 8); this.pos += 3; break;
        case 0x62: this.wait += 735; this.pos++; break;
        case 0x63: this.wait += 882; this.pos++; break;
        case 0x66: this._end(); break;
        case 0x67: {
          const type = d[this.pos + 2];
          const size = u32(d, this.pos + 3) & 0x7FFFFFFF;
          if (!this._seenBlocks.has(this.pos)) { this._seenBlocks.add(this.pos); this._addBlock(type, d, this.pos + 7, size); }
          this.pos += 7 + size;
          break;
        }
        case 0x68: this.pos += 12; break;
        case 0xE0: this.pcmPos = u32(d, this.pos + 1); this.pos += 5; break;
        case 0x90: { const s = this._stream(d[this.pos + 1]); s.chip = d[this.pos + 2]; s.port = d[this.pos + 3]; s.reg = d[this.pos + 4]; this.pos += 5; break; }
        case 0x91: { const s = this._stream(d[this.pos + 1]); s.bank = d[this.pos + 2]; s.stepSize = d[this.pos + 3] || 1; s.stepBase = d[this.pos + 4]; this.pos += 5; break; }
        case 0x92: { const s = this._stream(d[this.pos + 1]); s.freq = u32(d, this.pos + 2); this.pos += 6; break; }
        case 0x93: {
          const s = this._stream(d[this.pos + 1]);
          const start = u32(d, this.pos + 2), mode = d[this.pos + 6], len = u32(d, this.pos + 7);
          if (start !== 0xFFFFFFFF) s.ptr = start;
          s.loop = !!(mode & 0x80);
          const m = mode & 3;
          s.left = m === 1 ? len : m === 2 ? Math.round(len * s.freq / 1000) : m === 3 ? Infinity : s.left;
          s.start = s.ptr; s.total = s.left;
          s.active = true; s.frac = 0;
          this.pos += 11;
          break;
        }
        case 0x94: {
          const id = d[this.pos + 1];
          if (id === 0xFF) for (const k in this.streams) this.streams[k].active = false;
          else if (this.streams[id]) this.streams[id].active = false;
          this.pos += 2;
          break;
        }
        case 0x95: {
          const s = this._stream(d[this.pos + 1]);
          const blk = d[this.pos + 2] | (d[this.pos + 3] << 8), flags = d[this.pos + 4];
          const b = this.banks[s.bank];
          if (b && b.blocks[blk]) {
            s.ptr = b.blocks[blk].start; s.left = b.blocks[blk].len;
            s.start = s.ptr; s.total = s.left;
            s.loop = !!(flags & 1); s.active = true; s.frac = 0;
          }
          this.pos += 5;
          break;
        }
        default:
          if (c >= 0x30 && c <= 0x3F) this.pos += 2;
          else if ((c >= 0x40 && c <= 0x4E) || (c >= 0x51 && c <= 0x5F) || (c >= 0xA0 && c <= 0xBF)) this.pos += 3;
          else if (c === 0x4F) this.pos += 2;
          else if (c >= 0xC0 && c <= 0xDF) this.pos += 4;
          else if (c >= 0xE1) this.pos += 5;
          else this.pos += 1;
      }
    }
  }

  _stream(id) {
    return this.streams[id] || (this.streams[id] = { chip: 2, port: 0, reg: 0x2A, bank: 0, stepSize: 1, stepBase: 0, freq: 0, ptr: 0, left: 0, active: false, loop: false, frac: 0 });
  }

  _end() {
    if (this.h.hasLoop) {
      this.loops++;
      this.pos = this.h.loopOffset;
      if (this.loops >= MAX_LOOPS && !this.fading) { this.fading = true; this.fadePos = 0; }
    } else this.ended = true;
  }

  _runStreams() {
    for (const k in this.streams) {
      const s = this.streams[k];
      if (!s.active || !s.freq) continue;
      s.frac += s.freq / this.ym.rate;
      while (s.frac >= 1 && s.active) {
        s.frac -= 1;
        const b = this.banks[s.bank];
        if (!b || s.left <= 0) {
          if (s.loop && b) { s.ptr = s.start; s.left = s.total; } else { s.active = false; break; }
        }
        const v = b.data[s.ptr + s.stepBase];
        s.ptr += s.stepSize; s.left--;
        if (v === undefined) { s.active = false; break; }
        if ((s.chip & 0x7F) === 0x02) this.ym.write(s.port & 1, s.reg, v);
        else if ((s.chip & 0x7F) === 0x00) this.psg.write(v);
      }
    }
  }

  _tick() {
    if (!this.ended) {
      this._commands(false);
      this.wait -= this.vgmPerTick;
      this.played += this.vgmPerTick;
    }
    this._runStreams();
    this.ym.tick();
    const p = this.psg.run(this.psgPerTick) * PSG_GAIN;
    this.prevL = this.curL; this.prevR = this.curR;
    this.curL = this.ym.outL * FM_GAIN + p;
    this.curR = this.ym.outR * FM_GAIN + p;
  }

  // Rellena dos canales Float32Array. Devuelve false cuando termina.
  render(L, R) {
    if (!this.loaded) return false;
    const n = L.length;
    const fadeLen = FADE_SECONDS * this.outRate;
    for (let i = 0; i < n; i++) {
      this.frac += this.ymStep;
      while (this.frac >= 1) { this.frac -= 1; this._tick(); }
      const t = this.frac;
      let l = this.prevL + (this.curL - this.prevL) * t;
      let r = this.prevR + (this.curR - this.prevR) * t;
      if (this.fading) {
        const g = 1 - this.fadePos / fadeLen;
        if (g <= 0) { this.ended = true; l = r = 0; }
        else { l *= g * g; r *= g * g; this.fadePos++; }
      }
      // filtro anti-continua
      const yl = l - this.dcXL + 0.9995 * this.dcYL, yr = r - this.dcXR + 0.9995 * this.dcYR;
      this.dcXL = l; this.dcXR = r; this.dcYL = yl; this.dcYR = yr;
      L[i] = yl; R[i] = yr;
    }
    return !(this.ended && (this.fading || this._silent()));
  }

  _silent() {
    return ++this._tail > 40; // ~0.1 s de cola tras el final
  }

  // Busca saltando comandos sin generar audio
  seek(seconds) {
    const target = Math.max(0, seconds) * VGM_RATE;
    this.restart(); // los bancos de datos se reconstruyen al pasar por ellos
    while (this.played < target && !this.ended) {
      this._commands(true);
      const step = Math.min(this.wait, target - this.played);
      this.wait -= step; this.played += step;
      if (step <= 0 && this.wait > 0) break;
    }
    if (this.fading) this.fadePos = Math.min(FADE_SECONDS * this.outRate, Math.max(0, (this.played - (this.h.totalSamples + this.h.loopSamples * (MAX_LOOPS - 1))) / VGM_RATE * this.outRate));
  }
}
