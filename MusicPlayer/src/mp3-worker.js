// Worker de audio: codifica a MP3 (lamejs) la radio que se graba y exporta a MP3/WAV
// las canciones de VGM y de ROMs de Mega Drive (las renderiza con los mismos emuladores).
importScripts('vendor/lame.min.js');

let enc = null, parts = [];

function toI16(f) {
  const o = new Int16Array(f.length);
  for (let i = 0; i < f.length; i++) { const v = Math.max(-1, Math.min(1, f[i])); o[i] = v < 0 ? v * 32768 : v * 32767; }
  return o;
}
function encodeChunk(encoder, out, L, R) {
  const l = toI16(L), r = toI16(R);
  for (let i = 0; i < l.length; i += 1152) {
    const b = encoder.encodeBuffer(l.subarray(i, i + 1152), r.subarray(i, i + 1152));
    if (b.length) out.push(new Uint8Array(b));
  }
}
function wavBlob(chunksL, chunksR, rate) {
  let n = 0; for (const c of chunksL) n += c.length;
  const buf = new ArrayBuffer(44 + n * 4), v = new DataView(buf);
  const str = (o, s) => { for (let i = 0; i < s.length; i++) v.setUint8(o + i, s.charCodeAt(i)); };
  str(0, 'RIFF'); v.setUint32(4, 36 + n * 4, true); str(8, 'WAVEfmt '); v.setUint32(16, 16, true); v.setUint16(20, 1, true); v.setUint16(22, 2, true);
  v.setUint32(24, rate, true); v.setUint32(28, rate * 4, true); v.setUint16(32, 4, true); v.setUint16(34, 16, true); str(36, 'data'); v.setUint32(40, n * 4, true);
  let o = 44;
  for (let k = 0; k < chunksL.length; k++) {
    const L = toI16(chunksL[k]), R = toI16(chunksR[k]);
    for (let i = 0; i < L.length; i++) { v.setInt16(o, L[i], true); v.setInt16(o + 2, R[i], true); o += 4; }
  }
  return new Blob([buf], { type: 'audio/wav' });
}

async function exportTrack(m) {
  const rate = 44100, block = 4410;
  let player, total;
  if (m.kind === 'vgm') {
    const { VgmPlayer } = await import('./chips/vgm-core.js');
    player = new VgmPlayer(rate);
    player.load(new Uint8Array(m.data));
    total = player.h.duration;
  } else {
    const { MdPlayer } = await import('./md/md-player.js');
    player = new MdPlayer(rate);
    player.load({ ...m.cfg, rom: new Uint8Array(m.cfg.rom) });
    total = m.cfg.duration;
  }
  const encoder = m.format === 'mp3' ? new lamejs.Mp3Encoder(2, rate, 192) : null;
  const out = [], wl = [], wr = [];
  let t = 0, lastPct = -1;
  for (;;) {
    const L = new Float32Array(block), R = new Float32Array(block);
    const alive = player.render(L, R);
    if (encoder) encodeChunk(encoder, out, L, R); else { wl.push(L); wr.push(R); }
    t += block / rate;
    const pct = Math.min(99, Math.floor(t / total * 100));
    if (pct !== lastPct) { lastPct = pct; self.postMessage({ type: 'progress', id: m.id, p: pct / 100 }); }
    if (!alive || t >= total + 1) break;
  }
  if (encoder) { out.push(new Uint8Array(encoder.flush())); return new Blob(out, { type: 'audio/mpeg' }); }
  return wavBlob(wl, wr, rate);
}

self.onmessage = async e => {
  const m = e.data;
  try {
    if (m.type === 'start') { enc = new lamejs.Mp3Encoder(2, m.sampleRate, m.kbps || 192); parts = []; }
    else if (m.type === 'pcm') { if (enc) encodeChunk(enc, parts, m.l, m.r); }
    else if (m.type === 'end') {
      if (enc) parts.push(new Uint8Array(enc.flush()));
      const blob = new Blob(parts, { type: 'audio/mpeg' });
      enc = null; parts = [];
      self.postMessage({ type: 'done', id: m.id, blob });
    } else if (m.type === 'export') {
      const blob = await exportTrack(m);
      self.postMessage({ type: 'done', id: m.id, blob });
    }
  } catch (err) {
    self.postMessage({ type: 'error', id: m.id, message: String(err && err.message || err) });
  }
};
