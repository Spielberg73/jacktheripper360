// Descubre las canciones de una ROM de Mega Drive.
//
// 1. Arranca el juego sin vídeo y observa cómo el 68000 le pide música al driver
//    de sonido del Z80: escribe un número en una posición de la RAM del Z80 (el
//    "buzón") y poco después empiezan a sonar notas FM.
// 2. Guarda el estado de la consola justo antes de esa petición.
// 3. Desde ese estado, con el 68000 parado, prueba cada valor 0x01-0xFF en el buzón
//    y escucha (sin sintetizar) qué hace el driver: si suenan notas en varios
//    canales durante segundos es una canción; si dura poco, un jingle; si no, nada.
import { MegaDrive } from './megadrive.js';

const FPS = 60;

export function learn(info, maxFrames = 1800) {
  const md = new MegaDrive(info);
  md.fast = true;
  const writes = [];
  const perFrame = new Map();
  const pendingByAddr = new Map();
  let firstKeyon = -1, keyonFrom68k = false;
  md.onZ80Write = (addr, v, inReset) => {
    perFrame.set(md.frame, (perFrame.get(md.frame) || 0) + 1);
    if (inReset || writes.length > 50000) return;
    const w = { f: md.frame, addr, v, consumed: false };
    writes.push(w);
    const l = pendingByAddr.get(addr); if (l) l.push(w); else pendingByAddr.set(addr, [w]);
  };
  md.onZWriteByZ80 = addr => {
    const l = pendingByAddr.get(addr);
    if (!l) return;
    for (const w of l) if (md.frame - w.f <= 60) w.consumed = true;
    pendingByAddr.delete(addr);
  };
  md.onYMWrite = (part, reg, v, from68k) => {
    if (firstKeyon < 0 && part === 0 && reg === 0x28 && (v & 0xF0)) { firstKeyon = md.frame; keyonFrom68k = from68k; }
  };
  while (md.frame < maxFrames) {
    md.runFrames(1);
    if (firstKeyon >= 0 && md.frame > firstKeyon + 30) break;
  }
  if (firstKeyon < 0 || keyonFrom68k) return { mode: 'free', reason: firstKeyon < 0 ? 'sin música' : 'driver en el 68000' };
  const isBurst = w => (perFrame.get(w.f) || 0) > 24;
  const win = writes.filter(w => w.f <= firstKeyon && w.f >= firstKeyon - 240 && !isBurst(w) && w.v !== 0);
  const pick = [...win].reverse().find(w => w.consumed) || win[win.length - 1];
  if (!pick) return { mode: 'free', reason: 'sin buzón' };
  return { mode: 'z80', mailbox: pick.addr, value: pick.v, frame: pick.f, firstKeyon };
}

export function snapshotAt(info, frame) {
  const md = new MegaDrive(info);
  md.fast = true;
  md.runFrames(frame);
  return md.save();
}

function hash(str) { let h = 5381; for (let i = 0; i < str.length; i++) h = ((h << 5) + h + str.charCodeAt(i)) | 0; return (h >>> 0).toString(36); }

export function probe(info, snap, mailbox, value, frames = 360) {
  const md = new MegaDrive(info);
  md.load(snap);
  md.frozen68k = true; md.z80BusReq = false; md.fast = true;
  md.zram[mailbox] = value;
  let keyons = 0, first = -1, last = -1, dac = 0;
  const chans = new Set();
  const fp = [];
  const start = md.frame;
  md.onYMWrite = (part, reg, v) => {
    const f = md.frame - start;
    if (part === 0 && reg === 0x28 && (v & 0xF0)) {
      const ch = (v & 3) + ((v & 4) ? 3 : 0);
      keyons++; chans.add('F' + ch);
      if (first < 0) first = f;
      last = f;
      if (fp.length < 96) fp.push(ch + ':' + f);
    } else if (reg >= 0xA0 && reg < 0xA8 && fp.length < 96) fp.push('n' + reg.toString(16) + v);
    else if (part === 0 && reg === 0x2A) { if (++dac % 400 === 0) { chans.add('DAC'); if (first < 0) first = f; last = f; } }
  };
  const psgWrite = md.psg.write.bind(md.psg);
  md.psg.write = b => {
    psgWrite(b);
    if ((b & 0x90) === 0x90 && (b & 15) < 15) { // volumen audible en un canal PSG
      const f = md.frame - start;
      chans.add('P' + ((b >> 5) & 3)); if (first < 0) first = f; last = f; keyons++;
      if (fp.length < 96) fp.push('p' + b);
    }
  };
  while (md.frame - start < frames) {
    md.runFrames(1);
    const f = md.frame - start;
    if (f === 45 && first < 0) break; // nada en 0,75 s: no es una canción
  }
  const span = last - first;
  let kind = 'none';
  if (keyons >= 16 && chans.size >= 2 && last >= frames - 60) kind = 'song';
  else if (keyons >= 8 && chans.size >= 2 && span >= 90) kind = 'jingle';
  return { value, kind, keyons, channels: chans.size, first, last, length: span / FPS, fp: hash(fp.join(',')) };
}

// Escaneo completo. onProgress(0..1)
export function scanRom(info, onProgress = () => {}) {
  const t0 = Date.now();
  const l = learn(info);
  onProgress(0.1);
  if (l.mode !== 'z80') return { ...l, tracks: [], ms: Date.now() - t0 };
  const snap = snapshotAt(info, l.frame);
  const seen = new Set();
  const tracks = [];
  for (let v = 1; v <= 255; v++) {
    const r = probe(info, snap, l.mailbox, v);
    if ((r.kind === 'song' || r.kind === 'jingle') && !seen.has(r.fp)) {
      seen.add(r.fp);
      tracks.push({ value: v, kind: r.kind, length: r.kind === 'jingle' ? Math.ceil(r.length + 2) : 0, channels: r.channels });
    }
    if (v % 8 === 0) onProgress(0.1 + 0.9 * v / 255);
  }
  onProgress(1);
  if (!tracks.length) return { mode: 'free', reason: 'sin canciones', tracks: [], ms: Date.now() - t0 };
  return { ...l, tracks, ms: Date.now() - t0 };
}
