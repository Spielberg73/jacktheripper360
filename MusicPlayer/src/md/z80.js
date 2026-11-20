// Núcleo Zilog Z80 (intérprete). Decodificación por campos x/y/z/p/q.
// bus = { read(addr), write(addr, v), in(port), out(port, v) }

const FS = 0x80, FZ = 0x40, FY = 0x20, FH = 0x10, FX = 0x08, FP = 0x04, FN = 0x02, FC = 0x01;
const SZ = new Uint8Array(256), SZP = new Uint8Array(256);
for (let i = 0; i < 256; i++) {
  let p = 0; for (let b = 0; b < 8; b++) p ^= (i >> b) & 1;
  SZ[i] = (i & 0x80) | (i ? 0 : FZ) | (i & (FY | FX));
  SZP[i] = SZ[i] | (p ? 0 : FP);
}
const CYC = new Uint8Array([
  4, 10, 7, 6, 4, 4, 7, 4, 4, 11, 7, 6, 4, 4, 7, 4,
  8, 10, 7, 6, 4, 4, 7, 4, 12, 11, 7, 6, 4, 4, 7, 4,
  7, 10, 16, 6, 4, 4, 7, 4, 7, 11, 16, 6, 4, 4, 7, 4,
  7, 10, 13, 6, 11, 11, 10, 4, 7, 11, 13, 6, 4, 4, 7, 4,
  4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
  4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
  4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
  7, 7, 7, 7, 7, 7, 4, 7, 4, 4, 4, 4, 4, 4, 7, 4,
  4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
  4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
  4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
  4, 4, 4, 4, 4, 4, 7, 4, 4, 4, 4, 4, 4, 4, 7, 4,
  5, 10, 10, 10, 10, 11, 7, 11, 5, 10, 10, 0, 10, 17, 7, 11,
  5, 10, 10, 11, 10, 11, 7, 11, 5, 4, 10, 11, 10, 0, 7, 11,
  5, 10, 10, 19, 10, 11, 7, 11, 5, 4, 10, 4, 10, 0, 7, 11,
  5, 10, 10, 4, 10, 11, 7, 11, 5, 6, 10, 4, 10, 0, 7, 11
]);

export class Z80 {
  constructor(bus) {
    this.bus = bus;
    this.reset();
  }

  reset() {
    this.a = 0xFF; this.f = 0xFF; this.b = 0; this.c = 0; this.d = 0; this.e = 0; this.h = 0; this.l = 0;
    this.a_ = 0; this.f_ = 0; this.b_ = 0; this.c_ = 0; this.d_ = 0; this.e_ = 0; this.h_ = 0; this.l_ = 0;
    this.ix = 0xFFFF; this.iy = 0xFFFF; this.sp = 0xFFFF; this.pc = 0;
    this.i = 0; this.r = 0; this.iff1 = 0; this.iff2 = 0; this.im = 0;
    this.halted = false; this.irq = false; this.eiDelay = false;
    this.cycles = 0;
  }

  // ---- utilidades ----
  rd(a) { return this.bus.read(a & 0xFFFF); }
  wr(a, v) { this.bus.write(a & 0xFFFF, v & 0xFF); }
  fetch() { const v = this.bus.read(this.pc); this.pc = (this.pc + 1) & 0xFFFF; return v; }
  fetch16() { const lo = this.fetch(); return lo | (this.fetch() << 8); }
  rd16(a) { return this.rd(a) | (this.rd(a + 1) << 8); }
  wr16(a, v) { this.wr(a, v); this.wr(a + 1, v >> 8); }
  push(v) { this.sp = (this.sp - 2) & 0xFFFF; this.wr16(this.sp, v); }
  pop() { const v = this.rd16(this.sp); this.sp = (this.sp + 2) & 0xFFFF; return v; }
  get bc() { return (this.b << 8) | this.c; } set bc(v) { this.b = (v >> 8) & 0xFF; this.c = v & 0xFF; }
  get de() { return (this.d << 8) | this.e; } set de(v) { this.d = (v >> 8) & 0xFF; this.e = v & 0xFF; }
  get hl() { return (this.h << 8) | this.l; } set hl(v) { this.h = (v >> 8) & 0xFF; this.l = v & 0xFF; }
  get af() { return (this.a << 8) | this.f; } set af(v) { this.a = (v >> 8) & 0xFF; this.f = v & 0xFF; }
  incR() { this.r = (this.r & 0x80) | ((this.r + 1) & 0x7F); }

