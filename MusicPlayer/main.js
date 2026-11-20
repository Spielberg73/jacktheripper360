// NightDrive Player — proceso principal (Electron)
const { app, BrowserWindow, ipcMain, dialog, globalShortcut, shell } = require('electron');
const path = require('path');
const fs = require('fs');

const AUDIO_EXT = new Set(['.mp3', '.flac', '.wav', '.ogg', '.oga', '.m4a', '.aac', '.opus', '.webm', '.mp4', '.vgm', '.vgz', '.zip']);
const FULL_SIZE = { width: 1240, height: 900 };
const COMPACT_HEIGHT = 400;

let win = null;
let pendingFiles = filesFromArgv(process.argv);

function filesFromArgv(argv) {
  return argv.slice(1).filter(a => {
    try { return AUDIO_EXT.has(path.extname(a).toLowerCase()) && fs.statSync(a).isFile(); } catch { return false; }
  });
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', (_e, argv) => {
    const files = filesFromArgv(argv);
    if (win) {
      if (win.isMinimized()) win.restore();
      win.focus();
      if (files.length) win.webContents.send('open-files', files);
    }
  });
}

function createWindow() {
  win = new BrowserWindow({
    ...FULL_SIZE,
    minWidth: 1000,
    minHeight: 360,
    frame: false,
    backgroundColor: '#07070a',
    title: 'NightDrive Player',
    icon: path.join(__dirname, 'assets', 'icon.png'),
    show: false,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
      backgroundThrottling: false,
      autoplayPolicy: 'no-user-gesture-required'
    }
  });
  win.setMenuBarVisibility(false);
  win.loadFile(path.join(__dirname, 'src', 'index.html'));
  win.once('ready-to-show', () => win.show());
  win.webContents.setWindowOpenHandler(({ url }) => { shell.openExternal(url); return { action: 'deny' }; });
  win.on('maximize', () => win.webContents.send('win-state', 'max'));
  win.on('unmaximize', () => win.webContents.send('win-state', 'normal'));
}

function registerMediaKeys() {
  const map = {
    MediaPlayPause: 'playpause',
    MediaNextTrack: 'next',
    MediaPreviousTrack: 'prev',
    MediaStop: 'stop'
  };
  for (const [key, cmd] of Object.entries(map)) {
    try { globalShortcut.register(key, () => win && win.webContents.send('media-key', cmd)); } catch { /* otra app los tiene */ }
  }
}

async function scanFolder(dir, out = [], depth = 0) {
  if (depth > 12) return out;
  let entries;
  try { entries = await fs.promises.readdir(dir, { withFileTypes: true }); } catch { return out; }
  entries.sort((a, b) => a.name.localeCompare(b.name, undefined, { numeric: true, sensitivity: 'base' }));
  for (const e of entries) {
    const full = path.join(dir, e.name);
    if (e.isDirectory()) await scanFolder(full, out, depth + 1);
    else if (AUDIO_EXT.has(path.extname(e.name).toLowerCase())) out.push(full);
  }
  return out;
}

// ---------- IPC ----------
ipcMain.handle('dialog:open-files', async () => {
  const r = await dialog.showOpenDialog(win, {
    title: 'Añadir canciones',
    properties: ['openFile', 'multiSelections'],
    filters: [{ name: 'Audio', extensions: [...AUDIO_EXT].map(e => e.slice(1)) }, { name: 'Todos', extensions: ['*'] }]
  });
  return r.canceled ? [] : r.filePaths;
});

ipcMain.handle('dialog:open-folder', async () => {
  const r = await dialog.showOpenDialog(win, { title: 'Añadir carpeta', properties: ['openDirectory', 'multiSelections'] });
  if (r.canceled) return [];
  const all = [];
  for (const d of r.filePaths) await scanFolder(d, all);
  return all;
});

ipcMain.handle('fs:expand', async (_e, paths) => {
  const out = [];
  for (const p of paths) {
    try {
      const st = await fs.promises.stat(p);
      if (st.isDirectory()) await scanFolder(p, out);
      else if (AUDIO_EXT.has(path.extname(p).toLowerCase())) out.push(p);
    } catch { /* ignorar */ }
  }
  return out;
});

