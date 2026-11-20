// Núcleo Motorola 68000 (intérprete). Bus de 24 bits.
// bus = { read8(a), read16(a), write8(a, v), write16(a, v), iack?(level) }

const MASK = [0xFF, 0xFFFF, 0xFFFFFFFF];
const MSB = [0x80, 0x8000, 0x80000000];
const BYTES = [1, 2, 4];
const sx8 = v => (v & 0x80) ? (v | ~0xFF) : (v & 0xFF);
const sx16 = v => (v & 0x8000) ? (v | ~0xFFFF) : (v & 0xFFFF);
const EA_T = [[0, 0, 4, 4, 6, 8, 10, 0], [0, 0, 8, 8, 10, 12, 14, 0]];
const EA_T7 = [[8, 12, 8, 10, 4], [12, 16, 12, 14, 8]];

class Trap { constructor(v) { this.vector = v; } }

export class M68K {
  constructor(bus) {
    this.bus = bus;
    this.d = new Int32Array(8);
    this.a = new Int32Array(8);
    this.pc = 0; this.usp = 0; this.ssp = 0;
    this.s = 1; this.t = 0; this.mask = 7;
    this.x = 0; this.n = 0; this.z = 0; this.v = 0; this.c = 0;
    this.stopped = false; this.irqLevel = 0;
    this.cycles = 0; this.cyc = 0;
  }

  // ---------- memoria ----------
  r8(a) { return this.bus.read8(a & 0xFFFFFF) & 0xFF; }
  r16(a) { return this.bus.read16(a & 0xFFFFFE) & 0xFFFF; }
  r32(a) { return ((this.r16(a) << 16) | this.r16(a + 2)) >>> 0; }
  w8(a, v) { this.bus.write8(a & 0xFFFFFF, v & 0xFF); }
  w16(a, v) { this.bus.write16(a & 0xFFFFFE, v & 0xFFFF); }
  w32(a, v) { this.w16(a, v >>> 16); this.w16(a + 2, v & 0xFFFF); }
  rd(sz, a) { return sz === 0 ? this.r8(a) : sz === 1 ? this.r16(a) : this.r32(a); }
  wr(sz, a, v) { if (sz === 0) this.w8(a, v); else if (sz === 1) this.w16(a, v); else this.w32(a, v); }
  f16() { const v = this.r16(this.pc); this.pc = (this.pc + 2) >>> 0; return v; }
  f32() { const v = this.r32(this.pc); this.pc = (this.pc + 4) >>> 0; return v; }
  push16(v) { this.a[7] -= 2; this.w16(this.a[7] >>> 0, v); }
  push32(v) { this.a[7] -= 4; this.w32(this.a[7] >>> 0, v); }
  pop16() { const v = this.r16(this.a[7] >>> 0); this.a[7] += 2; return v; }
  pop32() { const v = this.r32(this.a[7] >>> 0); this.a[7] += 4; return v; }

  // ---------- SR ----------
  get sr() { return (this.t << 15) | (this.s << 13) | (this.mask << 8) | this.ccr; }
  set sr(v) {
    const s = (v >> 13) & 1;
    if (s !== this.s) {
      if (this.s) { this.ssp = this.a[7]; this.a[7] = this.usp; } else { this.usp = this.a[7]; this.a[7] = this.ssp; }
      this.s = s;
    }
    this.t = (v >> 15) & 1; this.mask = (v >> 8) & 7; this.ccr = v;
  }
  get ccr() { return (this.x << 4) | (this.n << 3) | (this.z << 2) | (this.v << 1) | this.c; }
  set ccr(v) { this.x = (v >> 4) & 1; this.n = (v >> 3) & 1; this.z = (v >> 2) & 1; this.v = (v >> 1) & 1; this.c = v & 1; }

  reset() {
    this.s = 1; this.t = 0; this.mask = 7; this.stopped = false;
    this.a[7] = this.ssp = this.r32(0);
    this.pc = this.r32(4);
  }

  setIRQ(level) { this.irqLevel = level; }

  exception(vector, pc = this.pc) {
    const sr = this.sr;
    this.sr = (sr | 0x2000) & 0x7FFF;
    this.push32(pc >>> 0);
    this.push16(sr);
    this.pc = this.r32(vector * 4);
    this.cyc += 34;
  }