  // HL / IX / IY según prefijo
  getHL(x) { return x === 0 ? this.hl : x === 1 ? this.ix : this.iy; }
  setHL(x, v) { v &= 0xFFFF; if (x === 0) this.hl = v; else if (x === 1) this.ix = v; else this.iy = v; }
  getRP(p, x) { return p === 0 ? this.bc : p === 1 ? this.de : p === 2 ? this.getHL(x) : this.sp; }
  setRP(p, v, x) { v &= 0xFFFF; if (p === 0) this.bc = v; else if (p === 1) this.de = v; else if (p === 2) this.setHL(x, v); else this.sp = v; }
  getRP2(p, x) { return p === 3 ? this.af : this.getRP(p, x); }
  setRP2(p, v, x) { if (p === 3) this.af = v; else this.setRP(p, v, x); }

  // Registro de 8 bits r (0..7). Para r=6 se pasa la dirección ya calculada.
  getR(r, x, addr) {
    switch (r) {
      case 0: return this.b; case 1: return this.c; case 2: return this.d; case 3: return this.e;
      case 4: return x === 0 ? this.h : x === 1 ? this.ix >> 8 : this.iy >> 8;
      case 5: return x === 0 ? this.l : x === 1 ? this.ix & 0xFF : this.iy & 0xFF;
      case 6: return this.rd(addr);
      default: return this.a;
    }
  }
  setR(r, v, x, addr) {
    v &= 0xFF;
    switch (r) {
      case 0: this.b = v; break; case 1: this.c = v; break; case 2: this.d = v; break; case 3: this.e = v; break;
      case 4: if (x === 0) this.h = v; else if (x === 1) this.ix = (this.ix & 0xFF) | (v << 8); else this.iy = (this.iy & 0xFF) | (v << 8); break;
      case 5: if (x === 0) this.l = v; else if (x === 1) this.ix = (this.ix & 0xFF00) | v; else this.iy = (this.iy & 0xFF00) | v; break;
      case 6: this.wr(addr, v); break;
      default: this.a = v;
    }
  }
  memAddr(x) { // dirección de (HL) o (IX+d)
    if (x === 0) return this.hl;
    const d = this.fetch();
    return (this.getHL(x) + (d < 128 ? d : d - 256)) & 0xFFFF;
  }

  cond(y) {
    const f = this.f;
    switch (y) {
      case 0: return !(f & FZ); case 1: return !!(f & FZ); case 2: return !(f & FC); case 3: return !!(f & FC);
      case 4: return !(f & FP); case 5: return !!(f & FP); case 6: return !(f & FS); default: return !!(f & FS);
    }
  }

