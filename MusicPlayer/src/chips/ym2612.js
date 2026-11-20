// Emulación del Yamaha YM2612 (OPN2) — chip FM de la Mega Drive.
// Basado en el funcionamiento documentado del OPN (tablas log-sin/exp, generador de
// envolventes con contadores de tasa, 8 algoritmos, feedback, LFO, modo especial del
// canal 3 y DAC del canal 6). Se ejecuta a la frecuencia nativa: reloj / 144.

const SIN_LEN = 1024, SIN_MASK = SIN_LEN - 1;
const TL_RES_LEN = 256, TL_TAB_LEN = 13 * 2 * TL_RES_LEN;
const ENV_QUIET = TL_TAB_LEN >> 3;
const MAX_ATT = 1023;
const ENV_STEP = 128 / 1024;
const EG_OFF = 0, EG_REL = 1, EG_SUS = 2, EG_DEC = 3, EG_ATT = 4;

const TL_TAB = new Int32Array(TL_TAB_LEN);
const SIN_TAB = new Uint32Array(SIN_LEN);
(function init() {
  for (let x = 0; x < TL_RES_LEN; x++) {
    let m = Math.floor((1 << 16) / Math.pow(2, (x + 1) * (ENV_STEP / 4) / 8));
    let n = m >> 4;
    n = (n & 1) ? (n >> 1) + 1 : n >> 1;
    n <<= 2;
    TL_TAB[x * 2] = n;
    TL_TAB[x * 2 + 1] = -n;
    for (let i = 1; i < 13; i++) {
      TL_TAB[x * 2 + i * 2 * TL_RES_LEN] = n >> i;
      TL_TAB[x * 2 + 1 + i * 2 * TL_RES_LEN] = -(n >> i);
    }
  }
  for (let i = 0; i < SIN_LEN; i++) {
    const m = Math.sin(((i * 2) + 1) * Math.PI / SIN_LEN);
    let o = m > 0 ? 8 * Math.log(1 / m) / Math.log(2) : 8 * Math.log(-1 / m) / Math.log(2);
    o = o / (ENV_STEP / 4);
    let n = Math.trunc(2 * o);
    n = (n & 1) ? (n >> 1) + 1 : n >> 1;
    SIN_TAB[i] = n * 2 + (m >= 0 ? 0 : 1);
  }
})();

const EG_INC = new Uint8Array([
  0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 1, 1, 0, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1,
  1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 2, 1, 1, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 1, 2, 2, 2, 1, 2, 2, 2,
  2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 4, 2, 2, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 2, 4, 4, 4, 2, 4, 4, 4,
  4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 8, 4, 4, 4, 8, 4, 8, 4, 8, 4, 8, 4, 8, 4, 8, 8, 8, 4, 8, 8, 8,
  8, 8, 8, 8, 8, 8, 8, 8, 16, 16, 16, 16, 16, 16, 16, 16, 0, 0, 0, 0, 0, 0, 0, 0
]);
const RATE_SEL = new Uint8Array(128);
const RATE_SH = new Uint8Array(128);
(function initRates() {
  for (let i = 0; i < 128; i++) {
    if (i < 32) { RATE_SEL[i] = 18; RATE_SH[i] = 0; continue; }
    const r = i - 32;
    if (r < 48) { RATE_SEL[i] = r & 3; RATE_SH[i] = 11 - (r >> 2); }
    else if (r < 60) { RATE_SEL[i] = 4 + (r - 48); RATE_SH[i] = 0; }
    else { RATE_SEL[i] = 16; RATE_SH[i] = 0; }
  }
})();

const DT_BASE = [
  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
  0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 6, 6, 7, 8, 8, 8, 8,
  1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 6, 6, 7, 8, 8, 9, 10, 11, 12, 13, 14, 16, 16, 16, 16,
  2, 2, 2, 2, 2, 3, 3, 3, 4, 4, 4, 5, 5, 6, 6, 7, 8, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 20, 22, 22, 22, 22
];
const DT_TAB = [];
for (let d = 0; d < 8; d++) {
  const row = new Int32Array(32);
  for (let k = 0; k < 32; k++) row[k] = d < 4 ? DT_BASE[d * 32 + k] : -DT_BASE[(d - 4) * 32 + k];
  DT_TAB.push(row);
}
const FKTABLE = [0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 3, 3, 3, 3, 3, 3];
const SL_TAB = Array.from({ length: 16 }, (_, i) => (i < 15 ? i : 31) * 32);
const LFO_STEPS = [108, 77, 71, 67, 62, 44, 8, 5];
const AMS_SHIFT = [8, 3, 1, 0];
const PMS_CENTS = [0, 3.4, 6.7, 10, 14, 20, 40, 80];
const OPMAP = [0, 2, 1, 3]; // orden de registros (S1,S3,S2,S4) -> índice S1..S4