  checkIRQ() {
    const l = this.irqLevel;
    if (l > this.mask || l === 7) {
      this.stopped = false;
      if (this.bus.iack) this.bus.iack(l);
      this.exception(24 + l);
      this.mask = l;
      this.cyc += 10;
      return true;
    }
    return false;
  }

  run(cycles) {
    const end = this.cycles + cycles;
    while (this.cycles < end) {
      this.cyc = 0;
      if (this.irqLevel) this.checkIRQ();
      if (this.stopped) { this.cycles = end; break; }
      const pc = this.pc;
      const op = this.f16();
      try { this.exec(op); }
      catch (e) { if (e instanceof Trap) this.exception(e.vector, e.vector === 4 || e.vector === 10 || e.vector === 11 || e.vector === 8 ? pc : this.pc); else throw e; }
      this.cycles += this.cyc || 4;
    }
    return this.cycles - end;
  }

  // ---------- direccionamiento ----------
  idx(base) {
    const ext = this.f16();
    const r = (ext >> 12) & 7;
    let x = (ext & 0x8000) ? this.a[r] : this.d[r];
    if (!(ext & 0x800)) x = sx16(x);
    return (base + x + sx8(ext)) >>> 0;
  }
  ea(mode, reg, sz) {
    switch (mode) {
      case 2: this.cyc += EA_T[sz >> 1][2]; return this.a[reg] >>> 0;
      case 3: { const a = this.a[reg] >>> 0; this.a[reg] = a + (sz === 0 && reg === 7 ? 2 : BYTES[sz]); this.cyc += EA_T[sz >> 1][3]; return a; }
      case 4: { const a = (this.a[reg] - (sz === 0 && reg === 7 ? 2 : BYTES[sz])) >>> 0; this.a[reg] = a; this.cyc += EA_T[sz >> 1][4]; return a; }
      case 5: { const d = sx16(this.f16()); this.cyc += EA_T[sz >> 1][5]; return (this.a[reg] + d) >>> 0; }
      case 6: this.cyc += EA_T[sz >> 1][6]; return this.idx(this.a[reg]);
      case 7:
        this.cyc += EA_T7[sz >> 1][reg] || 0;
        switch (reg) {
          case 0: return sx16(this.f16()) >>> 0;
          case 1: return this.f32();
          case 2: { const pc = this.pc; return (pc + sx16(this.f16())) >>> 0; }
          case 3: { const pc = this.pc; return this.idx(pc); }
        }
    }
    throw new Trap(4);
  }
  // lectura de operando
  get(mode, reg, sz) {
    if (mode === 0) return (this.d[reg] & MASK[sz]) >>> 0;
    if (mode === 1) return (this.a[reg] & MASK[sz]) >>> 0;
    if (mode === 7 && reg === 4) { this.cyc += sz === 2 ? 8 : 4; return sz === 0 ? this.f16() & 0xFF : sz === 1 ? this.f16() : this.f32(); }
    return this.rd(sz, this.ea(mode, reg, sz));
  }
  // escritura de operando (dirección ya calculada en `addr` si es memoria)
  setD(reg, sz, v) {
    if (sz === 2) this.d[reg] = v;
    else if (sz === 1) this.d[reg] = (this.d[reg] & ~0xFFFF) | (v & 0xFFFF);
    else this.d[reg] = (this.d[reg] & ~0xFF) | (v & 0xFF);
  }
  put(mode, reg, sz, v) {
    if (mode === 0) this.setD(reg, sz, v);
    else if (mode === 1) this.a[reg] = sz === 1 ? sx16(v) : v;
    else this.wr(sz, this.ea(mode, reg, sz), v);
  }
  // lectura-modificación-escritura
  rmw(mode, reg, sz, fn) {
    if (mode === 0) { const r = fn((this.d[reg] & MASK[sz]) >>> 0); this.setD(reg, sz, r); return; }
    const a = this.ea(mode, reg, sz);
    const r = fn(this.rd(sz, a));
    this.wr(sz, a, r);
    this.cyc += sz === 2 ? 8 : 4;
  }