  // ---- ALU ----
  add8(v, c) {
    const a = this.a, r = a + v + c;
    this.f = SZ[r & 0xFF] | ((a ^ v ^ r) & FH) | (((a ^ ~v) & (a ^ r) & 0x80) ? FP : 0) | (r > 0xFF ? FC : 0);
    this.a = r & 0xFF;
  }
  sub8(v, c, store = true) {
    const a = this.a, r = a - v - c;
    this.f = SZ[r & 0xFF] | ((a ^ v ^ r) & FH) | (((a ^ v) & (a ^ r) & 0x80) ? FP : 0) | (r < 0 ? FC : 0) | FN;
    if (store) this.a = r & 0xFF;
    else this.f = (this.f & ~(FX | FY)) | (v & (FX | FY));
  }
  alu(y, v) {
    switch (y) {
      case 0: this.add8(v, 0); break;
      case 1: this.add8(v, this.f & FC); break;
      case 2: this.sub8(v, 0); break;
      case 3: this.sub8(v, this.f & FC); break;
      case 4: this.a &= v; this.f = SZP[this.a] | FH; break;
      case 5: this.a ^= v; this.f = SZP[this.a]; break;
      case 6: this.a |= v; this.f = SZP[this.a]; break;
      default: this.sub8(v, 0, false);
    }
  }
  inc8(v) { const r = (v + 1) & 0xFF; this.f = (this.f & FC) | SZ[r] | (r === 0x80 ? FP : 0) | ((r & 0x0F) ? 0 : FH); return r; }
  dec8(v) { const r = (v - 1) & 0xFF; this.f = (this.f & FC) | SZ[r] | FN | (r === 0x7F ? FP : 0) | ((v & 0x0F) ? 0 : FH); return r; }
  add16(a, b) {
    const r = a + b;
    this.f = (this.f & (FS | FZ | FP)) | (((a ^ b ^ r) >> 8) & FH) | ((r >> 8) & (FX | FY)) | (r > 0xFFFF ? FC : 0);
    return r & 0xFFFF;
  }
  adc16(b) {
    const a = this.hl, c = this.f & FC, r = a + b + c;
    this.f = ((r >> 8) & (FS | FX | FY)) | ((r & 0xFFFF) ? 0 : FZ) | (((a ^ b ^ r) >> 8) & FH) |
      (((a ^ ~b) & (a ^ r) & 0x8000) ? FP : 0) | (r > 0xFFFF ? FC : 0);
    this.hl = r & 0xFFFF;
  }
  sbc16(b) {
    const a = this.hl, c = this.f & FC, r = a - b - c;
    this.f = ((r >> 8) & (FS | FX | FY)) | ((r & 0xFFFF) ? 0 : FZ) | (((a ^ b ^ r) >> 8) & FH) |
      (((a ^ b) & (a ^ r) & 0x8000) ? FP : 0) | (r < 0 ? FC : 0) | FN;
    this.hl = r & 0xFFFF;
  }
  rot(y, v) {
    let r, c;
    switch (y) {
      case 0: c = v >> 7; r = ((v << 1) | c) & 0xFF; break;               // RLC
      case 1: c = v & 1; r = (v >> 1) | (c << 7); break;                  // RRC
      case 2: c = v >> 7; r = ((v << 1) | (this.f & FC)) & 0xFF; break;   // RL
      case 3: c = v & 1; r = (v >> 1) | ((this.f & FC) << 7); break;      // RR
      case 4: c = v >> 7; r = (v << 1) & 0xFF; break;                     // SLA
      case 5: c = v & 1; r = (v >> 1) | (v & 0x80); break;                // SRA
      case 6: c = v >> 7; r = ((v << 1) | 1) & 0xFF; break;               // SLL (no documentada)
      default: c = v & 1; r = v >> 1;                                     // SRL
    }
    this.f = SZP[r] | c;
    return r;
  }

  // ---- interrupciones ----
  setIRQ(v) { this.irq = v; }
  interrupt() {
    if (!this.irq || !this.iff1 || this.eiDelay) return 0;
    if (this.halted) { this.halted = false; this.pc = (this.pc + 1) & 0xFFFF; }
    this.iff1 = this.iff2 = 0;
    this.incR();
    if (this.im === 2) { this.push(this.pc); this.pc = this.rd16((this.i << 8) | 0xFF); return 19; }
    this.push(this.pc); this.pc = 0x38; return 13; // IM 0 (RST 38h en la Mega Drive) e IM 1
  }

  run(cycles) {
    const end = this.cycles + cycles;
    while (this.cycles < end) this.step();
    return this.cycles - end;
  }

  step() {
    let c = this.interrupt();
    if (c) { this.cycles += c; return c; }
    if (this.halted) { this.incR(); this.cycles += 4; return 4; }
    this.eiDelay = false;
    c = this.exec(this.fetch(), 0);
    this.cycles += c;
    return c;
  }