class Op {
  constructor() {
    this.dt = 0; this.mul = 1; this.tl = 0; this.ksShift = 3;
    this.ar = 0; this.d1r = 0; this.d2r = 0; this.rr = 0; this.sl = 0; this.amMask = 0;
    this.phase = 0; this.incr = 0; this.vol = MAX_ATT; this.state = EG_OFF; this.key = 0;
    this.arIdx = 0; this.shAr = 0; this.selAr = 144; this.shD1 = 0; this.selD1 = 144;
    this.shD2 = 0; this.selD2 = 144; this.shRr = 0; this.selRr = 144;
  }
}

class Channel {
  constructor() {
    this.ops = [new Op(), new Op(), new Op(), new Op()];
    this.alg = 0; this.fb = 0; this.panL = true; this.panR = true; this.ams = 8; this.pms = 0;
    this.fnum = 0; this.blk = 0; this.kc = 0; this.fnh = 0;
    this.op1a = 0; this.op1b = 0; this.mem = 0;
  }
}

export class YM2612 {
  constructor(clock = 7670453) {
    this.clock = clock;
    this.rate = clock / 144;
    this.reset();
  }

  reset() {
    this.ch = Array.from({ length: 6 }, () => new Channel());
    this.sl3 = { fnum: [0, 0, 0], blk: [0, 0, 0], kc: [0, 0, 0], fnh: 0 };
    this.mode = 0;
    this.egCnt = 1; this.egTimer = 0;
    this.lfoEn = false; this.lfoFreq = 0; this.lfoCnt = 0; this.lfoTimer = 0;
    this.lfoAM = 126; this.lfoPM = 0;
    this.dacEn = false; this.dacOut = 0;
    this.outL = 0; this.outR = 0;
    this.levels = new Float32Array(6);
    for (const c of this.ch) this._refreshCh(c);
  }

  write(port, a, v) {
    a &= 0xFF; v &= 0xFF;
    if (port === 0 && a < 0x30) {
      switch (a) {
        case 0x22:
          this.lfoEn = !!(v & 8); this.lfoFreq = v & 7;
          if (!this.lfoEn) { this.lfoCnt = 0; this.lfoTimer = 0; this.lfoAM = 126; this._setPM(0); }
          break;
        case 0x27: {
          const old = this.mode & 0xC0;
          this.mode = v;
          if ((v & 0xC0) !== old) this._refreshCh(this.ch[2]);
          break;
        }
        case 0x28: {
          let c = v & 3;
          if (c === 3) return;
          if (v & 4) c += 3;
          const ch = this.ch[c];
          for (let i = 0; i < 4; i++) (v & (0x10 << i)) ? this._keyOn(ch.ops[i]) : this._keyOff(ch.ops[i]);
          break;
        }
        case 0x2A: this.dacOut = (v - 0x80) << 6; break;
        case 0x2B: this.dacEn = !!(v & 0x80); break;
      }
      return;
    }
    const c = a & 3;
    if (c === 3) return;
    const ch = this.ch[c + (port ? 3 : 0)];
    if (a >= 0xA0) {
      switch (a & 0xFC) {
        case 0xA0:
          ch.fnum = ((ch.fnh & 7) << 8) | v;
          ch.blk = (ch.fnh >> 3) & 7;
          ch.kc = (ch.blk << 2) | FKTABLE[ch.fnum >> 7];
          this._refreshCh(ch);
          break;
        case 0xA4: ch.fnh = v & 0x3F; break;
        case 0xA8:
          if (port) return;
          this.sl3.fnum[c] = ((this.sl3.fnh & 7) << 8) | v;
          this.sl3.blk[c] = (this.sl3.fnh >> 3) & 7;
          this.sl3.kc[c] = (this.sl3.blk[c] << 2) | FKTABLE[this.sl3.fnum[c] >> 7];
          this._refreshCh(this.ch[2]);
          break;
        case 0xAC: if (!port) this.sl3.fnh = v & 0x3F; break;
        case 0xB0: {
          ch.alg = v & 7;
          const fb = (v >> 3) & 7;
          ch.fb = fb ? fb + 6 : 0;
          break;
        }
        case 0xB4:
          ch.panL = !!(v & 0x80); ch.panR = !!(v & 0x40);
          ch.ams = AMS_SHIFT[(v >> 4) & 3]; ch.pms = v & 7;
          this._refreshCh(ch);
          break;
      }
      return;
    }
    const op = ch.ops[OPMAP[(a >> 2) & 3]];
    switch (a & 0xF0) {
      case 0x30: op.dt = (v >> 4) & 7; op.mul = (v & 15) ? (v & 15) * 2 : 1; this._refreshCh(ch); break;
      case 0x40: op.tl = (v & 0x7F) << 3; break;
      case 0x50: op.ksShift = 3 - (v >> 6); op.ar = v & 31; this._refreshCh(ch); break;
      case 0x60: op.amMask = (v & 0x80) ? ~0 : 0; op.d1r = v & 31; this._refreshCh(ch); break;
      case 0x70: op.d2r = v & 31; this._refreshCh(ch); break;
      case 0x80: op.sl = SL_TAB[v >> 4]; op.rr = v & 15; this._refreshCh(ch); break;
      // 0x90: SSG-EG (no emulado; apenas lo usan unos pocos juegos)
    }
  }