ipcMain.handle('fs:read', async (_e, p) => {
  const buf = await fs.promises.readFile(p);
  return new Uint8Array(buf.buffer, buf.byteOffset, buf.byteLength);
});

ipcMain.handle('fs:exists', async (_e, p) => {
  try { await fs.promises.access(p); return true; } catch { return false; }
});

let mm = null;
async function meta() { if (!mm) mm = require('music-metadata'); return mm; }

ipcMain.handle('meta:tags', async (_e, paths) => {
  const lib = await meta();
  const out = [];
  for (const p of paths) {
    try {
      const m = await lib.parseFile(p, { duration: false, skipCovers: true });
      out.push({
        title: m.common.title || null,
        artist: m.common.artist || null,
        album: m.common.album || null,
        track: m.common.track && m.common.track.no || null,
        duration: m.format.duration || null,
        codec: m.format.codec || m.format.container || null,
        bitrate: m.format.bitrate ? Math.round(m.format.bitrate / 1000) : null,
        sampleRate: m.format.sampleRate || null
      });
    } catch {
      out.push(null);
    }
  }
  return out;
});

function tagsFrom(m) {
  return {
    title: m.common.title || null,
    artist: m.common.artist || null,
    album: m.common.album || null,
    track: m.common.track && m.common.track.no || null,
    duration: m.format.duration || null,
    codec: m.format.codec || m.format.container || null,
    bitrate: m.format.bitrate ? Math.round(m.format.bitrate / 1000) : null,
    sampleRate: m.format.sampleRate || null
  };
}

// etiquetas a partir de bytes (canciones dentro de un ZIP)
ipcMain.handle('meta:buffer', async (_e, bytes, name) => {
  try {
    const lib = await meta();
    const ext = path.extname(name || '').slice(1).toLowerCase();
    const mime = { mp3: 'audio/mpeg', flac: 'audio/flac', wav: 'audio/wav', ogg: 'audio/ogg', opus: 'audio/ogg', m4a: 'audio/mp4', aac: 'audio/aac' }[ext];
    const m = await lib.parseBuffer(Buffer.from(bytes), { mimeType: mime, path: name }, { skipCovers: true });
    return tagsFrom(m);
  } catch { return null; }
});

ipcMain.handle('meta:cover', async (_e, p) => {
  try {
    const lib = await meta();
    const m = await lib.parseFile(p, { duration: false });
    const pic = lib.selectCover ? lib.selectCover(m.common.picture) : (m.common.picture || [])[0];
    if (!pic) return null;
    return `data:${pic.format};base64,${Buffer.from(pic.data).toString('base64')}`;
  } catch { return null; }
});

ipcMain.on('win:minimize', () => win && win.minimize());
ipcMain.on('win:maximize', () => { if (!win) return; win.isMaximized() ? win.unmaximize() : win.maximize(); });
ipcMain.on('win:close', () => win && win.close());
ipcMain.on('win:compact', (_e, compact) => {
  if (!win) return;
  if (win.isMaximized()) win.unmaximize();
  const [w] = win.getSize();
  if (compact) { win.setMinimumSize(1000, 300); win.setSize(w, COMPACT_HEIGHT, true); }
  else { win.setSize(w, FULL_SIZE.height, true); win.setMinimumSize(1000, 360); }
});
ipcMain.on('win:ontop', (_e, v) => win && win.setAlwaysOnTop(!!v));
ipcMain.on('win:progress', (_e, v) => win && win.setProgressBar(v < 0 ? -1 : v));
ipcMain.on('win:title', (_e, t) => win && win.setTitle(t));
ipcMain.handle('app:pending-files', () => { const f = pendingFiles; pendingFiles = []; return f; });

app.whenReady().then(() => {
  createWindow();
  registerMediaKeys();
});
app.on('will-quit', () => globalShortcut.unregisterAll());
app.on('window-all-closed', () => app.quit());