  exec(op, x) {
    this.incR();
    let cyc = CYC[op] + (x ? 4 : 0);
    const X = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
    switch (X) {
      case 0:
        switch (z) {
          case 0:
            switch (y) {
              case 0: break; // NOP
              case 1: { const t = this.af; this.af = (this.a_ << 8) | this.f_; this.a_ = t >> 8; this.f_ = t & 0xFF; break; }
              case 2: { const d = this.fetch(); this.b = (this.b - 1) & 0xFF; if (this.b) { this.pc = (this.pc + (d < 128 ? d : d - 256)) & 0xFFFF; cyc += 5; } break; }
              case 3: { const d = this.fetch(); this.pc = (this.pc + (d < 128 ? d : d - 256)) & 0xFFFF; break; }
              default: { const d = this.fetch(); if (this.cond(y - 4)) { this.pc = (this.pc + (d < 128 ? d : d - 256)) & 0xFFFF; cyc += 5; } }
            }
            break;
          case 1:
            if (!q) this.setRP(p, this.fetch16(), x);
            else this.setHL(x, this.add16(this.getHL(x), this.getRP(p, x)));
            break;
          case 2:
            switch (y) {
              case 0: this.wr(this.bc, this.a); break;
              case 1: this.a = this.rd(this.bc); break;
              case 2: this.wr(this.de, this.a); break;
              case 3: this.a = this.rd(this.de); break;
              case 4: this.wr16(this.fetch16(), this.getHL(x)); break;
              case 5: this.setHL(x, this.rd16(this.fetch16())); break;
              case 6: this.wr(this.fetch16(), this.a); break;
              default: this.a = this.rd(this.fetch16());
            }
            break;
          case 3: this.setRP(p, this.getRP(p, x) + (q ? -1 : 1), x); break;
          case 4: case 5: {
            if (y === 6) {
              const a = this.memAddr(x);
              if (x) cyc += 8;
              this.wr(a, z === 4 ? this.inc8(this.rd(a)) : this.dec8(this.rd(a)));
            } else this.setR(y, z === 4 ? this.inc8(this.getR(y, x)) : this.dec8(this.getR(y, x)), x);
            break;
          }
          case 6: {
            if (y === 6) { const a = this.memAddr(x); if (x) cyc += 4; this.wr(a, this.fetch()); }
            else this.setR(y, this.fetch(), x);
            break;
          }
          default:
            switch (y) {
              case 0: { const c = this.a >> 7; this.a = ((this.a << 1) | c) & 0xFF; this.f = (this.f & (FS | FZ | FP)) | (this.a & (FX | FY)) | c; break; }
              case 1: { const c = this.a & 1; this.a = (this.a >> 1) | (c << 7); this.f = (this.f & (FS | FZ | FP)) | (this.a & (FX | FY)) | c; break; }
              case 2: { const c = this.a >> 7; this.a = ((this.a << 1) | (this.f & FC)) & 0xFF; this.f = (this.f & (FS | FZ | FP)) | (this.a & (FX | FY)) | c; break; }
              case 3: { const c = this.a & 1; this.a = (this.a >> 1) | ((this.f & FC) << 7); this.f = (this.f & (FS | FZ | FP)) | (this.a & (FX | FY)) | c; break; }
              case 4: { // DAA
                let a = this.a, corr = 0, carry = this.f & FC;
                if ((this.f & FH) || (a & 0x0F) > 9) corr |= 0x06;
                if (carry || a > 0x99) { corr |= 0x60; carry = FC; }
                const n = this.f & FN;
                const r = n ? (a - corr) & 0xFF : (a + corr) & 0xFF;
                this.f = SZP[r] | carry | n | ((a ^ r) & FH);
                this.a = r;
                break;
              }
              case 5: this.a ^= 0xFF; this.f = (this.f & (FS | FZ | FP | FC)) | FH | FN | (this.a & (FX | FY)); break;
              case 6: this.f = (this.f & (FS | FZ | FP)) | FC | (this.a & (FX | FY)); break;
              default: this.f = (this.f & (FS | FZ | FP)) | ((this.f & FC) ? FH : FC) | (this.a & (FX | FY));
            }
        }
        break;

      case 1:
        if (op === 0x76) { this.halted = true; this.pc = (this.pc - 1) & 0xFFFF; break; }
        if (y === 6) { const a = this.memAddr(x); if (x) cyc += 8; this.wr(a, this.getR(z, 0)); }
        else if (z === 6) { const a = this.memAddr(x); if (x) cyc += 8; this.setR(y, this.rd(a), 0); }
        else this.setR(y, this.getR(z, x), x);
        break;

      case 2:
        if (z === 6) { const a = this.memAddr(x); if (x) cyc += 8; this.alu(y, this.rd(a)); }
        else this.alu(y, this.getR(z, x));
        break;

      default:
        switch (z) {
          case 0: if (this.cond(y)) { this.pc = this.pop(); cyc += 6; } break;
          case 1:
            if (!q) this.setRP2(p, this.pop(), x);
            else switch (p) {
              case 0: this.pc = this.pop(); break;
              case 1: { // EXX
                let t;
                t = this.b; this.b = this.b_; this.b_ = t; t = this.c; this.c = this.c_; this.c_ = t;
                t = this.d; this.d = this.d_; this.d_ = t; t = this.e; this.e = this.e_; this.e_ = t;
                t = this.h; this.h = this.h_; this.h_ = t; t = this.l; this.l = this.l_; this.l_ = t;
                break;
              }
              case 2: this.pc = this.getHL(x); break;
              default: this.sp = this.getHL(x);
            }
            break;
          case 2: { const a = this.fetch16(); if (this.cond(y)) this.pc = a; break; }
          case 3:
            switch (y) {
              case 0: this.pc = this.fetch16(); break;
              case 1: return this.execCB(x);
              case 2: this.bus.out((this.a << 8) | this.fetch(), this.a); break;
              case 3: this.a = this.bus.in((this.a << 8) | this.fetch()); break;
              case 4: { const v = this.rd16(this.sp); this.wr16(this.sp, this.getHL(x)); this.setHL(x, v); break; }
              case 5: { const t = this.de; this.de = this.hl; this.hl = t; break; }
              case 6: this.iff1 = this.iff2 = 0; break;
              default: this.iff1 = this.iff2 = 1; this.eiDelay = true;
            }
            break;
          case 4: { const a = this.fetch16(); if (this.cond(y)) { this.push(this.pc); this.pc = a; cyc += 7; } break; }
          case 5:
            if (!q) this.push(this.getRP2(p, x));
            else switch (p) {
              case 0: { const a = this.fetch16(); this.push(this.pc); this.pc = a; break; }
              case 1: return this.exec(this.fetch(), 1);
              case 2: return this.execED();
              default: return this.exec(this.fetch(), 2);
            }
            break;
          case 6: this.alu(y, this.fetch()); break;
          default: this.push(this.pc); this.pc = y << 3;
        }
    }
    return cyc;
  }

