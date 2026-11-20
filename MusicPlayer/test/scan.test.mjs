// Prueba de extremo a extremo con la ROM de prueba: aprende el buzón, escanea y toca.
// Ejecutar: node test/make-test-rom.mjs /tmp/ndtest.md && node test/scan.test.mjs /tmp/ndtest.md
import fs from 'fs';
import { parseRom } from '../src/md/megadrive.js';
import { learn, scanRom, snapshotAt } from '../src/md/scan.js';
import { MdPlayer } from '../src/md/md-player.js';
const info = parseRom(new Uint8Array(fs.readFileSync(process.argv[2])));
let fail = 0;
const check = (n, c, i = '') => { if (!c) { fail++; console.log('FALLO', n, i); } else console.log('ok  ', n); };
console.log('ROM:', info.title, info.serial, info.region, 'isMD', info.isMD);
let t = Date.now();
const l = learn(info);
console.log('learn', JSON.stringify(l), Date.now() - t, 'ms');
check('buzón aprendido en 1F00h con 81h', l.mode === 'z80' && l.mailbox === 0x1F00 && l.value === 0x81);
t = Date.now();
const res = scanRom(info);
console.log('scan', JSON.stringify(res.tracks), res.ms, 'ms');
check('canciones 81h, 82h y jingle 83h', res.tracks.length === 3 && res.tracks[0].value === 0x81 && res.tracks[0].kind === 'song' && res.tracks[1].value === 0x82 && res.tracks[2].value === 0x83 && res.tracks[2].kind === 'jingle');
// reproducir la 82h y medir la primera nota (La3 = 220 Hz, canal FM)
const snap = snapshotAt(info, l.frame);
const p = new MdPlayer(48000);
p.load({ rom: info.rom, snapshot: snap, mailbox: l.mailbox, value: 0x82, duration: 20 });
const L = new Float32Array(128), R = new Float32Array(128), out = [];
t = Date.now();
for (let i = 0; i < 48000 * 4 / 128; i++) { p.render(L, R); for (const v of L) out.push(v); }
const cpu = Date.now() - t;
console.log('4 s de audio en', cpu, 'ms de CPU');
check('emulación más rápida que tiempo real', cpu < 4000);
// la primera nota suena entre ~0,17 s y ~0,33 s (10 frames por nota): análisis Goertzel
const a = Math.round(48000 * 0.19), b = Math.round(48000 * 0.32);
const power = f => { const k = 2 * Math.cos(2 * Math.PI * f / 48000); let s1 = 0, s2 = 0; for (let i = a; i < b; i++) { const s0 = out[i] + k * s1 - s2; s2 = s1; s1 = s0; } return s1 * s1 + s2 * s2 - k * s1 * s2; };
const cands = { 'La3 220': 220, 'Do4 261.6': 261.63, 'Mi4 329.6': 329.63, 'Do5 523': 523.25 };
const pw = Object.fromEntries(Object.entries(cands).map(([n, f]) => [n, power(f)]));
const best = Object.entries(pw).sort((x, y) => y[1] - x[1])[0][0];
console.log('nota dominante', best);
check('primera nota de 82h = La3 (220 Hz)', best === 'La3 220', best);
let peak = 0; for (const v of out) peak = Math.max(peak, Math.abs(v));
check('hay sonido', peak > 0.05, peak);
fs.writeFileSync(process.argv[2] + '.raw', Buffer.from(new Float32Array(out).buffer));
process.exit(fail ? 1 : 0);