  _keyOn(op) {
    if (op.key) return;
    op.key = 1;
    op.phase = 0;
    if (op.arIdx >= 94) { op.vol = 0; op.state = EG_DEC; }
    else op.state = EG_ATT;
  }
  _keyOff(op) {
    if (!op.key) return;
    op.key = 0;
    if (op.state > EG_REL) op.state = EG_REL;
  }

  _setPM(pm) {
    if (pm === this.lfoPM) return;
    this.lfoPM = pm;
    for (const c of this.ch) if (c.pms) this._refreshCh(c);
  }

  _pmMult(ch) {
    if (!ch.pms || !this.lfoEn) return 1;
    const p = this.lfoPM & 31;
    const q = p & 7;
    let w;
    if (p < 8) w = q / 7; else if (p < 16) w = 1 - q / 7; else if (p < 24) w = -q / 7; else w = -1 + q / 7;
    return Math.pow(2, PMS_CENTS[ch.pms] * w / 1200);
  }

  _refreshCh(ch) {
    const mult = this._pmMult(ch);
    const special = ch === this.ch[2] && (this.mode & 0xC0);
    if (special) {
      const s = this.sl3;
      this._refreshOp(ch.ops[0], s.fnum[1], s.blk[1], s.kc[1], mult);
      this._refreshOp(ch.ops[1], s.fnum[2], s.blk[2], s.kc[2], mult);
      this._refreshOp(ch.ops[2], s.fnum[0], s.blk[0], s.kc[0], mult);
      this._refreshOp(ch.ops[3], ch.fnum, ch.blk, ch.kc, mult);
    } else {
      for (const op of ch.ops) this._refreshOp(op, ch.fnum, ch.blk, ch.kc, mult);
    }
  }

  _refreshOp(op, fnum, blk, kc, mult) {
    const fn = mult === 1 ? fnum : Math.min(0xFFF, Math.round(fnum * mult));
    let fc = ((fn << blk) >> 1) + DT_TAB[op.dt][kc];
    fc &= 0x1FFFF;
    op.incr = ((fc * op.mul) >> 1) << 6;
    const ksr = kc >> op.ksShift;
    let i = Math.min(127, (op.ar ? 32 + (op.ar << 1) : 0) + ksr);
    op.arIdx = i; op.shAr = RATE_SH[i]; op.selAr = RATE_SEL[i] * 8;
    i = Math.min(127, (op.d1r ? 32 + (op.d1r << 1) : 0) + ksr);
    op.shD1 = RATE_SH[i]; op.selD1 = RATE_SEL[i] * 8;
    i = Math.min(127, (op.d2r ? 32 + (op.d2r << 1) : 0) + ksr);
    op.shD2 = RATE_SH[i]; op.selD2 = RATE_SEL[i] * 8;
    i = Math.min(127, 34 + (op.rr << 2) + ksr);
    op.shRr = RATE_SH[i]; op.selRr = RATE_SEL[i] * 8;
  }

  _eg(op) {
    const cnt = this.egCnt;
    switch (op.state) {
      case EG_ATT:
        if (!(cnt & ((1 << op.shAr) - 1))) {
          op.vol += (~op.vol * EG_INC[op.selAr + ((cnt >> op.shAr) & 7)]) >> 4;
          if (op.vol <= 0) { op.vol = 0; op.state = EG_DEC; }
        }
        break;
      case EG_DEC:
        if (!(cnt & ((1 << op.shD1) - 1))) {
          op.vol += EG_INC[op.selD1 + ((cnt >> op.shD1) & 7)];
          if (op.vol >= op.sl) op.state = EG_SUS;
        }
        break;
      case EG_SUS:
        if (!(cnt & ((1 << op.shD2) - 1))) {
          op.vol += EG_INC[op.selD2 + ((cnt >> op.shD2) & 7)];
          if (op.vol >= MAX_ATT) op.vol = MAX_ATT;
        }
        break;
      case EG_REL:
        if (!(cnt & ((1 << op.shRr) - 1))) {
          op.vol += EG_INC[op.selRr + ((cnt >> op.shRr) & 7)];
          if (op.vol >= MAX_ATT) { op.vol = MAX_ATT; op.state = EG_OFF; }
        }
        break;
    }
  }