  // ---------- flags ----------
  nz(sz, r) { r = (r & MASK[sz]) >>> 0; this.n = (r & MSB[sz]) ? 1 : 0; this.z = r === 0 ? 1 : 0; return r; }
  logic(sz, r) { this.v = 0; this.c = 0; return this.nz(sz, r); }
  add(sz, s, d, x = 0) {
    const m = MASK[sz];
    s = (s & m) >>> 0; d = (d & m) >>> 0;
    const full = s + d + x;
    const r = (full & m) >>> 0;
    const msb = MSB[sz];
    this.c = this.x = full > m ? 1 : 0;
    this.v = (((s ^ r) & (d ^ r)) & msb) ? 1 : 0;
    this.n = (r & msb) ? 1 : 0;
    return r;
  }
  sub(sz, s, d, x = 0) {
    const m = MASK[sz];
    s = (s & m) >>> 0; d = (d & m) >>> 0;
    const r = ((d - s - x) & m) >>> 0;
    const msb = MSB[sz];
    this.c = this.x = (s + x > d) ? 1 : 0;
    this.v = (((s ^ d) & (r ^ d)) & msb) ? 1 : 0;
    this.n = (r & msb) ? 1 : 0;
    return r;
  }
  cmp(sz, s, d) { const x = this.x; const r = this.sub(sz, s, d); this.x = x; this.z = r === 0 ? 1 : 0; }

  cond(cc) {
    switch (cc) {
      case 0: return true; case 1: return false;
      case 2: return !this.c && !this.z; case 3: return !!(this.c || this.z);
      case 4: return !this.c; case 5: return !!this.c;
      case 6: return !this.z; case 7: return !!this.z;
      case 8: return !this.v; case 9: return !!this.v;
      case 10: return !this.n; case 11: return !!this.n;
      case 12: return this.n === this.v; case 13: return this.n !== this.v;
      case 14: return !this.z && this.n === this.v; default: return !!this.z || this.n !== this.v;
    }
  }

  priv() { if (!this.s) throw new Trap(8); }

  // ---------- ejecución ----------
  exec(op) {
    this.cyc += 4;
    const mode = (op >> 3) & 7, reg = op & 7, rx = (op >> 9) & 7;
    switch (op >> 12) {
      case 0x0: return this.g0(op, mode, reg, rx);
      case 0x1: return this.move(op, 0);
      case 0x2: return this.move(op, 2);
      case 0x3: return this.move(op, 1);
      case 0x4: return this.g4(op, mode, reg, rx);
      case 0x5: return this.g5(op, mode, reg, rx);
      case 0x6: { // Bcc / BRA / BSR
        const cc = (op >> 8) & 15;
        let disp = sx8(op);
        const base = this.pc;
        if ((op & 0xFF) === 0) disp = sx16(this.f16());
        if (cc === 1) { this.push32(this.pc); this.pc = (base + disp) >>> 0; this.cyc += 14; return; }
        if (this.cond(cc)) { this.pc = (base + disp) >>> 0; this.cyc += 6; } else this.cyc += 4;
        return;
      }
      case 0x7: { this.d[rx] = sx8(op); this.logic(2, this.d[rx]); return; }
      case 0x8: return this.g8(op, mode, reg, rx);
      case 0x9: return this.addsub(op, mode, reg, rx, true);
      case 0xA: throw new Trap(10);
      case 0xB: return this.gB(op, mode, reg, rx);
      case 0xC: return this.gC(op, mode, reg, rx);
      case 0xD: return this.addsub(op, mode, reg, rx, false);
      case 0xE: return this.gE(op, mode, reg, rx);
      default: throw new Trap(11);
    }
  }

  move(op, sz) {
    const sm = (op >> 3) & 7, sr = op & 7, dm = (op >> 6) & 7, dr = (op >> 9) & 7;
    const v = this.get(sm, sr, sz);
    if (dm === 1) { this.a[dr] = sz === 1 ? sx16(v) : v; return; } // MOVEA
    this.logic(sz, v);
    if (dm === 0) this.setD(dr, sz, v);
    else this.wr(sz, this.ea(dm, dr, sz), v);
  }

