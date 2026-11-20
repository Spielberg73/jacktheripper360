// Worker que analiza ROMs de Mega Drive sin bloquear la interfaz
import { parseRom, crc32 } from './md/megadrive.js';
import { scanRom, snapshotAt } from './md/scan.js';

self.onmessage = e => {
  const m = e.data;
  try {
    const info = parseRom(new Uint8Array(m.rom));
    let result;
    if (m.type === 'info') result = { crc: crc32(info.rom), title: info.title, isMD: info.isMD, region: info.region };
    else if (m.type === 'scan') {
      const res = scanRom(info, p => self.postMessage({ type: 'progress', id: m.id, p }));
      result = { ...res, crc: crc32(info.rom), title: info.title };
    } else if (m.type === 'snapshot') result = snapshotAt(info, m.frame);
    self.postMessage({ type: 'done', id: m.id, result });
  } catch (err) {
    self.postMessage({ type: 'error', id: m.id, message: String(err && err.message || err) });
  }
};