  execCB(x) {
    let addr = 0, op;
    if (x) { // DD CB d op
      const d = this.fetch();
      addr = (this.getHL(x) + (d < 128 ? d : d - 256)) & 0xFFFF;
      op = this.fetch();
    } else { op = this.fetch(); this.incR(); }
    const X = op >> 6, y = (op >> 3) & 7, z = op & 7;
    const mem = x || z === 6;
    if (!x && z === 6) addr = this.hl;
    const v = mem ? this.rd(addr) : this.getR(z, 0);
    let cyc = x ? 23 : (z === 6 ? 15 : 8);
    switch (X) {
      case 0: { const r = this.rot(y, v); if (mem) this.wr(addr, r); if (!mem || (x && z !== 6)) this.setR(z, r, 0); break; }
      case 1: {
        const r = v & (1 << y);
        let f = (this.f & FC) | FH | (r ? (r & FS) : (FZ | FP));
        f |= mem ? ((addr >> 8) & (FX | FY)) : (v & (FX | FY));
        this.f = f;
        cyc = x ? 20 : (z === 6 ? 12 : 8);
        break;
      }
      case 2: { const r = v & ~(1 << y); if (mem) this.wr(addr, r); if (!mem || (x && z !== 6)) this.setR(z, r, 0); break; }
      default: { const r = v | (1 << y); if (mem) this.wr(addr, r); if (!mem || (x && z !== 6)) this.setR(z, r, 0); }
    }
    return cyc;
  }