  g0(op, mode, reg, rx) {
    if (op & 0x100) { // BTST/BCHG/BCLR/BSET dinámicos o MOVEP
      if (mode === 1) return this.movep(op, reg, rx);
      return this.bitop((op >> 6) & 3, this.d[rx], mode, reg);
    }
    const kind = rx; // bits 11-9
    if (kind === 4) { const bit = this.f16() & 0xFF; return this.bitop((op >> 6) & 3, bit, mode, reg); }
    const sz = (op >> 6) & 3;
    if (sz === 3) throw new Trap(4);
    // a CCR / SR
    if (mode === 7 && reg === 4 && (kind === 0 || kind === 1 || kind === 5)) {
      const imm = this.f16();
      if (sz === 0) {
        const c = this.ccr;
        this.ccr = kind === 0 ? c | imm : kind === 1 ? c & imm : c ^ imm;
      } else {
        this.priv();
        const s = this.sr;
        this.sr = kind === 0 ? s | imm : kind === 1 ? s & imm : s ^ imm;
      }
      this.cyc += 16;
      return;
    }
    const imm = sz === 0 ? this.f16() & 0xFF : sz === 1 ? this.f16() : this.f32();
    this.cyc += sz === 2 ? 8 : 4;
    switch (kind) {
      case 0: return this.rmw(mode, reg, sz, d => this.logic(sz, d | imm));
      case 1: return this.rmw(mode, reg, sz, d => this.logic(sz, d & imm));
      case 2: return this.rmw(mode, reg, sz, d => { const r = this.sub(sz, imm, d); this.z = r === 0 ? 1 : 0; return r; });
      case 3: return this.rmw(mode, reg, sz, d => { const r = this.add(sz, imm, d); this.z = r === 0 ? 1 : 0; return r; });
      case 5: return this.rmw(mode, reg, sz, d => this.logic(sz, d ^ imm));
      case 6: return this.cmp(sz, imm, this.get(mode, reg, sz));
      default: throw new Trap(4);
    }
  }

  bitop(type, bit, mode, reg) {
    if (mode === 0) {
      bit &= 31;
      const m = 1 << bit;
      this.z = (this.d[reg] & m) ? 0 : 1;
      if (type === 1) this.d[reg] ^= m; else if (type === 2) this.d[reg] &= ~m; else if (type === 3) this.d[reg] |= m;
      this.cyc += 2;
      return;
    }
    bit &= 7;
    const m = 1 << bit;
    if (type === 0) { this.z = (this.get(mode, reg, 0) & m) ? 0 : 1; return; }
    const a = this.ea(mode, reg, 0);
    const v = this.r8(a);
    this.z = (v & m) ? 0 : 1;
    this.w8(a, type === 1 ? v ^ m : type === 2 ? v & ~m : v | m);
    this.cyc += 4;
  }

  movep(op, reg, dr) {
    const a = (this.a[reg] + sx16(this.f16())) >>> 0;
    const om = (op >> 6) & 3;
    if (om === 0) this.d[dr] = (this.d[dr] & ~0xFFFF) | (this.r8(a) << 8) | this.r8(a + 2);
    else if (om === 1) this.d[dr] = (this.r8(a) << 24) | (this.r8(a + 2) << 16) | (this.r8(a + 4) << 8) | this.r8(a + 6);
    else if (om === 2) { const v = this.d[dr]; this.w8(a, v >> 8); this.w8(a + 2, v); }
    else { const v = this.d[dr]; this.w8(a, v >>> 24); this.w8(a + 2, v >> 16); this.w8(a + 4, v >> 8); this.w8(a + 6, v); }
    this.cyc += om & 1 ? 20 : 12;
  }