  _chan(ch) {
    const o = ch.ops, am = this.lfoAM >> ch.ams;
    const o0 = o[0], o1 = o[1], o2 = o[2], o3 = o[3];
    // S1 con feedback
    const fbIn = ch.op1a + ch.op1b;
    ch.op1a = ch.op1b;
    ch.op1b = 0;
    let eg = o0.vol + o0.tl + (am & o0.amMask);
    if (eg < ENV_QUIET) {
      const pm = ch.fb ? fbIn << ch.fb : 0;
      const p = (eg << 3) + SIN_TAB[(((o0.phase & ~0xFFFF) + pm) >> 16) & SIN_MASK];
      ch.op1b = p >= TL_TAB_LEN ? 0 : TL_TAB[p];
    }
    const m1 = ch.op1a;
    const e1 = o1.vol + o1.tl + (am & o1.amMask);
    const e2 = o2.vol + o2.tl + (am & o2.amMask);
    const e3 = o3.vol + o3.tl + (am & o3.amMask);
    const memPrev = ch.mem;
    let out = 0, v1, v2, v3;
    switch (ch.alg) {
      case 0: v2 = opc(o2, e2, memPrev); v1 = opc(o1, e1, m1); ch.mem = v1; out = opc(o3, e3, v2); break;
      case 1: v2 = opc(o2, e2, memPrev); v1 = opc(o1, e1, 0); ch.mem = m1 + v1; out = opc(o3, e3, v2); break;
      case 2: v2 = opc(o2, e2, memPrev); v1 = opc(o1, e1, 0); ch.mem = v1; out = opc(o3, e3, m1 + v2); break;
      case 3: v2 = opc(o2, e2, 0); v1 = opc(o1, e1, m1); out = opc(o3, e3, v2 + memPrev); ch.mem = v1; break;
      case 4: v2 = opc(o2, e2, 0); v1 = opc(o1, e1, m1); v3 = opc(o3, e3, v2); out = v1 + v3; break;
      case 5: v2 = opc(o2, e2, memPrev); v1 = opc(o1, e1, m1); v3 = opc(o3, e3, m1); ch.mem = m1; out = v1 + v2 + v3; break;
      case 6: v2 = opc(o2, e2, 0); v1 = opc(o1, e1, m1); v3 = opc(o3, e3, 0); out = v1 + v2 + v3; break;
      default: out = m1 + opc(o1, e1, 0) + opc(o2, e2, 0) + opc(o3, e3, 0); break;
    }
    return out;
  }

  // Avanza una muestra nativa. Deja el resultado en outL / outR (enteros ~±49152).
  tick() {
    if (++this.egTimer >= 3) {
      this.egTimer = 0;
      this.egCnt = (this.egCnt + 1) & 0xFFF || 1;
      for (let c = 0; c < 6; c++) {
        const ops = this.ch[c].ops;
        this._eg(ops[0]); this._eg(ops[1]); this._eg(ops[2]); this._eg(ops[3]);
      }
    }
    if (this.lfoEn && ++this.lfoTimer >= LFO_STEPS[this.lfoFreq]) {
      this.lfoTimer = 0;
      this.lfoCnt = (this.lfoCnt + 1) & 127;
      const p = this.lfoCnt;
      this.lfoAM = p < 64 ? (p ^ 63) << 1 : (p & 63) << 1;
      this._setPM(p >> 2);
    }
    let L = 0, R = 0;
    for (let c = 0; c < 6; c++) {
      const ch = this.ch[c];
      let out = (c === 5 && this.dacEn) ? this.dacOut : this._chan(ch);
      if (out > 8191) out = 8191; else if (out < -8192) out = -8192;
      if (ch.panL) L += out;
      if (ch.panR) R += out;
      const a = out < 0 ? -out : out;
      if (a > this.levels[c]) this.levels[c] = a;
      const ops = ch.ops;
      ops[0].phase = (ops[0].phase + ops[0].incr) | 0;
      ops[1].phase = (ops[1].phase + ops[1].incr) | 0;
      ops[2].phase = (ops[2].phase + ops[2].incr) | 0;
      ops[3].phase = (ops[3].phase + ops[3].incr) | 0;
    }
    this.outL = L; this.outR = R;
  }
}

function opc(op, eg, pm) {
  if (eg >= ENV_QUIET) return 0;
  const p = (eg << 3) + SIN_TAB[(((op.phase & ~0xFFFF) + (pm << 15)) >> 16) & SIN_MASK];
  return p >= TL_TAB_LEN ? 0 : TL_TAB[p];
}
