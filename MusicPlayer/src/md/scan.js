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

// Aprende cómo pide música el juego. Devuelve plantillas de "transacción":
// { frame, writes: [[addr, valor], ...], vars: [posiciones con el nº de canción] }
export function learn(info, maxFrames = 1800) {
  const md = new MegaDrive(info);
  md.fast = true;
  const log = [];
  const perFrame = new Map();
  const consumed = new Set();       // direcciones que el Z80 vuelve a escribir (consume el comando)
  const lastByAddr = new Map();
  let firstKeyon = -1, keyonFrom68k = false;
  const bursts = new Map(); // frame -> escrituras en orden (para cargas de driver)
  md.onZ80Write = (addr, v, inReset) => {
    perFrame.set(md.frame, (perFrame.get(md.frame) || 0) + 1);
    let b = bursts.get(md.frame); if (!b) bursts.set(md.frame, b = []);
    if (b.length < 20000) b.push([addr, v]);
    if (inReset || log.length > 50000) return;
    log.push({ f: md.frame, addr, v });
    lastByAddr.set(addr, md.frame);
  };
  md.onZWriteByZ80 = addr => { const f = lastByAddr.get(addr); if (f !== undefined && md.frame - f <= 60) consumed.add(addr); };
  md.onYMWrite = (part, reg, v, from68k) => {
    if (firstKeyon < 0 && part === 0 && reg === 0x28 && (v & 0xF0)) { firstKeyon = md.frame; keyonFrom68k = from68k; }
  };
  let txCount = 0;
  while (md.frame < maxFrames) {
    md.runFrames(1);
    if (firstKeyon >= 0 && md.frame > firstKeyon + 600 && txCount >= 3) break;
    if (md.frame % 60 === 0) txCount = transactions(log, perFrame).length;
  }
  if (firstKeyon < 0 || keyonFrom68k) return { mode: 'free', reason: firstKeyon < 0 ? 'sin música' : 'driver en el 68000' };

  const txs = transactions(log, perFrame);
  const templates = [];
  for (let j = 0; j < txs.length; j++) {
    for (let i = 0; i < j; i++) {
      const vars = varPositions(txs[i].writes, txs[j].writes);
      if (vars) { templates.push({ frame: txs[j].f, writes: txs[j].writes, vars }); templates.push({ frame: txs[i].f, writes: txs[i].writes, vars }); break; }
    }
  }
  // buzón sencillo de un byte (la petición solo se ha visto una vez)
  for (const t of txs) if (t.writes.length <= 2) {
    const k = t.writes.findIndex(([a, v]) => v !== 0 && consumed.has(a));
    if (k >= 0) templates.push({ frame: t.f, writes: t.writes, vars: [k] });
  }
  // parámetros escritos justo después de subir el driver (p. ej. Sound Images):
  // se re-escriben direcciones ya cargadas en el mismo frame y luego arranca el Z80
  for (const [f, b] of bursts) {
    if (b.length < 1024) continue;
    const firstIdx = new Map();
    b.forEach(([a], i) => { if (!firstIdx.has(a)) firstIdx.set(a, i); });
    const params = b.filter(([a], i) => firstIdx.get(a) !== i);
    if (!params.length || params.length > 16) continue;
    params.forEach(([, v], i) => templates.push({ frame: f + 1, writes: params, vars: [i], restart: true }));
  }
  const seen = new Set();
  const uniq = templates.filter(t => { const k = t.frame + ':' + t.vars; if (seen.has(k)) return false; seen.add(k); return true; });
  if (!uniq.length) return { mode: 'free', reason: 'sin buzón' };
  return { mode: 'z80', templates: uniq, firstKeyon };
}

// Agrupa por frame las escrituras del 68000 a la RAM del Z80, quitando cargas masivas
// y "latidos" (direcciones escritas muchas veces con uno o dos valores siempre iguales).
function transactions(log, perFrame) {
  const stats = new Map();
  for (const w of log) {
    let s = stats.get(w.addr);
    if (!s) stats.set(w.addr, s = { frames: new Set(), vals: new Set() });
    s.frames.add(w.f); s.vals.add(w.v);
  }
  const noise = a => { const s = stats.get(a); return s.frames.size >= 8 && s.vals.size <= 2; };
  const byFrame = new Map();
  for (const w of log) {
    if ((perFrame.get(w.f) || 0) > 64 || noise(w.addr)) continue;
    if (!byFrame.has(w.f)) byFrame.set(w.f, []);
    byFrame.get(w.f).push([w.addr, w.v]);
  }
  return [...byFrame].map(([f, writes]) => ({ f, writes }));
}