  g4(op, mode, reg, rx) {
    if (op & 0x100) {
      if (((op >> 6) & 7) === 7) { this.a[rx] = this.ea(mode, reg, 2); this.cyc += 0; return; } // LEA
      if (((op >> 6) & 7) === 6) { // CHK.W
        const src = sx16(this.get(mode, reg, 1)), dv = sx16(this.d[rx]);
        this.cyc += 6;
        if (dv < 0) { this.n = 1; throw new Trap(6); }
        if (dv > src) { this.n = 0; throw new Trap(6); }
        return;
      }
      throw new Trap(4);
    }
    const sz = (op >> 6) & 3;
    switch (rx) {
      case 0: // NEGX / MOVE from SR
        if (sz === 3) { const s = this.sr; if (mode === 0) this.setD(reg, 1, s); else this.w16(this.ea(mode, reg, 1), s); return; }
        return this.rmw(mode, reg, sz, d => { const z = this.z; const r = this.sub(sz, d, 0, this.x); this.z = r ? 0 : z; return r; });
      case 1: // CLR
        if (sz === 3) throw new Trap(4);
        if (mode === 0) this.setD(reg, sz, 0); else { const a = this.ea(mode, reg, sz); this.wr(sz, a, 0); }
        this.n = 0; this.z = 1; this.v = 0; this.c = 0;
        return;
      case 2: // NEG / MOVE to CCR
        if (sz === 3) { this.ccr = this.get(mode, reg, 1); this.cyc += 8; return; }
        return this.rmw(mode, reg, sz, d => { const r = this.sub(sz, d, 0); this.z = r ? 0 : 1; return r; });
      case 3: // NOT / MOVE to SR
        if (sz === 3) { this.priv(); this.sr = this.get(mode, reg, 1); this.cyc += 8; return; }
        return this.rmw(mode, reg, sz, d => this.logic(sz, ~d));
      case 4:
        if (sz === 0) return this.rmw(mode, reg, 0, d => this.nbcd(d)); // NBCD
        if (sz === 1) {
          if (mode === 0) { const v = this.d[reg]; this.d[reg] = (v >>> 16) | (v << 16); this.logic(2, this.d[reg]); return; } // SWAP
          this.push32(this.ea(mode, reg, 2)); this.cyc += 8; return; // PEA
        }
        if (mode === 0) { // EXT
          if (sz === 2) { this.setD(reg, 1, sx8(this.d[reg])); this.logic(1, this.d[reg]); }
          else { this.d[reg] = sx16(this.d[reg]); this.logic(2, this.d[reg]); }
          return;
        }
        return this.movem(op, mode, reg, false);
      case 5: // TST / TAS / ILLEGAL
        if (sz === 3) {
          if (op === 0x4AFC) throw new Trap(4);
          const v = this.get(mode, reg, 0); this.logic(0, v); // TAS (la Mega Drive no completa la escritura)
          if (mode === 0) this.d[reg] |= 0x80;
          return;
        }
        this.logic(sz, this.get(mode, reg, sz));
        return;
      case 6: // MOVEM memoria -> registros
        if (sz >= 2) return this.movem(op, mode, reg, true);
        throw new Trap(4);
      case 7:
        if (sz === 1) {
          switch (mode) {
            case 0: case 1: throw new Trap(32 + (op & 15)); // TRAP
            case 2: { // LINK
              const d = sx16(this.f16());
              this.push32(this.a[reg] >>> 0);
              this.a[reg] = this.a[7];
              this.a[7] += d;
              this.cyc += 12; return;
            }
            case 3: this.a[7] = this.a[reg]; this.a[reg] = this.pop32(); this.cyc += 8; return; // UNLK
            case 4: this.priv(); this.usp = this.a[reg]; return; // MOVE An,USP
            case 5: this.priv(); this.a[reg] = this.usp; return; // MOVE USP,An
            case 6:
              switch (reg) {
                case 0: this.priv(); this.cyc += 128; return; // RESET
                case 1: return; // NOP
                case 2: { this.priv(); const v = this.f16(); this.sr = v; this.stopped = true; return; } // STOP
                case 3: { this.priv(); const sr = this.pop16(); this.pc = this.pop32(); this.sr = sr; this.cyc += 16; return; } // RTE
                case 5: this.pc = this.pop32(); this.cyc += 12; return; // RTS
                case 6: if (this.v) throw new Trap(7); return; // TRAPV
                case 7: { this.ccr = this.pop16(); this.pc = this.pop32(); this.cyc += 16; return; } // RTR
              }
          }
          throw new Trap(4);
        }
        if (sz === 2) { const a = this.ea(mode, reg, 2); this.push32(this.pc); this.pc = a; this.cyc += 12; return; } // JSR
        if (sz === 3) { this.pc = this.ea(mode, reg, 2); this.cyc += 4; return; } // JMP
    }
    throw new Trap(4);
  }

