// NightDrive Player — ROMs de Mega Drive: análisis (en un Worker) y caché de resultados
(function () {
  const ROM_RE = /\.(md|gen|smd|bin)$/i;
  let worker = null, seq = 0;
  const pending = new Map();
  const snaps = new Map();

  function getWorker() {
    if (worker) return worker;
    worker = new Worker('rom-worker.js', { type: 'module' });
    worker.onmessage = e => {
      const m = e.data, p = pending.get(m.id);
      if (!p) return;
      if (m.type === 'progress') { if (p.onProgress) p.onProgress(m.p); return; }
      pending.delete(m.id);
      m.type === 'error' ? p.reject(new Error(m.message)) : p.resolve(m.result);
    };
    worker.onerror = e => { for (const p of pending.values()) p.reject(new Error(e.message || 'worker')); pending.clear(); worker = null; };
    return worker;
  }
  function call(type, bytes, extra = {}, onProgress) {
    const id = ++seq;
    const rom = bytes.slice().buffer;
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject, onProgress });
      getWorker().postMessage({ type, id, rom, ...extra }, [rom]);
    });
  }

  const cacheKey = crc => 'ndrom:v1:' + crc;
  function cached(crc) { try { return JSON.parse(localStorage.getItem(cacheKey(crc)) || 'null'); } catch { return null; } }
  function store(crc, res) { try { localStorage.setItem(cacheKey(crc), JSON.stringify(res)); } catch { } }

  // Devuelve { crc, title, isMD, mode, mailbox, value, frame, tracks[] }
  async function scan(bytes, onProgress) {
    const info = await call('info', bytes);
    if (!info.isMD) return { ...info, mode: 'none', tracks: [] };
    const hit = cached(info.crc);
    if (hit) return hit;
    const res = await call('scan', bytes, {}, onProgress);
    const out = { crc: res.crc, title: res.title, isMD: true, mode: res.mode, mailbox: res.mailbox, value: res.value, frame: res.frame, reason: res.reason, tracks: res.tracks };
    store(info.crc, out);
    return out;
  }

  // Estado de la consola justo antes de la petición de música (se guarda en memoria)
  async function snapshot(bytes, rom) {
    const key = rom.crc + ':' + rom.frame;
    if (!snaps.has(key)) snaps.set(key, call('snapshot', bytes, { frame: rom.frame }));
    try { return await snaps.get(key); } catch (e) { snaps.delete(key); throw e; }
  }

  window.NDRom = { isRom: n => ROM_RE.test(n || ''), scan, snapshot };
})();