// Posiciones que cambian entre dos peticiones con la misma forma = número de canción.
function varPositions(a, b) {
  if (a.length !== b.length || !a.length) return null;
  const ring = [], same = [];
  for (let i = 0; i < a.length; i++) {
    if (a[i][1] === b[i][1]) continue;
    (a[i][0] === b[i][0] ? same : ring).push(i);
  }
  // en una cola circular los datos cambian de dirección y los punteros no
  if (ring.length) {
    const vals = new Set(ring.map(i => b[i][1]));
    return vals.size === 1 ? ring : null;
  }
  if (same.length === 1) return same;
  return null;
}

export function snapshotAt(info, frame) {
  const md = new MegaDrive(info);
  md.fast = true;
  md.runFrames(frame);
  return md.save();
}

export function applyTemplate(md, tpl, value) {
  tpl.writes.forEach(([addr, v], i) => { md.zram[addr] = tpl.vars.includes(i) ? value : v; });
  if (tpl.restart) { // el driver lee el parámetro al arrancar: lo reiniciamos
    md.z80.reset(); md.ym.reset(); md.psg.reset(); md.ymAddr = [0, 0];
    md.z80Reset = false;
  }
}

function hash(str) { let h = 5381; for (let i = 0; i < str.length; i++) h = ((h << 5) + h + str.charCodeAt(i)) | 0; return (h >>> 0).toString(36); }

export function probe(info, snap, tpl, value, frames = 360) {
  const md = new MegaDrive(info);
  md.load(snap);
  md.frozen68k = true; md.z80BusReq = false; md.fast = true;
  applyTemplate(md, tpl, value);
  let keyons = 0, first = -1, last = -1, dac = 0, ymw = 0;
  const chans = new Set();
  const fp = [];
  const start = md.frame;
  md.onYMWrite = (part, reg, v) => {
    const f = md.frame - start;
    ymw++;
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
  return { value, kind, keyons, ymw, channels: chans.size, first, last, length: span / FPS, fp: hash(fp.join(',')) };
}

// Escaneo completo. onProgress(0..1)
export function scanRom(info, onProgress = () => {}) {
  const t0 = Date.now();
  const l = learn(info);
  onProgress(0.05);
  if (l.mode !== 'z80') return { ...l, tracks: [], ms: Date.now() - t0 };
  // elegir la plantilla: la primera a la que el driver reacciona; si hay varias
  // candidatas de arranque, la que da más canciones distintas en una prueba corta
  let tpl = null, snap = null, best = -1;
  for (const t of l.templates.slice(0, 8)) {
    const s = snapshotAt(info, t.frame);
    if (t.restart) {
      const fps = new Set();
      for (let v = 0; v < 12; v++) { const r = probe(info, s, t, v, 150); if (r.keyons >= 8) fps.add(r.fp); }
      if (fps.size > best) { best = fps.size; tpl = t; snap = s; }
      continue;
    }
    if (tpl) break;
    const r = probe(info, s, t, t.writes[t.vars[0]][1], 120);
    if (r.kind !== 'none' || r.ymw > 100) { tpl = t; snap = s; break; }
  }
  if (tpl && tpl.restart && best < 1) tpl = null;
  if (!tpl) return { mode: 'free', reason: 'peticiones no reconocidas', tracks: [], ms: Date.now() - t0 };
  onProgress(0.1);
  const seen = new Set();
  const tracks = [];
  for (let v = 0; v <= 255; v++) {
    const r = probe(info, snap, tpl, v);
    if ((r.kind === 'song' || r.kind === 'jingle') && !seen.has(r.fp)) {
      seen.add(r.fp);
      tracks.push({ value: v, kind: r.kind, length: r.kind === 'jingle' ? Math.ceil(r.length + 2) : 0, channels: r.channels });
    }
    if (v % 8 === 0) onProgress(0.1 + 0.9 * v / 255);
  }
  onProgress(1);
  if (!tracks.length) return { mode: 'free', reason: 'sin canciones', tracks: [], ms: Date.now() - t0 };
  return { mode: 'z80', template: { frame: tpl.frame, writes: tpl.writes, vars: tpl.vars, restart: !!tpl.restart }, frame: tpl.frame, tracks, ms: Date.now() - t0 };
}