  movem(op, mode, reg, toReg) {
    const mask = this.f16();
    const sz = (op & 0x40) ? 2 : 1;
    const step = BYTES[sz];
    let n = 0;
    if (toReg) {
      let a = mode === 3 ? this.a[reg] >>> 0 : this.ea(mode, reg, sz);
      for (let i = 0; i < 16; i++) if (mask & (1 << i)) {
        const v = sz === 2 ? this.r32(a) | 0 : sx16(this.r16(a));
        if (i < 8) this.d[i] = v; else this.a[i - 8] = v;
        a += step; n++;
      }
      if (mode === 3) this.a[reg] = a;
    } else if (mode === 4) {
      let a = this.a[reg] >>> 0;
      for (let i = 0; i < 16; i++) if (mask & (1 << i)) {
        a -= step;
        const r = 15 - i;
        const v = r < 8 ? this.d[r] : this.a[r - 8];
        if (sz === 2) this.w32(a, v >>> 0); else this.w16(a, v);
        n++;
      }
      this.a[reg] = a;
    } else {
      let a = this.ea(mode, reg, sz);
      for (let i = 0; i < 16; i++) if (mask & (1 << i)) {
        const v = i < 8 ? this.d[i] : this.a[i - 8];
        if (sz === 2) this.w32(a, v >>> 0); else this.w16(a, v);
        a += step; n++;
      }
    }
    this.cyc += 8 + n * (sz === 2 ? 8 : 4);
  }

  g5(op, mode, reg, rx) {
    const sz = (op >> 6) & 3;
    if (sz === 3) {
      const cc = (op >> 8) & 15;
      if (mode === 1) { // DBcc
        const base = this.pc;
        const disp = sx16(this.f16());
        if (!this.cond(cc)) {
          const w = ((this.d[reg] & 0xFFFF) - 1) & 0xFFFF;
          this.setD(reg, 1, w);
          if (w !== 0xFFFF) { this.pc = (base + disp) >>> 0; this.cyc += 6; } else this.cyc += 10;
        } else this.cyc += 8;
        return;
      }
      const v = this.cond(cc) ? 0xFF : 0; // Scc
      if (mode === 0) this.setD(reg, 0, v); else this.w8(this.ea(mode, reg, 0), v);
      return;
    }
    const q = rx || 8;
    if (mode === 1) { this.a[reg] += (op & 0x100) ? -q : q; this.cyc += 4; return; } // ADDQ/SUBQ An
    this.rmw(mode, reg, sz, d => {
      const r = (op & 0x100) ? this.sub(sz, q, d) : this.add(sz, q, d);
      this.z = r === 0 ? 1 : 0;
      return r;
    });
  }

  g8(op, mode, reg, rx) {
    const om = (op >> 6) & 7;
    if (om === 3) { // DIVU
      const s = this.get(mode, reg, 1);
      this.cyc += 136;
      if (!s) throw new Trap(5);
      const dv = this.d[rx] >>> 0, q = Math.floor(dv / s), r = dv % s;
      if (q > 0xFFFF) { this.v = 1; this.c = 0; this.n = 1; return; }
      this.d[rx] = (r << 16) | q;
      this.n = (q & 0x8000) ? 1 : 0; this.z = q === 0 ? 1 : 0; this.v = 0; this.c = 0;
      return;
    }
    if (om === 7) { // DIVS
      const s = sx16(this.get(mode, reg, 1));
      this.cyc += 154;
      if (!s) throw new Trap(5);
      const dv = this.d[rx] | 0, q = Math.trunc(dv / s), r = dv - q * s;
      if (q > 32767 || q < -32768) { this.v = 1; this.c = 0; this.n = 1; return; }
      this.d[rx] = ((r & 0xFFFF) << 16) | (q & 0xFFFF);
      this.n = q < 0 ? 1 : 0; this.z = q === 0 ? 1 : 0; this.v = 0; this.c = 0;
      return;
    }
    if (om === 4 && mode <= 1) return this.bcdOp(op, mode, reg, rx, false); // SBCD
    const sz = om & 3;
    if (om < 4) { const v = this.get(mode, reg, sz); this.setD(rx, sz, this.logic(sz, (this.d[rx] | v))); return; }
    const s = this.d[rx];
    this.rmw(mode, reg, sz, d => this.logic(sz, d | s));
  }

