// NightDrive Player — lógica de la aplicación
(function () {
  const $ = s => document.querySelector(s);
  const $$ = s => [...document.querySelectorAll(s)];
  const nd = window.nd || null;              // API de Electron (null en navegador)
  const { AudioEngine, PRESETS } = window.NDAudio;

  // ---------------- frontales (skins) ----------------
  const SKINS = [
    { id: 'stealth', name: 'STEALTH RED', desc: 'Negro piano, teclas rojas, VFD ámbar',
      plate: 'linear-gradient(180deg,#2c2c31 0%,#101013 46%,#050506 100%)', edge: '#000', illum: '#ff2a2a', disp: ['#ffb300', '#ff5a00'], brand: '#e9ebef', key: ['#2b2d33', '#0e0f12'] },
    { id: 'arctic', name: 'ARCTIC BLUE', desc: 'Aluminio cepillado, iluminación azul hielo',
      plate: 'repeating-linear-gradient(90deg,rgba(255,255,255,.05) 0 1px,rgba(0,0,0,.05) 1px 3px),linear-gradient(180deg,#cfd4dc 0%,#959ba5 48%,#5d636c 100%)', edge: '#2a2d33', illum: '#2fb4ff', disp: ['#bfe9ff', '#2f8fff'], brand: '#1a1d22', key: ['#3a3f48', '#16181c'], light: true },
    { id: 'carbon', name: 'CARBON GREEN', desc: 'Fibra de carbono, LCD verde tuning',
      plate: 'radial-gradient(#000 15%,transparent 16%) 0 0/12px 12px,radial-gradient(#000 15%,transparent 16%) 6px 6px/12px 12px,radial-gradient(rgba(255,255,255,.09) 15%,transparent 20%) 0 1px/12px 12px,radial-gradient(rgba(255,255,255,.09) 15%,transparent 20%) 6px 7px/12px 12px,#242424', edge: '#000', illum: '#39ff6a', disp: ['#a6ff4d', '#19d86b'], brand: '#e9ebef', key: ['#2a2c2f', '#0d0e0f'] },
    { id: 'rainbow', name: 'NEON RAINBOW', desc: 'Iluminación multicolor que cambia sola',
      plate: 'linear-gradient(180deg,#26262b 0%,#0c0c0f 50%,#040405 100%)', edge: '#000', illum: '#ff00aa', disp: ['#ffffff', '#ffffff'], brand: '#e9ebef', key: ['#2b2d33', '#0e0f12'], rainbow: true },
    { id: 'titanium', name: 'TITANIUM ORANGE', desc: 'Titanio pistola, todo naranja',
      plate: 'linear-gradient(180deg,#666b73 0%,#33373d 52%,#1b1d21 100%)', edge: '#0b0c0e', illum: '#ff7a00', disp: ['#ffa640', '#ff5a00'], brand: '#f2f3f5', key: ['#3a3d43', '#18191c'] },
    { id: 'violet', name: 'VIOLET NIGHT', desc: 'Cromo violeta, teclas magenta, pantalla cian',
      plate: 'linear-gradient(160deg,#3a1d57 0%,#160b26 55%,#07040c 100%)', edge: '#000', illum: '#ff3df2', disp: ['#7ff6ff', '#2ab8ff'], brand: '#f0e6ff', key: ['#2e2338', '#100b16'] },
    { id: 'gold', name: 'CHAMPAGNE GOLD', desc: 'Edición dorada, iluminación ámbar',
      plate: 'repeating-linear-gradient(90deg,rgba(255,255,255,.05) 0 1px,rgba(0,0,0,.04) 1px 3px),linear-gradient(180deg,#e6d3a3 0%,#b49a5e 50%,#6f5a2c 100%)', edge: '#2b2210', illum: '#ffb000', disp: ['#ffd36b', '#ff8a00'], brand: '#2a2112', key: ['#3b3426', '#15120c'], light: true },
    { id: 'ice', name: 'PEARL WHITE', desc: 'Blanco perla con OEL blanco-azul',
      plate: 'linear-gradient(180deg,#ffffff 0%,#dfe3ea 50%,#a9b0bc 100%)', edge: '#6b717c', illum: '#4d7cff', disp: ['#ffffff', '#7fb2ff'], brand: '#20242c', key: ['#40444c', '#1a1c20'], light: true }
  ];
  const DISP_COLORS = [
    { id: 'auto', name: 'FRONTAL' }, { id: 'amber', name: 'ÁMBAR', c: ['#ffb300', '#ff5a00'] },
    { id: 'blue', name: 'AZUL', c: ['#bfe9ff', '#2f8fff'] }, { id: 'green', name: 'VERDE', c: ['#a6ff4d', '#19d86b'] },
    { id: 'red', name: 'ROJO', c: ['#ff6b5a', '#ff1a1a'] }, { id: 'white', name: 'BLANCO', c: ['#ffffff', '#cfe3ff'] },
    { id: 'cyan', name: 'CIAN', c: ['#7ff6ff', '#00c2ff'] }, { id: 'rainbow', name: 'ARCOÍRIS', rainbow: true }
  ];

  // ---------------- estado persistente ----------------
  const DEFAULTS = {
    skin: 'stealth', illAuto: true, illHue: 0, dispColor: 'auto', dimmer: 1, mode: 0,
    volume: 20, bands: 31, gains: null, preset: 'FLAT', preamp: 0, bass: 0, loud: 0, balance: 0, eqOn: true,
    custom: { C1: null, C2: null }, repeat: 0, shuffle: false, playlist: [], current: -1, position: 0, tab: 'eq', onTop: false
  };
  let S;
  try { S = Object.assign({}, DEFAULTS, JSON.parse(localStorage.getItem('nightdrive') || '{}')); } catch { S = { ...DEFAULTS }; }
  let saveTimer = 0;
  function save() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
      try {
        const out = { ...S, playlist: nd ? tracks.filter(t => !t.scanning).map(t => ({ path: t.path, entry: t.entry, zip: t.zip, tagged: t.tagged, rom: t.rom, title: t.title, artist: t.artist, album: t.album, duration: t.duration, name: t.name, codec: t.codec, bitrate: t.bitrate, sampleRate: t.sampleRate, system: t.system })) : [] };
        out.position = engine.el.currentTime || 0;
        out.current = current;
        localStorage.setItem('nightdrive', JSON.stringify(out));
      } catch { /* almacenamiento no disponible */ }
    }, 300);
  }

  // ---------------- motor ----------------
  const engine = new AudioEngine();
  const audio = engine.media;   // <audio> o el emulador VGM, según la pista
  const isVgm = name => window.NDVgm.isVgm(name);
  const display = new window.NDDisplay.DotDisplay($('#display'), engine);
  const eq = new window.NDEQ.GraphicEQ({ canvas: $('#eqGraph'), fadersEl: $('#faders'), engine, onChange: onEqChange });

  let tracks = [];        // {id, path?, file?, name, title, artist, album, duration}
  let current = -1;
  let order = [];         // orden aleatorio
  let selected = new Set();
  let scanTimer = 0;
  let scanMode = false;
  let curUrl = null;
  let uid = 1;
  let filter = '';

  // ---------------- skins ----------------
  function currentSkin() { return SKINS.find(s => s.id === S.skin) || SKINS[0]; }
  function applySkin() {
    const sk = currentSkin();
    const r = document.documentElement.style;
    r.setProperty('--plate', sk.plate);
    r.setProperty('--plate-edge', sk.edge);
    r.setProperty('--brand', sk.brand);
    r.setProperty('--key-a', sk.key[0]);
    r.setProperty('--key-b', sk.key[1]);
    $('#plate').classList.toggle('light-plate', !!sk.light);
    applyIllum();
    applyDisplayColor();
    $$('.skin').forEach(el => el.classList.toggle('on', el.dataset.id === sk.id));
  }
  function illumColor() {
    const sk = currentSkin();
    if (sk.rainbow && S.illAuto) return `hsl(${(performance.now() / 25) % 360},100%,58%)`;
    return S.illAuto ? sk.illum : `hsl(${S.illHue},100%,56%)`;
  }
  function applyIllum() {
    const c = illumColor();
    const r = document.documentElement.style;
    r.setProperty('--illum', c);
    r.setProperty('--illum-soft', c.startsWith('hsl') ? c.replace('hsl(', 'hsla(').replace(')', ',.35)') : c + '59');
    eq.setColors(toHex(c), toHex(dispColors()[0]));
  }
  function dispColors() {
    const dc = DISP_COLORS.find(d => d.id === S.dispColor);
    if (dc && dc.c) return dc.c;
    return currentSkin().disp;
  }
  function applyDisplayColor() {
    const dc = DISP_COLORS.find(d => d.id === S.dispColor);
    const rainbow = (dc && dc.rainbow) || (S.dispColor === 'auto' && currentSkin().rainbow);
    const c = dispColors();
    const r = document.documentElement.style;
    r.setProperty('--disp-a', rainbow ? '#ffffff' : c[0]);
    r.setProperty('--disp-b', rainbow ? '#ff00aa' : c[1]);
    display.setColors(c[0], c[1], rainbow);
    display.brightness = S.dimmer;
    eq.setColors(toHex(illumColor()), toHex(c[0]));
  }
  const _cv = document.createElement('canvas').getContext('2d');
  function toHex(c) { _cv.fillStyle = '#000'; _cv.fillStyle = c; return _cv.fillStyle.startsWith('#') ? _cv.fillStyle : '#ffffff'; }

  function buildSkinPanel() {
    $('#skins').innerHTML = SKINS.map(s => `
      <button class="skin" data-id="${s.id}">
        <div class="skin-prev" style="background:${s.plate};--c:${s.rainbow ? '#ff00aa' : s.illum};--d1:${s.disp[0]};--d2:${s.disp[1]}">
          <div class="sk-knob"></div>
          <div class="sk-disp">${[5, 9, 14, 11, 7, 12, 16, 10, 6, 9, 13, 8, 5, 7].map(h => `<i style="height:${h}px"></i>`).join('')}</div>
          <div class="sk-joy"></div>
        </div>
        <div class="skin-name">${s.name}</div><div class="skin-desc">${s.desc}</div>
      </button>`).join('');
    $$('.skin').forEach(el => el.onclick = () => { S.skin = el.dataset.id; applySkin(); display.flash(currentSkin().name, 'FACEPLATE'); save(); });
    $('#dispChips').innerHTML = DISP_COLORS.map(d => `<button data-id="${d.id}"><i style="background:${d.rainbow ? 'conic-gradient(red,yellow,lime,cyan,blue,magenta,red)' : d.c ? d.c[0] : 'var(--illum)'}"></i>${d.name}</button>`).join('');
    $$('#dispChips button').forEach(b => b.onclick = () => { S.dispColor = b.dataset.id; applyDisplayColor(); refreshChips(); save(); });
    $('#modeChips').innerHTML = window.NDDisplay.MODES.map((m, i) => `<button data-i="${i}">${m}</button>`).join('');
    $$('#modeChips button').forEach(b => b.onclick = () => setMode(+b.dataset.i));
    $('#illAuto').checked = S.illAuto;
    $('#illHue').value = S.illHue;
    $('#illAuto').onchange = e => { S.illAuto = e.target.checked; applyIllum(); save(); };
    $('#illHue').oninput = e => { S.illHue = +e.target.value; S.illAuto = false; $('#illAuto').checked = false; applyIllum(); save(); };
    $('#dimmer').value = S.dimmer;
    $('#dimmer').oninput = e => { S.dimmer = +e.target.value; display.brightness = S.dimmer; save(); };
    refreshChips();
  }
  function refreshChips() {
    $$('#dispChips button').forEach(b => b.classList.toggle('on', b.dataset.id === S.dispColor));
    $$('#modeChips button').forEach(b => b.classList.toggle('on', +b.dataset.i === display.mode));
  }
  function setMode(i) {
    display.setMode(i);
    S.mode = display.mode;
    $('#iconMode').textContent = window.NDDisplay.MODES[display.mode];
    refreshChips(); save();
  }

  // ---------------- EQ ----------------
  const PRESET_NAMES = [...Object.keys(PRESETS), 'C1', 'C2'];
  function buildPresets() {
    $('#presets').innerHTML = PRESET_NAMES.map(p => `<button data-p="${p}">${p === 'C1' ? 'CUSTOM 1' : p === 'C2' ? 'CUSTOM 2' : p}</button>`).join('');
    $$('#presets button').forEach(b => b.onclick = () => selectPreset(b.dataset.p));
  }
  function selectPreset(name, quiet) {
    if (name === 'C1' || name === 'C2') {
      const c = S.custom[name];
      if (!c) { display.flash(name === 'C1' ? 'CUSTOM 1' : 'CUSTOM 2', 'VACÍO'); return; }
      eq.applyPreset(c);
    } else eq.applyPreset(PRESETS[name]);
    S.preset = name;
    markPreset();
    if (!quiet) display.flash('EQ ' + (name === 'C1' ? 'CUSTOM 1' : name === 'C2' ? 'CUSTOM 2' : name), 'GRAPHIC EQ');
    save();
  }
  function markPreset() {
    $$('#presets button').forEach(b => b.classList.toggle('on', b.dataset.p === S.preset));
    updateIcons();
  }
  function onEqChange(kind) {
    if (kind === 'band') { S.preset = 'CUSTOM'; markPreset(); }
    S.gains = eq.gains.slice();
    S.bands = eq.freqs.length;
    eq.markDirty();
    save();
  }
  function curvePoints() { return eq.freqs.map((f, i) => [f, eq.gains[i]]); }
  function setEqBypass(on) {
    S.eqOn = on;
    eq.bypassGains = on ? null : eq.gains.slice();
    engine.bands.forEach((b, i) => engine.setBandGain(i, on ? eq.gains[i] : 0));
    engine.setBassBoost(on ? S.bass : 0);
    engine.setLoudness(on ? S.loud : 0);
    segOn('#segEqOn', on ? 1 : 0);
    eq.markDirty();
    setTimeout(() => eq.markDirty(), 120);
    updateIcons();
    save();
  }
  function segOn(sel, v) { $$(sel + ' button').forEach(b => b.classList.toggle('on', +b.dataset.v === +v)); }

  function bindEqControls() {
    const fmtDb = v => (v > 0 ? '+' : '') + v + ' dB';
    const pre = $('#rPre'), bass = $('#rBass'), bal = $('#rBal');
    pre.oninput = () => { S.preamp = +pre.value; engine.setPreamp(S.preamp); $('#oPre').textContent = fmtDb(S.preamp); eq.markDirty(); save(); };
    bass.oninput = () => { S.bass = +bass.value; if (S.eqOn) engine.setBassBoost(S.bass); $('#oBass').textContent = fmtDb(S.bass); setTimeout(() => eq.markDirty(), 80); updateIcons(); save(); };
    bal.oninput = () => { S.balance = +bal.value; engine.setBalance(S.balance); $('#oBal').textContent = S.balance === 0 ? 'C' : (S.balance < 0 ? 'L' : 'R') + Math.round(Math.abs(S.balance) * 15); save(); };
    bal.ondblclick = () => { bal.value = 0; bal.oninput(); };
    $$('#segLoud button').forEach(b => b.onclick = () => setLoud(+b.dataset.v));
    $$('#segBands button').forEach(b => b.onclick = () => { eq.setBandCount(+b.dataset.v); segOn('#segBands', b.dataset.v); if (!S.eqOn) setEqBypass(false); });
    $$('#segEqOn button').forEach(b => b.onclick = () => setEqBypass(b.dataset.v === '1'));
    $('#saveC1').onclick = () => { S.custom.C1 = curvePoints(); S.preset = 'C1'; markPreset(); display.flash('CUSTOM 1', 'MEMORIZADO'); save(); };
    $('#saveC2').onclick = () => { S.custom.C2 = curvePoints(); S.preset = 'C2'; markPreset(); display.flash('CUSTOM 2', 'MEMORIZADO'); save(); };
    $('#eqReset').onclick = () => {
      selectPreset('FLAT');
      pre.value = 0; pre.oninput(); bass.value = 0; bass.oninput(); bal.value = 0; bal.oninput(); setLoud(0);
    };
    pre.value = S.preamp; bass.value = S.bass; bal.value = S.balance;
    pre.oninput(); bass.oninput(); bal.oninput();
  }
  function setLoud(v) {
    S.loud = v;
    if (S.eqOn) engine.setLoudness(v);
    segOn('#segLoud', v);
    setTimeout(() => eq.markDirty(), 80);
    updateIcons(); save();
  }

  // ---------------- playlist ----------------
  function parseName(name) {
    const base = name.replace(/\.[^.]+$/, '').replace(/_/g, ' ');
    const m = base.match(/^(?:\d+[\s.\-_]+)?(.+?)\s+-\s+(.+)$/);
    return m ? { artist: m[1].trim(), title: m[2].trim() } : { artist: '', title: base.replace(/^\d+[\s.\-_]+/, '').trim() };
  }
  function basename(p) { return p.split(/[\\/]/).pop(); }

  const AUDIO_RE = /\.(mp3|flac|wav|ogg|oga|m4a|aac|opus|webm|mp4)$/i;
  const PLAYABLE_RE = /\.(mp3|flac|wav|ogg|oga|m4a|aac|opus|webm|mp4|vgm|vgz)$/i;
  const isZip = n => window.NDZip.isZip(n);
  const isRom = n => window.NDRom.isRom(n);

  // ---- lectura de bytes (archivo suelto o dentro de un ZIP) ----
  let zipCache = { key: null, bytes: null };
  async function readSource(src) {
    const key = src.file || src.path;
    if (zipCache.key === key) return zipCache.bytes;
    const bytes = src.file ? new Uint8Array(await src.file.arrayBuffer()) : await nd.readFile(src.path);
    if (isZip(src.file ? src.file.name : src.path)) zipCache = { key, bytes };
    return bytes;
  }
  async function readTrackBytes(t) {
    const base = await readSource(t);
    return t.entry ? window.NDZip.extractByName(base, t.entry) : base;
  }

  // Convierte rutas/archivos en pistas; los ZIP se abren y aportan sus canciones
  async function expandSources(srcs) {
    const out = [];
    for (const src of srcs) {
      const name = src.file ? src.file.name : basename(src.path);
      if (isZip(name)) {
        try {
          const bytes = await readSource(src);
          const entries = window.NDZip.list(bytes).filter(e => PLAYABLE_RE.test(e.name) || isRom(e.name))
            .sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true }));
          for (const e of entries) {
            const n = basename(e.name);
            if (isRom(n)) out.push(romPlaceholder({ ...src, entry: e.name, zip: name }, n));
            else out.push({ id: uid++, ...src, entry: e.name, name: n, zip: name, ...parseName(n), album: name.replace(/\.zip$/i, ''), duration: 0 });
          }
        } catch (err) { console.warn(err); display.flash('ERROR ZIP', name.slice(0, 18)); }
      } else if (isRom(name)) {
        out.push(romPlaceholder(src, name));
      } else if (PLAYABLE_RE.test(name)) {
        out.push({ id: uid++, ...src, name, ...parseName(name), album: '', duration: 0 });
      }
    }
    return out;
  }

  async function addSources(srcs, playFirst) {
    const fresh = await expandSources(srcs);
    if (!fresh.length) { display.flash('SIN AUDIO', 'NADA QUE AÑADIR'); return; }
    const start = tracks.length;
    tracks.push(...fresh);
    renderList();
    display.flash(`+${fresh.length} PISTAS`, 'AÑADIDAS');
    if (playFirst || current < 0) playIndex(start);
    // etiquetas: VGM (GD3) y audio suelto (Electron). El audio dentro de ZIP se etiqueta al reproducirlo.
    for (const t of fresh.filter(t => isVgm(t.name))) {
      try { await applyVgmInfo(t, await readTrackBytes(t)); } catch { /* VGM dañado */ }
    }
    if (nd) {
      const normal = fresh.filter(t => !isVgm(t.name) && !t.entry && t.path && !t.scanning);
      for (let i = 0; i < normal.length; i += 25) {
        const chunk = normal.slice(i, i + 25);
        const tags = await nd.tags(chunk.map(t => t.path));
        chunk.forEach((t, k) => applyTags(t, tags[k]));
        renderList();
      }
    }
    renderList();
    if (tracks[current] && fresh.includes(tracks[current])) updateNowPlaying();
    reshuffle();
    save();
    for (const ph of fresh.filter(t => t.scanning)) await scanRomTrack(ph);
  }

  // ---- ROMs de Mega Drive: se analizan y se convierten en una pista por canción ----
  let pendingPlay = null;
  function romPlaceholder(src, name) {
    return { id: uid++, ...src, name, title: 'Analizando ROM…', artist: 'Mega Drive', album: name, duration: 0, scanning: true };
  }
  function romTrack(src, res, t, i) {
    const base = { id: uid++, path: src.path, file: src.file, entry: src.entry, zip: src.zip, name: src.name, album: res.title, system: 'Mega Drive', codec: 'ROM · 68000 + Z80 + YM2612 + PSG' };
    if (!t) return { ...base, title: res.title + ' (modo libre)', artist: 'Arranque del juego', duration: 180, rom: { crc: res.crc, free: true } };
    const n = String(i + 1).padStart(2, '0');
    return { ...base, title: (t.kind === 'jingle' ? 'Jingle ' : 'Track ') + n, artist: 'Mega Drive · ID ' + t.value.toString(16).toUpperCase() + 'h',
      duration: t.kind === 'jingle' ? t.length : 150, rom: { crc: res.crc, template: res.template, frame: res.frame, value: t.value, jingle: t.kind === 'jingle' } };
  }
  async function scanRomTrack(ph) {
    let lastDraw = 0;
    try {
      const bytes = await readTrackBytes(ph);
      const res = await window.NDRom.scan(bytes, p => {
        ph.title = `Analizando ROM… ${Math.round(p * 100)}%`;
        const now = performance.now();
        if (now - lastDraw > 250) { lastDraw = now; renderList(); display.flash('ANALIZANDO ROM', Math.round(p * 100) + '%', 900); }
      });
      const idx = tracks.indexOf(ph);
      if (idx < 0) return;
      const list = res.mode === 'none' ? [] : res.mode === 'z80' ? res.tracks.map((t, i) => romTrack(ph, res, t, i)) : [romTrack(ph, res, null, 0)];
      const cur = tracks[current];
      tracks.splice(idx, 1, ...list);
      current = cur ? tracks.indexOf(cur) : -1;
      renderList(); reshuffle(); save();
      if (res.mode === 'none') display.flash('NO ES UNA ROM', 'DE MEGA DRIVE');
      else display.flash(res.title.toUpperCase(), res.mode === 'z80' ? `${list.length} CANCIONES` : 'MODO LIBRE', 2500);
      if (pendingPlay === ph) { pendingPlay = null; if (list.length) playIndex(idx); }
    } catch (err) {
      console.error(err);
      const idx = tracks.indexOf(ph);
      if (idx >= 0) { tracks.splice(idx, 1); if (current > idx) current--; renderList(); }
      display.flash('ERROR ROM', String(err.message || err).slice(0, 20).toUpperCase());
    }
  }

  function applyTags(t, m) {
    if (!m) return;
    if (m.title) t.title = m.title;
    if (m.artist) t.artist = m.artist;
    if (m.album) t.album = m.album;
    if (m.duration) t.duration = m.duration;
    t.codec = m.codec; t.bitrate = m.bitrate; t.sampleRate = m.sampleRate;
  }
  async function applyVgmInfo(t, bytes) {
    const m = await window.NDVgm.info(bytes);
    if (m.title) t.title = m.title;
    if (m.artist) t.artist = m.artist;
    if (m.album) t.album = m.album;
    t.duration = m.duration;
    t.codec = m.codec;
    t.system = m.system;
  }
  function addPaths(paths, playFirst) {
    if (!paths || !paths.length) return;
    return addSources(paths.map(p => ({ path: p })), playFirst);
  }
  function addFiles(files, playFirst) {
    const list = [...files].filter(f => f.type.startsWith('audio') || PLAYABLE_RE.test(f.name) || isZip(f.name) || isRom(f.name));
    if (!list.length) return;
    if (nd) { // en Electron preferimos rutas (persisten entre sesiones)
      const paths = list.map(f => nd.pathForFile(f)).filter(Boolean);
      if (paths.length) return addPaths(paths, playFirst);
    }
    return addSources(list.map(f => ({ file: f })), playFirst);
  }
  async function pickFiles() {
    if (nd) addPaths(await nd.openFiles());
    else $('#fileInput').click();
  }
  async function pickFolder() {
    if (nd) addPaths(await nd.openFolder());
    else { const i = $('#fileInput'); i.setAttribute('webkitdirectory', ''); i.click(); i.removeAttribute('webkitdirectory'); }
  }

  function fmt(t) {
    if (!isFinite(t) || t <= 0) return '--:--';
    const m = Math.floor(t / 60), s = Math.floor(t % 60);
    return `${m}:${String(s).padStart(2, '0')}`;
  }
  const esc = s => String(s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

  function renderList() {
    const f = filter.toLowerCase();
    const html = [];
    tracks.forEach((t, i) => {
      if (f && !`${t.title} ${t.artist} ${t.album} ${t.name}`.toLowerCase().includes(f)) return;
      html.push(`<div class="pl-row${i === current ? ' cur' : ''}${selected.has(t.id) ? ' sel' : ''}" data-i="${i}"><span><b class="pl-num">${i === current ? (audio.paused ? '❚❚' : '▶') : i + 1}</b><b class="pl-play" title="Reproducir">▶</b></span><span>${esc(t.title)}</span><span>${esc(t.artist)}</span><span>${esc(t.album)}</span><span>${fmt(t.duration)}</span></div>`);
    });
    $('#plList').innerHTML = html.join('');
    $('#plEmpty').style.display = tracks.length ? 'none' : 'flex';
    $('#listCount').textContent = tracks.length;
    display.state.hasDisc = tracks.length > 0;
    display.state.total = tracks.length;
  }
  function bindList() {
    const list = $('#plList');
    // Un clic selecciona (sin redibujar la lista, para no romper el doble clic),
    // doble clic o el botón ▶ de la fila reproduce, Enter reproduce la seleccionada.
    list.addEventListener('dblclick', e => { const r = e.target.closest('.pl-row'); if (r) playIndex(+r.dataset.i); });
    list.addEventListener('click', e => {
      const r = e.target.closest('.pl-row'); if (!r) return;
      if (e.target.closest('.pl-play')) { playIndex(+r.dataset.i); return; }
      const t = tracks[+r.dataset.i];
      if (!e.ctrlKey) { selected.clear(); list.querySelectorAll('.pl-row.sel').forEach(x => x.classList.remove('sel')); }
      selected.has(t.id) ? selected.delete(t.id) : selected.add(t.id);
      r.classList.toggle('sel', selected.has(t.id));
    });
    list.addEventListener('keydown', e => { if (e.key === 'Enter') { const r = list.querySelector('.pl-row.sel'); if (r) playIndex(+r.dataset.i); } });
    list.tabIndex = 0;
    $('#plFiles').onclick = pickFiles;
    $('#plFolder').onclick = pickFolder;
    $('#plRemove').onclick = removeSelected;
    $('#plClear').onclick = clearList;
    $('#plSearch').oninput = e => { filter = e.target.value; renderList(); };
    $('#fileInput').onchange = e => { addFiles(e.target.files); e.target.value = ''; };
  }
  function removeSelected() {
    if (!selected.size) return;
    const curTrack = tracks[current];
    tracks = tracks.filter(t => !selected.has(t.id));
    selected.clear();
    current = curTrack ? tracks.indexOf(curTrack) : -1;
    if (curTrack && current < 0) { stop(); }
    reshuffle(); renderList(); save();
  }
  function clearList() {
    stop();
    tracks = []; current = -1; selected.clear();
    renderList(); updateNowPlaying(); save();
    display.flash('EJECT', 'PLAYLIST VACÍA');
  }

  // ---------------- reproducción ----------------
  // `seq` evita que una carga antigua más lenta pise a la última pedida
  async function loadTrack(i, seq = ++loadSeq) {
    const t = tracks[i];
    if (!t) return false;
    try {
      const bytes = await readTrackBytes(t);
      if (seq !== loadSeq) return false;
      if (t.rom) {
        const snap = t.rom.free ? null : await window.NDRom.snapshot(bytes, t.rom);
        if (seq !== loadSeq) return false;
        await audio.loadMd({ rom: bytes, snapshot: snap, template: t.rom.template, value: t.rom.value, duration: t.duration, jingle: !!t.rom.jingle });
        return true;
      }
      if (isVgm(t.name)) {
        await audio.loadVgm(bytes);
        return true;
      }
      if (curUrl) { URL.revokeObjectURL(curUrl); curUrl = null; }
      curUrl = URL.createObjectURL(new Blob([bytes], { type: mimeFor(t.name) }));
      audio.loadUrl(curUrl);
      // las pistas dentro de un ZIP se etiquetan al reproducirlas
      if (t.entry && nd && !t.tagged) {
        t.tagged = true;
        nd.tagsBuffer(bytes, t.name).then(m => { applyTags(t, m); renderList(); if (tracks[current] === t) updateNowPlaying(); save(); });
      }
      return true;
    } catch (err) {
      console.error(err);
      display.flash('ERROR-11', 'NO SE PUEDE LEER');
      return false;
    }
  }
  function mimeFor(name) {
    const ext = name.split('.').pop().toLowerCase();
    return { mp3: 'audio/mpeg', flac: 'audio/flac', wav: 'audio/wav', ogg: 'audio/ogg', oga: 'audio/ogg', opus: 'audio/ogg', m4a: 'audio/mp4', mp4: 'audio/mp4', aac: 'audio/aac', webm: 'audio/webm' }[ext] || '';
  }
  let loadSeq = 0;
  async function playIndex(i, startAt = 0, autoplay = true) {
    if (i < 0 || i >= tracks.length) return;
    if (tracks[i].scanning) { pendingPlay = tracks[i]; display.flash('ESPERA', 'ANALIZANDO ROM'); return; }
    const seq = ++loadSeq;
    current = i;
    renderList();
    updateNowPlaying();
    const ok = await loadTrack(i, seq);
    if (seq !== loadSeq) return;
    if (!ok) { setTimeout(() => next(true), 1200); return; }
    if (startAt) audio.addEventListener('loadedmetadata', () => { audio.currentTime = startAt; }, { once: true });
    if (scanMode) audio.addEventListener('loadedmetadata', () => { audio.currentTime = Math.min(audio.duration * 0.3, 60); }, { once: true });
    if (autoplay) await play();
    save();
  }
  async function play() {
    if (current < 0) { if (tracks.length) return playIndex(0); pickFiles(); return; }
    if (!audio.src) return playIndex(current);
    await engine.resume();
    try { await audio.play(); } catch (e) { console.warn(e); }
  }
  function pause() { audio.pause(); }
  function togglePlay() { audio.paused ? play() : pause(); }
  function stop() { audio.pause(); try { audio.currentTime = 0; } catch { } setScan(false); }
  function reshuffle() {
    order = tracks.map((_, i) => i);
    for (let i = order.length - 1; i > 0; i--) { const j = Math.floor(Math.random() * (i + 1)); [order[i], order[j]] = [order[j], order[i]]; }
  }
  function next(auto) {
    if (!tracks.length) return;
    if (auto && S.repeat === 1) return playIndex(current);
    let n;
    if (S.shuffle) {
      const pos = order.indexOf(current);
      n = order[pos + 1];
      if (n === undefined) { if (auto && S.repeat !== 2) { stop(); return; } reshuffle(); n = order[0]; }
    } else {
      n = current + 1;
      if (n >= tracks.length) { if (auto && S.repeat !== 2) { stop(); setScan(false); return; } n = 0; }
    }
    playIndex(n);
  }
  function prev() {
    if (!tracks.length) return;
    if (audio.currentTime > 3) { audio.currentTime = 0; return; }
    let n;
    if (S.shuffle) { const pos = order.indexOf(current); n = order[Math.max(0, pos - 1)]; }
    else n = current - 1 < 0 ? tracks.length - 1 : current - 1;
    playIndex(n);
  }
  function seekBy(s) { if (audio.duration) audio.currentTime = Math.max(0, Math.min(audio.duration - 0.5, audio.currentTime + s)); }
  function setScan(on) {
    scanMode = on;
    clearInterval(scanTimer);
    if (on) {
      display.flash('SCAN', 'INTRO 10 SEG');
      if (audio.duration) audio.currentTime = Math.min(audio.duration * 0.3, 60);
      let left = 10;
      scanTimer = setInterval(() => { if (!audio.paused && --left <= 0) { left = 10; next(); } }, 1000);
      if (audio.paused) play();
    }
    updateIcons();
  }
  function cycleRepeat() {
    S.repeat = (S.repeat + 1) % 3;
    display.flash(['REPEAT OFF', 'REPEAT TRACK', 'REPEAT ALL'][S.repeat]);
    updateIcons(); save();
  }
  function toggleShuffle() {
    S.shuffle = !S.shuffle;
    reshuffle();
    display.flash(S.shuffle ? 'RANDOM ON' : 'RANDOM OFF');
    updateIcons(); save();
  }

  async function updateNowPlaying() {
    const t = tracks[current];
    $('#npTitle').textContent = t ? t.title : 'Sin pista';
    $('#npArtist').textContent = t ? (t.artist || 'Artista desconocido') : '—';
    $('#npAlbum').textContent = t ? t.album || '' : '';
    $('#npTech').textContent = t ? [t.system, t.codec, t.bitrate && t.bitrate + ' kbps', t.sampleRate && (t.sampleRate / 1000) + ' kHz'].filter(Boolean).join(' · ') : '';
    const st = display.state;
    st.title = t ? t.title : ''; st.artist = t ? [t.artist, t.album].filter(Boolean).join(' / ') : '';
    st.track = current + 1;
    st.format = t ? (t.rom ? 'ROM' : isVgm(t.name) ? 'VGM' : (t.name.split('.').pop() || '').toUpperCase().slice(0, 4)) : '';
    const title = t ? `${t.artist ? t.artist + ' — ' : ''}${t.title}` : 'NightDrive Player';
    $('#tbTitle').textContent = title;
    document.title = t ? `${t.title} · NightDrive` : 'NightDrive Player';
    if (nd) nd.title(document.title);
    if ('mediaSession' in navigator && t) {
      navigator.mediaSession.metadata = new MediaMetadata({ title: t.title, artist: t.artist, album: t.album });
    }
    const img = $('#npImg');
    img.classList.remove('show');
    if (t && nd && t.path) {
      const id = t.id;
      const data = await nd.cover(t.path);
      if (data && tracks[current] && tracks[current].id === id) {
        img.src = data; img.classList.add('show');
        if ('mediaSession' in navigator) navigator.mediaSession.metadata = new MediaMetadata({ title: t.title, artist: t.artist, album: t.album, artwork: [{ src: data }] });
      }
    }
  }

  // ---------------- volumen / knob ----------------
  function setVolume(v, quiet) {
    S.volume = Math.max(0, Math.min(40, Math.round(v)));
    engine.setVolume(S.volume);
    const knob = $('#knob');
    knob.style.setProperty('--v', S.volume / 40);
    knob.querySelector('.knob-cap').style.setProperty('--rot', `${-135 + (S.volume / 40) * 270}deg`);
    $('#tVol').textContent = 'VOL ' + S.volume;
    if (!quiet) display.flash(`VOLUME ${String(S.volume).padStart(2, '0')}`, S.volume === 0 ? 'MUTE' : '▮'.repeat(Math.ceil(S.volume / 4)), 1200);
    save();
  }
  function bindKnob() {
    const knob = $('#knob');
    knob.addEventListener('wheel', e => { e.preventDefault(); setVolume(S.volume + (e.deltaY < 0 ? 1 : -1)); }, { passive: false });
    let drag = null;
    knob.addEventListener('pointerdown', e => {
      knob.setPointerCapture(e.pointerId);
      const r = knob.getBoundingClientRect();
      drag = { cx: r.left + r.width / 2, cy: r.top + r.height / 2, y: e.clientY, v: S.volume, moved: false, ang: null };
    });
    knob.addEventListener('pointermove', e => {
      if (!drag) return;
      // giro circular alrededor del centro
      const a = Math.atan2(e.clientY - drag.cy, e.clientX - drag.cx);
      if (drag.ang !== null) {
        let d = a - drag.ang;
        if (d > Math.PI) d -= 2 * Math.PI; if (d < -Math.PI) d += 2 * Math.PI;
        drag.acc = (drag.acc || 0) + d;
        const steps = Math.trunc(drag.acc / 0.12);
        if (steps) { drag.acc -= steps * 0.12; drag.moved = true; setVolume(S.volume + steps); }
      }
      drag.ang = a;
    });
    knob.addEventListener('pointerup', () => { if (drag && !drag.moved) togglePlay(); drag = null; });
  }

  // ---------------- indicadores ----------------
  function updateIcons() {
    const on = (k, v) => { const el = document.querySelector(`#icons [data-i="${k}"]`); if (el) el.classList.toggle('on', !!v); };
    on('play', !audio.paused);
    on('pause', audio.paused && current >= 0);
    on('rpt', S.repeat > 0);
    $('#icons [data-i="rpt"]').textContent = S.repeat === 2 ? 'RPT ALL' : 'RPT';
    on('rdm', S.shuffle);
    on('scan', scanMode);
    on('eq', S.eqOn && S.preset !== 'FLAT');
    on('loud', S.eqOn && S.loud > 0);
    on('bass', S.eqOn && S.bass > 0);
    $$('.key.num').forEach(k => {
      const n = +k.dataset.k;
      k.classList.toggle('on', (n === 1 && S.repeat > 0) || (n === 2 && S.shuffle) || (n === 3 && scanMode) || (n === 4 && audio.paused && current >= 0));
    });
    $('#tPlay').textContent = audio.paused ? '▶' : '❚❚';
    document.body.classList.toggle('playing', !audio.paused);
    display.state.playing = !audio.paused;
  }

  // ---------------- frontal ----------------
  function toggleFaceplate(open) {
    const p = $('#plate');
    const willOpen = open === undefined ? !p.classList.contains('open') : open;
    p.classList.toggle('open', willOpen);
  }
  function setTab(t) {
    S.tab = t;
    $$('#tabs button').forEach(b => b.classList.toggle('on', b.dataset.tab === t));
    $$('.panel').forEach(p => p.classList.toggle('on', p.id === 'panel-' + t));
    requestAnimationFrame(() => eq.resize());
    if (document.body.classList.contains('compact')) setCompact(false);
    save();
  }
  function setCompact(v) {
    document.body.classList.toggle('compact', v);
    $('#tbCompact').classList.toggle('on', v);
    if (nd) nd.compact(v);
    setTimeout(() => { display.resize(); eq.resize(); }, 350);
  }
  let powerOff = false;
  function togglePower() {
    powerOff = !powerOff;
    $('#display').style.visibility = powerOff ? 'hidden' : '';
    $('#icons').style.visibility = powerOff ? 'hidden' : '';
    if (!powerOff) display.intro();
  }

  function bindFaceplate() {
    $('#kOpen').onclick = () => toggleFaceplate(true);
    $('#slotClose').onclick = () => toggleFaceplate(false);
    $('#discSlot').onclick = pickFiles;
    $('#slotFiles').onclick = async () => { await pickFiles(); toggleFaceplate(false); };
    $('#slotFolder').onclick = async () => { await pickFolder(); toggleFaceplate(false); };
    $('#slotEject').onclick = () => { clearList(); };
    $('#kSrc').onclick = togglePower;
    $('#kDisp').onclick = () => setMode(display.mode + 1);
    $('#display').onclick = () => setMode(display.mode + 1);
    $('#kEq').onclick = () => {
      const list = PRESET_NAMES.filter(p => p !== 'C1' && p !== 'C2' || S.custom[p]);
      const i = list.indexOf(S.preset);
      selectPreset(list[(i + 1) % list.length]);
    };
    $('#kAudio').onclick = () => { setLoud((S.loud + 1) % 4); display.flash('LOUDNESS', ['OFF', 'LOW', 'MID', 'HIGH'][S.loud]); };
    $('#kIllumi').onclick = () => {
      const i = SKINS.findIndex(s => s.id === S.skin);
      S.skin = SKINS[(i + 1) % SKINS.length].id;
      applySkin(); display.flash(currentSkin().name, 'FACEPLATE'); save();
    };
    $$('.key.num').forEach(k => k.onclick = () => {
      const n = +k.dataset.k;
      if (n === 1) cycleRepeat();
      else if (n === 2) toggleShuffle();
      else if (n === 3) setScan(!scanMode);
      else if (n === 4) togglePlay();
      else if (n === 5) seekBy(-10);
      else if (n === 6) seekBy(10);
    });
    $$('#joy [data-j]').forEach(b => b.onclick = () => {
      const j = b.dataset.j;
      if (j === 'play') togglePlay();
      else if (j === 'next') next();
      else if (j === 'prev') prev();
      else if (j === 'up') setTab('list');
      else if (j === 'down') setTab('eq');
    });
    // mantener pulsado ◀◀ / ▶▶ = avance rápido
    $$('#joy .jb.left, #joy .jb.right').forEach(b => {
      let t = 0, ff = 0;
      b.addEventListener('pointerdown', () => { t = setTimeout(() => { ff = setInterval(() => seekBy(b.classList.contains('right') ? 4 : -4), 120); b._ff = true; }, 450); });
      const end = () => { clearTimeout(t); if (ff) { clearInterval(ff); ff = 0; setTimeout(() => b._ff = false, 0); } };
      b.addEventListener('pointerup', end); b.addEventListener('pointerleave', end);
      b.addEventListener('click', e => { if (b._ff) { e.stopImmediatePropagation(); b._ff = false; } }, true);
    });
  }

  function bindTransport() {
    $('#tPlay').onclick = togglePlay;
    $('#tNext').onclick = () => next();
    $('#tPrev').onclick = prev;
    $('#tStop').onclick = stop;
    const seek = $('#seek');
    let dragging = false;
    const at = e => { const r = seek.getBoundingClientRect(); return Math.max(0, Math.min(1, (e.clientX - r.left) / r.width)); };
    seek.addEventListener('pointerdown', e => { if (!audio.duration) return; dragging = true; seek.setPointerCapture(e.pointerId); audio.currentTime = at(e) * audio.duration; });
    seek.addEventListener('pointermove', e => { if (dragging) audio.currentTime = at(e) * audio.duration; });
    seek.addEventListener('pointerup', () => dragging = false);
    $$('#tabs button').forEach(b => b.onclick = () => setTab(b.dataset.tab));
  }

  function bindWindow() {
    $('#tbMin').onclick = () => nd && nd.minimize();
    $('#tbMax').onclick = () => nd && nd.maximize();
    $('#tbClose').onclick = () => { save(); nd ? nd.close() : window.close(); };
    $('#tbCompact').onclick = () => setCompact(!document.body.classList.contains('compact'));
    $('#tbTop').onclick = () => { S.onTop = !S.onTop; $('#tbTop').classList.toggle('on', S.onTop); nd && nd.onTop(S.onTop); save(); };
    if (!nd) $$('#tbMin, #tbMax, #tbClose').forEach(b => b.style.display = 'none');
    if (nd) {
      nd.on('media-key', cmd => ({ playpause: togglePlay, next: () => next(), prev, stop })[cmd]());
      nd.on('open-files', files => addPaths(files, true));
    }
    if ('mediaSession' in navigator) {
      const ms = navigator.mediaSession;
      ms.setActionHandler('play', play); ms.setActionHandler('pause', pause);
      ms.setActionHandler('nexttrack', () => next()); ms.setActionHandler('previoustrack', prev);
    }

    // arrastrar y soltar
    let depth = 0;
    window.addEventListener('dragenter', e => { e.preventDefault(); depth++; document.body.classList.add('dragging'); });
    window.addEventListener('dragleave', () => { if (--depth <= 0) { depth = 0; document.body.classList.remove('dragging'); } });
    window.addEventListener('dragover', e => e.preventDefault());
    window.addEventListener('drop', async e => {
      e.preventDefault(); depth = 0; document.body.classList.remove('dragging');
      const files = [...e.dataTransfer.files];
      if (nd) {
        const paths = files.map(f => nd.pathForFile(f)).filter(Boolean);
        addPaths(await nd.expand(paths));
      } else addFiles(files);
      toggleFaceplate(false);
    });

    // teclado
    window.addEventListener('keydown', e => {
      if (e.target.matches('input[type=search]')) return;
      const k = e.key;
      if (k === ' ') { e.preventDefault(); togglePlay(); }
      else if (k === 'ArrowRight') { e.preventDefault(); e.ctrlKey ? next() : seekBy(5); }
      else if (k === 'ArrowLeft') { e.preventDefault(); e.ctrlKey ? prev() : seekBy(-5); }
      else if (k === 'ArrowUp') { e.preventDefault(); setVolume(S.volume + 1); }
      else if (k === 'ArrowDown') { e.preventDefault(); setVolume(S.volume - 1); }
      else if (k === 'd' || k === 'D') setMode(display.mode + 1);
      else if (k === 'Delete') removeSelected();
      else if (k === 'o' && e.ctrlKey) { e.preventDefault(); pickFiles(); }
    });
  }

  // eventos del <audio>
  audio.addEventListener('play', updateIcons);
  audio.addEventListener('pause', () => { updateIcons(); renderList(); save(); });
  audio.addEventListener('playing', renderList);
  audio.addEventListener('ended', () => next(true));
  audio.addEventListener('loadedmetadata', () => {
    const t = tracks[current];
    if (t && !t.duration) { t.duration = audio.duration; renderList(); }
    display.state.duration = audio.duration;
  });
  audio.addEventListener('error', () => { if (audio.src) { display.flash('ERROR-23', 'FORMATO NO SOPORTADO'); setTimeout(() => next(true), 1500); } });

  // ---------------- bucle de render ----------------
  let lastIllum = 0, lastUi = 0;
  function frame(now) {
    const st = display.state;
    st.time = audio.currentTime || 0;
    st.duration = audio.duration || (tracks[current] && tracks[current].duration) || 0;
    if (!powerOff) display.render();
    if ($('#panel-eq').classList.contains('on') && !document.body.classList.contains('compact')) eq.render();
    if (now - lastUi > 100) {
      lastUi = now;
      const p = st.duration ? st.time / st.duration : 0;
      $('#seekFill').style.width = p * 100 + '%';
      $('#seekThumb').style.left = p * 100 + '%';
      $('#tCur').textContent = fmt(st.time).replace('--:--', '0:00');
      $('#tDur').textContent = fmt(st.duration);
      if (nd) nd.progress(audio.paused || !st.duration ? -1 : p);
    }
    if (currentSkin().rainbow && S.illAuto && now - lastIllum > 60) { lastIllum = now; applyIllum(); }
    requestAnimationFrame(frame);
  }

  // ---------------- arranque ----------------
  async function init() {
    buildSkinPanel();
    buildPresets();
    if (S.bands !== 31) { eq.setBandCount(S.bands); }
    segOn('#segBands', S.bands);
    if (S.gains && S.gains.length === eq.freqs.length) eq.setAll(S.gains, false);
    markPreset();
    bindEqControls();
    segOn('#segLoud', S.loud);
    setEqBypass(S.eqOn);
    bindList();
    bindFaceplate();
    bindTransport();
    bindWindow();
    bindKnob();
    applySkin();
    setVolume(S.volume, true);
    display.mode = S.mode || 0;
    $('#iconMode').textContent = window.NDDisplay.MODES[display.mode];
    refreshChips();
    $('#tbTop').classList.toggle('on', !!S.onTop);
    if (nd && S.onTop) nd.onTop(true);
    setTab(S.tab || 'eq');

    // restaurar playlist (Electron)
    if (nd && S.playlist && S.playlist.length) {
      tracks = S.playlist.map(t => ({ id: uid++, ...t }));
      renderList(); reshuffle();
      if (S.current >= 0 && S.current < tracks.length) {
        current = S.current;
        updateNowPlaying(); renderList();
        const pos = S.position || 0;
        loadTrack(current).then(ok => { if (ok && pos) audio.addEventListener('loadedmetadata', () => { audio.currentTime = pos; }, { once: true }); });
      }
    } else renderList();
    if (nd) {
      const pending = await nd.pendingFiles();
      if (pending.length) addPaths(pending, true);
    }
    // el AudioContext necesita un gesto del usuario en algunos entornos
    const unlock = () => { engine.resume(); window.removeEventListener('pointerdown', unlock); };
    window.addEventListener('pointerdown', unlock);
    updateIcons();
    requestAnimationFrame(frame);
    window.addEventListener('beforeunload', save);
  }

  // API de depuración/pruebas
  window.NightDrive = { engine, display, eq, addFiles, playIndex, setMode, applySkin, S, toggleFaceplate, selectPreset, setTab, SKINS };

  init();
})();
