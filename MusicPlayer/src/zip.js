// NightDrive Player — lector de archivos ZIP (stored + deflate)
(function () {
  const td = new TextDecoder('utf-8');
  const cp437 = new TextDecoder('ibm866'); // aproximación para nombres sin flag UTF-8

  function u16(d, o) { return d[o] | (d[o + 1] << 8); }
  function u32(d, o) { return (d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | (d[o + 3] << 24)) >>> 0; }

  // Devuelve [{ name, method, compSize, size, offset }]
  function list(bytes) {
    const d = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    let eocd = -1;
    for (let i = d.length - 22; i >= Math.max(0, d.length - 65557); i--) {
      if (u32(d, i) === 0x06054B50) { eocd = i; break; }
    }
    if (eocd < 0) throw new Error('ZIP no válido');
    const count = u16(d, eocd + 10);
    let p = u32(d, eocd + 16);
    const out = [];
    for (let i = 0; i < count && p + 46 <= d.length; i++) {
      if (u32(d, p) !== 0x02014B50) break;
      const flags = u16(d, p + 8), method = u16(d, p + 10);
      const compSize = u32(d, p + 20), size = u32(d, p + 24);
      const nLen = u16(d, p + 28), xLen = u16(d, p + 30), cLen = u16(d, p + 32);
      const local = u32(d, p + 42);
      const raw = d.subarray(p + 46, p + 46 + nLen);
      const name = (flags & 0x800) ? td.decode(raw) : cp437.decode(raw);
      if (!name.endsWith('/')) out.push({ name, method, compSize, size, local, encrypted: !!(flags & 1) });
      p += 46 + nLen + xLen + cLen;
    }
    return out;
  }

  async function extract(bytes, entry) {
    const d = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
    if (entry.encrypted) throw new Error('ZIP cifrado');
    const lp = entry.local;
    if (u32(d, lp) !== 0x04034B50) throw new Error('Entrada ZIP dañada');
    const start = lp + 30 + u16(d, lp + 26) + u16(d, lp + 28);
    const comp = d.subarray(start, start + entry.compSize);
    if (entry.method === 0) return comp.slice();
    if (entry.method !== 8) throw new Error('Compresión ZIP no soportada (' + entry.method + ')');
    const ds = new DecompressionStream('deflate-raw');
    const buf = await new Response(new Blob([comp]).stream().pipeThrough(ds)).arrayBuffer();
    return new Uint8Array(buf);
  }

  async function extractByName(bytes, name) {
    const e = list(bytes).find(x => x.name === name);
    if (!e) throw new Error('No está en el ZIP: ' + name);
    return extract(bytes, e);
  }

  window.NDZip = { list, extract, extractByName, isZip: n => /\.zip$/i.test(n || '') };
})();