  execED() {
    const op = this.fetch();
    this.incR();
    const X = op >> 6, y = (op >> 3) & 7, z = op & 7, p = y >> 1, q = y & 1;
    if (X === 1) {
      switch (z) {
        case 0: { const v = this.bus.in(this.bc); if (y !== 6) this.setR(y, v, 0); this.f = (this.f & FC) | SZP[v]; return 12; }
        case 1: this.bus.out(this.bc, y === 6 ? 0 : this.getR(y, 0)); return 12;
        case 2: if (q) this.adc16(this.getRP(p, 0)); else this.sbc16(this.getRP(p, 0)); return 15;
        case 3: { const a = this.fetch16(); if (q) this.setRP(p, this.rd16(a), 0); else this.wr16(a, this.getRP(p, 0)); return 20; }
        case 4: { const v = this.a; this.a = 0; this.sub8(v, 0); return 8; }
        case 5: this.pc = this.pop(); this.iff1 = this.iff2; return 14;
        case 6: this.im = [0, 0, 1, 2, 0, 0, 1, 2][y]; return 8;
        default:
          switch (y) {
            case 0: this.i = this.a; return 9;
            case 1: this.r = this.a; return 9;
            case 2: this.a = this.i; this.f = (this.f & FC) | SZ[this.a] | (this.iff2 ? FP : 0); return 9;
            case 3: this.a = this.r; this.f = (this.f & FC) | SZ[this.a] | (this.iff2 ? FP : 0); return 9;
            case 4: { // RRD
              const m = this.rd(this.hl);
              this.wr(this.hl, ((this.a << 4) | (m >> 4)) & 0xFF);
              this.a = (this.a & 0xF0) | (m & 0x0F);
              this.f = (this.f & FC) | SZP[this.a]; return 18;
            }
            case 5: { // RLD
              const m = this.rd(this.hl);
              this.wr(this.hl, ((m << 4) | (this.a & 0x0F)) & 0xFF);
              this.a = (this.a & 0xF0) | (m >> 4);
              this.f = (this.f & FC) | SZP[this.a]; return 18;
            }
            default: return 8;
          }
      }
    }
    if (X === 2 && y >= 4 && z <= 3) return this.block(y, z);
    return 8; // NOP no documentado
  }

  block(y, z) {
    const inc = (y & 1) ? -1 : 1, rep = y >= 6;
    let cyc = 16;
    switch (z) {
      case 0: { // LDI/LDD/LDIR/LDDR
        const v = this.rd(this.hl);
        this.wr(this.de, v);
        this.hl = (this.hl + inc) & 0xFFFF; this.de = (this.de + inc) & 0xFFFF; this.bc = (this.bc - 1) & 0xFFFF;
        const n = (v + this.a) & 0xFF;
        this.f = (this.f & (FS | FZ | FC)) | (this.bc ? FP : 0) | (n & FX) | ((n << 4) & FY);
        if (rep && this.bc) { this.pc = (this.pc - 2) & 0xFFFF; cyc += 5; }
        break;
      }
      case 1: { // CPI/CPD/CPIR/CPDR
        const v = this.rd(this.hl), r = (this.a - v) & 0xFF;
        const h = (this.a ^ v ^ r) & FH;
        this.hl = (this.hl + inc) & 0xFFFF; this.bc = (this.bc - 1) & 0xFFFF;
        const n = (r - (h ? 1 : 0)) & 0xFF;
        this.f = (this.f & FC) | (SZ[r] & ~(FX | FY)) | h | FN | (this.bc ? FP : 0) | (n & FX) | ((n << 4) & FY);
        if (rep && this.bc && r) { this.pc = (this.pc - 2) & 0xFFFF; cyc += 5; }
        break;
      }
      case 2: { // INI/IND/INIR/INDR
        const v = this.bus.in(this.bc);
        this.wr(this.hl, v);
        this.b = (this.b - 1) & 0xFF; this.hl = (this.hl + inc) & 0xFFFF;
        this.f = SZ[this.b] | FN;
        if (rep && this.b) { this.pc = (this.pc - 2) & 0xFFFF; cyc += 5; }
        break;
      }
      default: { // OUTI/OUTD/OTIR/OTDR
        const v = this.rd(this.hl);
        this.b = (this.b - 1) & 0xFF;
        this.bus.out(this.bc, v);
        this.hl = (this.hl + inc) & 0xFFFF;
        this.f = SZ[this.b] | FN;
        if (rep && this.b) { this.pc = (this.pc - 2) & 0xFFFF; cyc += 5; }
      }
    }
    return cyc;
  }

  // estado serializable
  save() { const o = {}; for (const k of Object.keys(this)) if (k !== 'bus') o[k] = this[k]; return o; }
  load(o) { Object.assign(this, o); }
}
