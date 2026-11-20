// Mega Drive / Genesis "sin vídeo": 68000 + Z80 + YM2612 + SN76489 + VDP mínimo.
// Suficiente para ejecutar el juego y su driver de sonido. No dibuja gráficos.
import { M68K } from './m68k.js';
import { Z80 } from './z80.js';
import { YM2612 } from '../chips/ym2612.js';
import { SN76489 } from '../chips/sn76489.js';

const NTSC = { master: 53693175, lines: 262, vblankLine: 224, fps: 60 };
const PAL = { master: 53203424, lines: 313, vblankLine: 224, fps: 50 };

// ---------- ROM ----------
export function parseRom(input) {
  let rom = input instanceof Uint8Array ? input : new Uint8Array(input);
  // formato SMD (cabecera de 512 bytes + bloques intercalados de 16 KB)
  if (rom.length % 16384 === 512 && rom[8] === 0xAA && rom[9] === 0xBB) {
    const body = rom.subarray(512), out = new Uint8Array(body.length);
    for (let b = 0; b < body.length; b += 16384) {
      for (let i = 0; i < 8192; i++) { out[b + i * 2 + 1] = body[b + i]; out[b + i * 2] = body[b + 8192 + i]; }
    }
    rom = out;
  }
  const txt = (a, n) => { let s = ''; for (let i = 0; i < n; i++) { const c = rom[a + i]; s += c >= 32 && c < 127 ? String.fromCharCode(c) : ' '; } return s.replace(/\s+/g, ' ').trim(); };
  const isMD = rom.length >= 0x200 && /SEGA/.test(txt(0x100, 16));
  const overseas = txt(0x150, 48), domestic = txt(0x120, 48);
  const region = txt(0x1F0, 3).toUpperCase();
  const title = (overseas && !/^[\s?]*$/.test(overseas) ? overseas : domestic) || 'Mega Drive ROM';
  return {
    rom, isMD, title: title.replace(/\b([A-Z])([A-Z']+)\b/g, (m, a, b) => a + b.toLowerCase()),
    serial: txt(0x180, 14), region, checksum: (rom[0x18E] << 8) | rom[0x18F],
    sram: txt(0x1B0, 2) === 'RA' ? { start: ((rom[0x1B4] << 24) | (rom[0x1B5] << 16) | (rom[0x1B6] << 8) | rom[0x1B7]) >>> 0, end: ((rom[0x1B8] << 24) | (rom[0x1B9] << 16) | (rom[0x1BA] << 8) | rom[0x1BB]) >>> 0 } : null
  };
}

export function crc32(d) {
  let c, crc = 0xFFFFFFFF;
  const t = crc32.t || (crc32.t = Array.from({ length: 256 }, (_, n) => { c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; }));
  for (let i = 0; i < d.length; i++) crc = t[(crc ^ d[i]) & 0xFF] ^ (crc >>> 8);
  return ((crc ^ 0xFFFFFFFF) >>> 0).toString(16).padStart(8, '0');
}

// ---------- estado serializable genérico ----------
function dump(o) {
  if (ArrayBuffer.isView(o)) return o.slice();
  if (Array.isArray(o)) return o.map(dump);
  if (o && typeof o === 'object') { const r = {}; for (const k of Object.keys(o)) if (typeof o[k] !== 'function') r[k] = dump(o[k]); return r; }
  return o;
}
function restore(t, d) {
  for (const k of Object.keys(d)) {
    const v = d[k], cur = t[k];
    if (ArrayBuffer.isView(cur)) cur.set(v);
    else if (Array.isArray(cur)) { v.forEach((x, i) => { if (x && typeof x === 'object' && cur[i] && typeof cur[i] === 'object') restore(cur[i], x); else cur[i] = x; }); }
    else if (cur && typeof cur === 'object' && v && typeof v === 'object') restore(cur, v);
    else t[k] = v;
  }
}

export class MegaDrive {
  constructor(romInfo, opts = {}) {
    this.info = romInfo;
    this.rom = romInfo.rom;
    const reg = romInfo.region || '';
    this.pal = opts.pal !== undefined ? opts.pal : (/E/.test(reg) && !/[UJ]/.test(reg));
    const t = this.pal ? PAL : NTSC;
    this.timing = t;
    this.version = (this.pal ? 0xC0 : (/U|4|5|A/.test(reg) || !/J/.test(reg) ? 0x80 : 0x00)) | 0x20;
    this.cyc68PerLine = t.master / 7 / (t.lines * t.fps);
    this.ram = new Uint8Array(0x10000);
    this.zram = new Uint8Array(0x2000);
    this.sram = new Uint8Array(0x10000);
    this.m68k = new M68K(this);
    this.z80 = new Z80({ read: a => this.zRead(a), write: (a, v) => this.zWrite(a, v), in: () => 0xFF, out: () => {} });
    this.ym = new YM2612(t.master / 7);
    this.psg = new SN76489(t.master / 15);
    this.z80PerSample = 144 * 7 / 15;
    this.psgPerSample = this.psg.rate / this.ym.rate;
    this.fast = false;      // sin síntesis (para buscar canciones)
    this.frozen68k = false; // 68000 detenido (para tocar canciones sueltas)
    this.onZ80Write = null; // (addr, v, z80EnReset)
    this.onZWriteByZ80 = null;
    this.onYMWrite = null;  // (parte, registro, valor)
    this.powerOn();
  }

  powerOn() {
    this.ram.fill(0); this.zram.fill(0);
    this.z80Reset = true; this.z80BusReq = false; this.z80Bank = 0;
    this.ymAddr = [0, 0];
    this.timerA = { period: 1024, cnt: 0, on: false }; this.timerB = { period: 256 * 16, cnt: 0, on: false };
    this.ymStatus = 0; this.ymCtrl = 0; this.taLo = 0; this.taHi = 0;
    this.vdp = { regs: new Uint8Array(32), pending: false, code: 0, line: 0, lineCyc: 0, vint: false, hint: false, hcnt: 0, inVblank: false, zIntLeft: 0 };
    this.io = { data: new Uint8Array(4), ctrl: new Uint8Array(4), pad: 0 };
    this.sramOn = false;
    this.frame = 0;
    this.z80Frac = 0; this.c68 = 0;
    this.ym.reset(); this.psg.reset(); this.z80.reset();
    this.m68k.reset();
  }

  // ---------- bus del 68000 ----------
  read8(a) {
    if (a < 0x400000) {
      if (this.info.sram && a >= this.info.sram.start && a <= this.info.sram.end && (this.sramOn || a >= this.rom.length)) return this.sram[(a - this.info.sram.start) & 0xFFFF];
      return a < this.rom.length ? this.rom[a] : 0xFF;
    }
    if (a >= 0xE00000) return this.ram[a & 0xFFFF];
    if (a >= 0xA00000 && a < 0xA10000) return this.zArea68(a & 0xFFFF);
    if (a >= 0xA10000 && a < 0xA10020) return this.ioRead(a & 0x1F);
    if ((a & 0xFFFFFE) === 0xA11100) return (a & 1) ? 0 : ((this.z80BusReq && !this.z80Reset) ? 0 : 1);
    if (a >= 0xC00000 && a < 0xC00020) { const w = this.vdpRead(a & 0x1E); return (a & 1) ? w & 0xFF : w >> 8; }
    return 0;
  }
  read16(a) {
    if (a < 0x400000 && a + 1 < this.rom.length && !(this.sramOn && this.info.sram && a >= this.info.sram.start)) return (this.rom[a] << 8) | this.rom[a + 1];
    if (a >= 0xE00000) { const o = a & 0xFFFF; return (this.ram[o] << 8) | this.ram[o + 1]; }
    if (a >= 0xC00000 && a < 0xC00020) return this.vdpRead(a & 0x1E);
    if (a === 0xA11100) return ((this.z80BusReq && !this.z80Reset) ? 0 : 0x100);
    if (a >= 0xA00000 && a < 0xA10000) { const v = this.zArea68(a & 0xFFFF); return (v << 8) | v; }
    return (this.read8(a) << 8) | this.read8(a + 1);
  }
  write8(a, v) {
    if (a >= 0xE00000) { this.ram[a & 0xFFFF] = v; return; }
    if (a >= 0xA00000 && a < 0xA10000) return this.zArea68Write(a & 0xFFFF, v);
    if (a >= 0xA10000 && a < 0xA10020) return this.ioWrite(a & 0x1F, v);
    if ((a & 0xFFFFFE) === 0xA11100) { if (!(a & 1)) this.setBusReq(v & 1); return; }
    if ((a & 0xFFFFFE) === 0xA11200) { if (!(a & 1)) this.setZ80Reset(!(v & 1)); return; }
    if (a === 0xA130F1) { this.sramOn = !!(v & 1); return; }
    if (a >= 0xC00000 && a < 0xC00020) {
      const r = a & 0x1F;
      if (r >= 0x10 && r < 0x18) { if (r & 1) this.psg.write(v); return; }
      return this.vdpWrite(r & 0x1E, (v << 8) | v);
    }
    if (a < 0x400000 && this.info.sram && a >= this.info.sram.start && a <= this.info.sram.end) this.sram[(a - this.info.sram.start) & 0xFFFF] = v;
  }
  write16(a, v) {
    if (a >= 0xE00000) { const o = a & 0xFFFF; this.ram[o] = v >> 8; this.ram[o + 1] = v & 0xFF; return; }
    if (a >= 0xC00000 && a < 0xC00020) {
      const r = a & 0x1F;
      if (r >= 0x10 && r < 0x18) { this.psg.write(v & 0xFF); return; }
      return this.vdpWrite(r & 0x1E, v);
    }
    if (a === 0xA11100) { this.setBusReq((v >> 8) & 1); return; }
    if (a === 0xA11200) { this.setZ80Reset(!((v >> 8) & 1)); return; }
    if (a >= 0xA00000 && a < 0xA10000) return this.zArea68Write(a & 0xFFFF, v >> 8);
    this.write8(a, v >> 8); this.write8(a + 1, v & 0xFF);
  }
  iack(level) {
    if (level === 6) this.vdp.vint = false; else if (level === 4) this.vdp.hint = false;
    this.updateIRQ();
  }

  setBusReq(on) { this.z80BusReq = !!on; }
  setZ80Reset(on) {
    if (on && !this.z80Reset) { this.z80.reset(); this.ym.reset(); this.ymAddr = [0, 0]; }
    this.z80Reset = !!on;
  }

  // zona del Z80 vista desde el 68000
  zArea68(o) {
    if (o < 0x4000) return this.zram[o & 0x1FFF];
    if (o < 0x6000) return this.ymStatus;
    return 0xFF;
  }
  zArea68Write(o, v) {
    if (o < 0x4000) {
      this.zram[o & 0x1FFF] = v;
      if (this.onZ80Write) this.onZ80Write(o & 0x1FFF, v, this.z80Reset);
      return;
    }
    if (o < 0x6000) return this.ymPort(o & 3, v, true);
    if (o < 0x6100) return this.bankWrite(v);
    if (o >= 0x7F10 && o < 0x7F18) { if (o & 1) this.psg.write(v); }
  }

  // ---------- bus del Z80 ----------
  zRead(a) {
    if (a < 0x4000) return this.zram[a & 0x1FFF];
    if (a < 0x6000) return this.ymStatus;
    if (a < 0x8000) return 0xFF;
    const addr = (this.z80Bank + (a & 0x7FFF)) & 0xFFFFFF;
    if (addr >= 0xA00000 && addr < 0xA10000) return 0xFF;
    return this.read8(addr);
  }
  zWrite(a, v) {
    if (a < 0x4000) { this.zram[a & 0x1FFF] = v; if (this.onZWriteByZ80) this.onZWriteByZ80(a & 0x1FFF, v); return; }
    if (a < 0x6000) return this.ymPort(a & 3, v, false);
    if (a < 0x6100) return this.bankWrite(v);
    if (a >= 0x7F10 && a < 0x7F18) { if (a & 1) this.psg.write(v); return; }
    if (a >= 0x8000) {
      const addr = (this.z80Bank + (a & 0x7FFF)) & 0xFFFFFF;
      if (addr >= 0xE00000) this.ram[addr & 0xFFFF] = v;
    }
  }
  bankWrite(v) { this.z80Bank = ((this.z80Bank >>> 1) | ((v & 1) << 23)) & 0xFF8000; }

  // ---------- YM2612 (puertos y timers) ----------
  ymPort(p, v, from68k) {
    const part = (p >> 1) & 1;
    if (!(p & 1)) { this.ymAddr[part] = v; return; }
    const r = this.ymAddr[part];
    if (part === 0) {
      if (r === 0x24) { this.taHi = v; this.timerA.period = 1024 - ((this.taHi << 2) | (this.taLo & 3)); }
      else if (r === 0x25) { this.taLo = v; this.timerA.period = 1024 - ((this.taHi << 2) | (this.taLo & 3)); }
      else if (r === 0x26) this.timerB.period = (256 - v) * 16;
      else if (r === 0x27) {
        if ((v & 1) && !this.timerA.on) this.timerA.cnt = this.timerA.period;
        if ((v & 2) && !this.timerB.on) this.timerB.cnt = this.timerB.period;
        this.timerA.on = !!(v & 1); this.timerB.on = !!(v & 2);
        this.ymCtrl = v;
        if (v & 0x10) this.ymStatus &= ~1;
        if (v & 0x20) this.ymStatus &= ~2;
      }
    }
    if (this.onYMWrite) this.onYMWrite(part, r, v, from68k);
    this.ym.write(part, r, v);
  }
  ymTimers() {
    const A = this.timerA, B = this.timerB;
    if (A.on && --A.cnt <= 0) { A.cnt += A.period; if (this.ymCtrl & 4) this.ymStatus |= 1; }
    if (B.on && --B.cnt <= 0) { B.cnt += B.period; if (this.ymCtrl & 8) this.ymStatus |= 2; }
  }

  // ---------- VDP mínimo ----------
  vdpRead(r) {
    const v = this.vdp;
    if (r < 4) return 0;
    if (r < 8) {
      v.pending = false;
      const disp = this.vdp.regs[1] & 0x40;
      return 0x3600 | (v.vint ? 0x80 : 0) | ((v.inVblank || !disp) ? 0x08 : 0) | (v.lineCyc > this.cyc68PerLine * 0.8 ? 0x04 : 0) | (this.pal ? 1 : 0);
    }
    const vc = v.line > 0xEA ? v.line - 6 : v.line;
    return ((vc & 0xFF) << 8) | (Math.floor(v.lineCyc / this.cyc68PerLine * 0xB6) & 0xFF);
  }
  vdpWrite(r, w) {
    const v = this.vdp;
    if (r < 4) { v.pending = false; return; } // datos: VRAM/CRAM no emulados
    if (r >= 8) return;
    if (v.pending) {
      v.pending = false;
      v.code = (v.code & 3) | ((w >> 2) & 0x3C);
      return; // DMA instantáneo (sin vídeo no hay nada que copiar)
    }
    if ((w & 0xC000) === 0x8000) {
      const reg = (w >> 8) & 0x1F;
      v.regs[reg] = w & 0xFF;
      if (reg === 0 || reg === 1) this.updateIRQ();
      return;
    }
    v.code = (w >> 14) & 3;
    v.pending = true;
  }
  updateIRQ() {
    const v = this.vdp, r = v.regs;
    const lvl = (v.vint && (r[1] & 0x20)) ? 6 : (v.hint && (r[0] & 0x10)) ? 4 : 0;
    this.m68k.setIRQ(lvl);
  }
  nextLine() {
    const v = this.vdp;
    v.line++;
    if (v.line >= this.timing.lines) { v.line = 0; v.inVblank = false; this.frame++; }
    // interrupción horizontal
    if (v.line <= this.timing.vblankLine) {
      if (--v.hcnt < 0) { v.hcnt = v.regs[10]; v.hint = true; this.updateIRQ(); }
    } else v.hcnt = v.regs[10];
    if (v.line === this.timing.vblankLine) {
      v.inVblank = true; v.vint = true; this.updateIRQ();
      this.z80.setIRQ(true); v.zIntLeft = 1;
    } else if (v.zIntLeft > 0 && --v.zIntLeft <= 0) this.z80.setIRQ(false);
  }

  // ---------- mandos ----------
  ioRead(r) {
    switch (r >> 1) {
      case 0: return this.version;
      case 1: case 2: {
        const port = (r >> 1) - 1;
        const th = (this.io.data[port] & this.io.ctrl[port] & 0x40) || !(this.io.ctrl[port] & 0x40) ? 0x40 : 0;
        const p = port === 0 ? this.io.pad : 0; // bits: U D L R B C A START
        let val;
        if (th) val = 0x40 | (~((p & 0x3F)) & 0x3F);
        else val = (~(((p & 3)) | ((p >> 2) & 0x30)) & 0x33);
        return (val & ~this.io.ctrl[port]) | (this.io.data[port] & this.io.ctrl[port]);
      }
      case 3: return 0x7F;
      case 4: case 5: case 6: return this.io.ctrl[(r >> 1) - 4];
      default: return 0;
    }
  }
  ioWrite(r, v) {
    const k = r >> 1;
    if (k >= 1 && k <= 3) this.io.data[k - 1] = v;
    else if (k >= 4 && k <= 6) this.io.ctrl[k - 4] = v;
  }
  press(buttons) { this.io.pad = buttons; } // U=1 D=2 L=4 R=8 B=16 C=32 A=64 START=128

  // ---------- ejecución ----------
  // Avanza una muestra del YM2612 (144 ciclos del 68000)
  tick() {
    if (!this.frozen68k) { this.c68 += 144; if (this.c68 > 0) this.c68 = -this.m68k.run(this.c68); }
    if (!this.z80Reset && !this.z80BusReq) {
      this.z80Frac += this.z80PerSample;
      const n = Math.floor(this.z80Frac);
      if (n > 0) this.z80Frac -= n + this.z80.run(n);
    }
    this.ymTimers();
    if (!this.fast) { this.ym.tick(); this.psg.run(this.psgPerSample); }
    const v = this.vdp;
    v.lineCyc += 144;
    while (v.lineCyc >= this.cyc68PerLine) { v.lineCyc -= this.cyc68PerLine; this.nextLine(); }
  }

  runFrames(n) {
    const end = this.frame + n;
    while (this.frame < end) this.tick();
  }

  save() {
    return {
      m68k: this.m68k.save(), z80: this.z80.save(), ym: dump(this.ym), psg: dump(this.psg),
      ram: this.ram.slice(), zram: this.zram.slice(), sram: this.sram.slice(),
      sys: { z80Reset: this.z80Reset, z80BusReq: this.z80BusReq, z80Bank: this.z80Bank, ymAddr: this.ymAddr.slice(),
        timerA: { ...this.timerA }, timerB: { ...this.timerB }, ymStatus: this.ymStatus, ymCtrl: this.ymCtrl, taLo: this.taLo, taHi: this.taHi,
        vdp: dump(this.vdp), io: dump(this.io), sramOn: this.sramOn, frame: this.frame, z80Frac: this.z80Frac, c68: this.c68 }
    };
  }
  load(s) {
    this.m68k.load(s.m68k); this.z80.load(s.z80);
    restore(this.ym, s.ym); restore(this.psg, s.psg);
    this.ram.set(s.ram); this.zram.set(s.zram); this.sram.set(s.sram);
    const y = s.sys;
    for (const k of ['z80Reset', 'z80BusReq', 'z80Bank', 'ymStatus', 'ymCtrl', 'taLo', 'taHi', 'sramOn', 'frame', 'z80Frac', 'c68']) this[k] = y[k];
    this.ymAddr = y.ymAddr.slice(); this.timerA = { ...y.timerA }; this.timerB = { ...y.timerB };
    this.vdp = dump(y.vdp); this.io = dump(y.io);
  }
}