  addsub(op, mode, reg, rx, isSub) {
    const om = (op >> 6) & 7;
    if (om === 3 || om === 7) { // ADDA / SUBA
      const sz = om === 3 ? 1 : 2;
      let v = this.get(mode, reg, sz);
      if (sz === 1) v = sx16(v);
      this.a[rx] = isSub ? this.a[rx] - v : this.a[rx] + v;
      this.cyc += 4;
      return;
    }
    const sz = om & 3;
    if (om >= 4 && mode <= 1) { // ADDX / SUBX
      if (mode === 0) {
        const z = this.z;
        const r = isSub ? this.sub(sz, this.d[reg], this.d[rx], this.x) : this.add(sz, this.d[reg], this.d[rx], this.x);
        this.z = r ? 0 : z;
        this.setD(rx, sz, r);
      } else {
        const sa = this.ea(4, reg, sz), s = this.rd(sz, sa);
        const da = this.ea(4, rx, sz), d = this.rd(sz, da);
        const z = this.z;
        const r = isSub ? this.sub(sz, s, d, this.x) : this.add(sz, s, d, this.x);
        this.z = r ? 0 : z;
        this.wr(sz, da, r);
      }
      return;
    }
    if (om < 4) {
      const s = this.get(mode, reg, sz);
      const r = isSub ? this.sub(sz, s, this.d[rx]) : this.add(sz, s, this.d[rx]);
      this.z = r === 0 ? 1 : 0;
      this.setD(rx, sz, r);
      return;
    }
    const s = this.d[rx];
    this.rmw(mode, reg, sz, d => { const r = isSub ? this.sub(sz, s, d) : this.add(sz, s, d); this.z = r === 0 ? 1 : 0; return r; });
  }

  gB(op, mode, reg, rx) {
    const om = (op >> 6) & 7;
    if (om === 3 || om === 7) { // CMPA
      const sz = om === 3 ? 1 : 2;
      let v = this.get(mode, reg, sz);
      if (sz === 1) v = sx16(v);
      this.cmp(2, v, this.a[rx]);
      this.cyc += 2;
      return;
    }
    const sz = om & 3;
    if (om < 4) { this.cmp(sz, this.get(mode, reg, sz), this.d[rx]); return; }
    if (mode === 1) { // CMPM
      const s = this.rd(sz, this.ea(3, reg, sz));
      const d = this.rd(sz, this.ea(3, rx, sz));
      this.cmp(sz, s, d);
      return;
    }
    const s = this.d[rx]; // EOR
    this.rmw(mode, reg, sz, d => this.logic(sz, d ^ s));
  }

  gC(op, mode, reg, rx) {
    const om = (op >> 6) & 7;
    if (om === 3) { const s = this.get(mode, reg, 1); this.d[rx] = Math.imul(this.d[rx] & 0xFFFF, s) >>> 0; this.logic(2, this.d[rx]); this.cyc += 66; return; } // MULU
    if (om === 7) { const s = sx16(this.get(mode, reg, 1)); this.d[rx] = Math.imul(sx16(this.d[rx]), s); this.logic(2, this.d[rx]); this.cyc += 66; return; } // MULS
    if (om === 4 && mode <= 1) return this.bcdOp(op, mode, reg, rx, true); // ABCD
    if (om === 5 && mode === 0) { const t = this.d[rx]; this.d[rx] = this.d[reg]; this.d[reg] = t; return; } // EXG Dx,Dy
    if (om === 5 && mode === 1) { const t = this.a[rx]; this.a[rx] = this.a[reg]; this.a[reg] = t; return; } // EXG Ax,Ay
    if (om === 6 && mode === 1) { const t = this.d[rx]; this.d[rx] = this.a[reg]; this.a[reg] = t; return; } // EXG Dx,Ay
    const sz = om & 3;
    if (om < 4) { const v = this.get(mode, reg, sz); this.setD(rx, sz, this.logic(sz, this.d[rx] & v)); return; }
    const s = this.d[rx];
    this.rmw(mode, reg, sz, d => this.logic(sz, d & s));
  }

