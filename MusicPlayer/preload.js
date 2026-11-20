const { contextBridge, ipcRenderer, webUtils } = require('electron');

contextBridge.exposeInMainWorld('nd', {
  isElectron: true,
  openFiles: () => ipcRenderer.invoke('dialog:open-files'),
  openFolder: () => ipcRenderer.invoke('dialog:open-folder'),
  expand: paths => ipcRenderer.invoke('fs:expand', paths),
  readFile: p => ipcRenderer.invoke('fs:read', p),
  exists: p => ipcRenderer.invoke('fs:exists', p),
  tags: paths => ipcRenderer.invoke('meta:tags', paths),
  tagsBuffer: (bytes, name) => ipcRenderer.invoke('meta:buffer', bytes, name),
  cover: p => ipcRenderer.invoke('meta:cover', p),
  pendingFiles: () => ipcRenderer.invoke('app:pending-files'),
  pathForFile: f => { try { return webUtils.getPathForFile(f); } catch { return null; } },
  minimize: () => ipcRenderer.send('win:minimize'),
  maximize: () => ipcRenderer.send('win:maximize'),
  close: () => ipcRenderer.send('win:close'),
  compact: v => ipcRenderer.send('win:compact', v),
  onTop: v => ipcRenderer.send('win:ontop', v),
  progress: v => ipcRenderer.send('win:progress', v),
  title: t => ipcRenderer.send('win:title', t),
  on: (channel, fn) => {
    if (!['media-key', 'open-files', 'win-state'].includes(channel)) return;
    ipcRenderer.on(channel, (_e, ...args) => fn(...args));
  }
});
