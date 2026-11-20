// AudioWorklet que reproduce VGM emulando YM2612 + SN76489
import { VgmPlayer } from './chips/vgm-core.js';
import { MdPlayer } from './md/md-player.js';

class VgmProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.player = null;
    this.playing = false;
    this.lastPost = 0;
    this.port.onmessage = e => this.onMessage(e.data);
  }

  onMessage(m) {
    switch (m.type) {
      case 'load':
        this.playing = false;
        try {
          const p = new VgmPlayer(sampleRate);
          p.load(new Uint8Array(m.data));
          this.player = p;
          this.port.postMessage({ type: 'loaded', id: m.id, duration: p.h.duration });
        } catch (err) {
          this.player = null;
          this.port.postMessage({ type: 'error', id: m.id, message: String(err && err.message || err) });
        }
        break;
      case 'loadMD':
        this.playing = false;
        try {
          const p = new MdPlayer(sampleRate);
          p.load({ ...m.cfg, rom: new Uint8Array(m.cfg.rom) });
          this.player = p;
          this.port.postMessage({ type: 'loaded', id: m.id, duration: p.h.duration });
        } catch (err) {
          this.player = null;
          this.port.postMessage({ type: 'error', id: m.id, message: String(err && err.message || err) });
        }
        break;
      case 'play': if (this.player) this.playing = true; break;
      case 'pause': this.playing = false; break;
      case 'seek': if (this.player) { this.player.seek(m.time); this.post(); } break;
      case 'unload': this.player = null; this.playing = false; break;
    }
  }

  post() {
    const p = this.player;
    const lv = new Float32Array(10);
    for (let i = 0; i < 6; i++) { lv[i] = Math.min(1, p.ym.levels[i] / 8192); p.ym.levels[i] = 0; }
    for (let i = 0; i < 4; i++) { lv[6 + i] = Math.min(1, p.psg.levels[i]); p.psg.levels[i] = 0; }
    this.port.postMessage({ type: 'pos', t: p.time, frame: currentFrame, levels: lv });
  }

  process(_inputs, outputs) {
    const out = outputs[0];
    const L = out[0], R = out[1] || out[0];
    if (!this.player || !this.playing) { L.fill(0); if (R !== L) R.fill(0); return true; }
    const alive = this.player.render(L, R);
    if (currentFrame - this.lastPost >= sampleRate * 0.03) { this.lastPost = currentFrame; this.post(); }
    if (!alive) { this.playing = false; this.post(); this.port.postMessage({ type: 'ended' }); }
    return true;
  }
}

registerProcessor('vgm-processor', VgmProcessor);