  // ---- BCD ----
  abcd(s, d) {
    let lo = (s & 15) + (d & 15) + this.x;
    let r = (s & 0xF0) + (d & 0xF0) + lo;
    if (lo > 9) r += 6;
    let c = 0;
    if (r > 0x99) { r += 0x60; c = 1; }
    r &= 0xFF;
    this.c = this.x = c; if (r) this.z = 0; this.n = (r & 0x80) ? 1 : 0;
    return r;
  }
  sbcd(s, d) {
    let lo = (d & 15) - (s & 15) - this.x;
    let r = (d & 0xF0) - (s & 0xF0) + lo;
    if (lo < 0) r -= 6;
    let c = 0;
    if (r < 0) { r += 0xA0; c = 1; }
    r &= 0xFF;
    this.c = this.x = c; if (r) this.z = 0; this.n = (r & 0x80) ? 1 : 0;
    return r;
  }
  nbcd(d) { return this.sbcd(d, 0); }
  bcdOp(op, mode, reg, rx, isAdd) {
    if (mode === 0) { this.setD(rx, 0, isAdd ? this.abcd(this.d[reg] & 0xFF, this.d[rx] & 0xFF) : this.sbcd(this.d[reg] & 0xFF, this.d[rx] & 0xFF)); this.cyc += 2; return; }
    const sa = this.ea(4, reg, 0), s = this.r8(sa);
    const da = this.ea(4, rx, 0), d = this.r8(da);
    this.w8(da, isAdd ? this.abcd(s, d) : this.sbcd(s, d));
  }

  // ---- desplazamientos ----
  shift(type, left, sz, v, cnt) {
    const m = MASK[sz], msb = MSB[sz];
    v = (v & m) >>> 0;
    this.v = 0;
    if (cnt === 0) { this.c = type === 2 ? this.x : 0; return this.nz(sz, v); }
    let c = 0;
    for (let i = 0; i < cnt; i++) {
      if (left) {
        c = (v & msb) ? 1 : 0;
        let nv = ((v << 1) & m) >>> 0;
        if (type === 2) nv |= this.x;
        else if (type === 3) nv |= c;
        if (type === 0 && ((nv ^ v) & msb)) this.v = 1;
        v = nv >>> 0;
      } else {
        c = v & 1;
        let nv = v >>> 1;
        if (type === 0) nv |= v & msb;
        else if (type === 2) nv |= this.x ? msb : 0;
        else if (type === 3) nv |= c ? msb : 0;
        v = nv >>> 0;
      }
      if (type !== 3) this.x = c;
    }
    this.c = c;
    this.cyc += 2 * cnt;
    return this.nz(sz, v);
  }

  gE(op, mode, reg, rx) {
    const sz = (op >> 6) & 3, left = !!(op & 0x100);
    if (sz === 3) { // en memoria, de 1 bit
      const type = rx & 3;
      return this.rmw(mode, reg, 1, d => this.shift(type, left, 1, d, 1));
    }
    const type = (op >> 3) & 3;
    const cnt = (op & 0x20) ? (this.d[rx] & 63) : (rx || 8);
    this.setD(reg, sz, this.shift(type, left, sz, this.d[reg], cnt));
    this.cyc += sz === 2 ? 4 : 2;
  }

  save() {
    return { d: this.d.slice(), a: this.a.slice(), pc: this.pc, usp: this.usp, ssp: this.ssp, s: this.s, t: this.t, mask: this.mask,
      x: this.x, n: this.n, z: this.z, v: this.v, c: this.c, stopped: this.stopped, irqLevel: this.irqLevel, cycles: this.cycles };
  }
  load(o) {
    this.d.set(o.d); this.a.set(o.a);
    for (const k of ['pc', 'usp', 'ssp', 's', 't', 'mask', 'x', 'n', 'z', 'v', 'c', 'stopped', 'irqLevel', 'cycles']) this[k] = o[k];
  }
}
