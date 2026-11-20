// Genera una ROM de Mega Drive original para pruebas (NightDrive Test ROM).
// 68000: arranca, sube un driver al Z80, activa VBlank y pide la canción 0x81.
// Z80: driver mínimo con buzón en 1F00h: 81h y 82h = canciones en bucle, 83h = jingle.
import fs from 'fs';

// ---------- mini ensamblador Z80 (solo lo necesario) ----------
const z = new Uint8Array(0x400); let pc = 0; const labels = {}, fix = [];
const org = a => { pc = a; };
const db = (...b) => b.forEach(v => { z[pc++] = v & 0xFF; });
const label = n => { labels[n] = pc; };
const jr = (op, n) => { db(op, 0); fix.push({ at: pc - 1, n, rel: true }); };
const jp = (op, n) => { db(op, 0, 0); fix.push({ at: pc - 2, n, rel: false }); };
const w16 = v => [v & 0xFF, v >> 8];
const ldA = a => db(0x3A, ...w16(a)), stA = a => db(0x32, ...w16(a));
const ym = (reg, val) => db(0x16, reg, 0x1E, val, 0xCD, ...w16(0)) || fix.push({ at: pc - 2, n: 'ymw', rel: false });
const MB = 0x1F00, SONG = 0x1F01, TICK = 0x1F02, NOTE = 0x1F03, TEMPO = 0x1F04;
org(0); db(0xF3); db(0x31, ...w16(0x1E00)); db(0xED, 0x56); jp(0xC3, 'main');
org(0x38); db(0xF5); ldA(TICK); db(0x3C); stA(TICK); db(0xF1, 0xFB, 0xED, 0x4D);
org(0x50); label('ymw'); db(0x7A); stA(0x4000); db(0x7B); stA(0x4001); db(0xC9);
org(0x60); label('main');
// patch del canal 1: algoritmo 7, solo S4 audible
ym(0xB0, 0x07); ym(0xB4, 0xC0);
for (const o of [0, 4, 8, 12]) { ym(0x30 + o, 0x01); ym(0x40 + o, o === 12 ? 0x08 : 0x7F); ym(0x50 + o, 0x1F); ym(0x60 + o, 0x05); ym(0x70 + o, 0x02); ym(0x80 + o, 0x2F); }
db(0xFB); // ei
label('loop'); db(0x76); // halt
ldA(MB); db(0xB7); jr(0x28, 'play');
stA(SONG); db(0xAF); stA(MB); stA(NOTE); stA(TEMPO);
label('play'); ldA(SONG); db(0xD6, 0x81, 0xFE, 0x03); jr(0x30, 'loop');
db(0x4F); // ld c,a
ldA(TEMPO); db(0x3C); stA(TEMPO); db(0xFE, 10); jr(0x38, 'loop');
db(0xAF); stA(TEMPO);
ldA(NOTE); db(0x47, 0x3C); stA(NOTE); // ld b,a ; inc a
db(0x79, 0xFE, 0x02); jr(0x20, 'cont'); db(0x78, 0xFE, 12); jr(0x38, 'cont');
db(0xAF); stA(SONG); ym(0x28, 0x00); db(0x3E, 0x9F); stA(0x7F11); jp(0xC3, 'loop');
label('cont');
db(0x78, 0xE6, 0x07, 0x47); // ld a,b ; and 7 ; ld b,a
db(0x79, 0x87, 0x87, 0x87, 0x80, 0x87); // a = (c*8+b)*2
db(0x5F, 0x16, 0x00); db(0x21, 0, 0); fix.push({ at: pc - 2, n: 'songs', rel: false }); db(0x19); // ld hl,songs ; add hl,de
db(0xE5); ym(0x28, 0x00); db(0xE1); // keyoff
db(0x16, 0xA4, 0x5E, 0x23, 0xCD, 0, 0); fix.push({ at: pc - 2, n: 'ymw', rel: false });
db(0x16, 0xA0, 0x5E, 0xCD, 0, 0); fix.push({ at: pc - 2, n: 'ymw', rel: false });
ym(0x28, 0xF0);
db(0x78, 0xF6, 0x80); stA(0x7F11); db(0x3E, 0x0A); stA(0x7F11); db(0x3E, 0x92); stA(0x7F11); // PSG canal 0
jp(0xC3, 'loop');
label('songs');
const clock = 7670453;
const note = midi => { const f = 440 * Math.pow(2, (midi - 69) / 12); for (let blk = 0; blk < 8; blk++) { const fn = Math.round(f * 144 * Math.pow(2, 20) / clock / Math.pow(2, blk - 1)); if (fn < 2048) return [(blk << 3) | (fn >> 8), fn & 0xFF]; } };
const SONGS = [[60, 64, 67, 72, 67, 64, 60, 55], [57, 60, 64, 69, 64, 60, 57, 52], [72, 74, 76, 77, 79, 81, 83, 84]];
SONGS.forEach(s => s.forEach(n => db(...note(n))));
for (const f of fix) { const t = labels[f.n]; if (t === undefined) throw new Error('etiqueta ' + f.n); if (f.rel) { const d = t - (f.at + 1); if (d < -128 || d > 127) throw new Error('jr lejos ' + f.n); z[f.at] = d & 0xFF; } else { z[f.at] = t & 0xFF; z[f.at + 1] = t >> 8; } }
const zlen = pc;

// ---------- ROM 68000 ----------
const rom = new Uint8Array(0x20000).fill(0xFF);
const W = (a, v) => { rom[a] = (v >> 8) & 0xFF; rom[a + 1] = v & 0xFF; };
const L = (a, v) => { W(a, v >>> 16); W(a + 2, v & 0xFFFF); };
for (let v = 2; v < 64; v++) L(v * 4, 0x310);
L(0, 0x00FFFE00); L(4, 0x200); L(30 * 4, 0x300);
const S = (a, s, n) => { for (let i = 0; i < n; i++) rom[a + i] = i < s.length ? s.charCodeAt(i) : 0x20; };
S(0x100, 'SEGA MEGA DRIVE ', 16); S(0x110, '(C)NDRV 2026.NOV', 16);
S(0x120, 'NIGHTDRIVE TEST ROM', 48); S(0x150, 'NIGHTDRIVE TEST ROM', 48);
S(0x180, 'GM 00000000-00', 14); S(0x1F0, 'JUE', 16);
const code = [
  0x33FC, 0x0100, 0x00A1, 0x1100,   // move.w #$100,$A11100  (pedir bus)
  0x33FC, 0x0000, 0x00A1, 0x1200,   // move.w #0,$A11200     (Z80 en reset)
  0x41F9, 0x00A0, 0x0000,           // lea $A00000,a0
  0x43F9, 0x0000, 0x1000,           // lea $1000,a1 (driver)
  0x303C, zlen - 1,                 // move.w #len-1,d0
  0x10D9,                           // move.b (a1)+,(a0)+
  0x51C8, 0xFFFC,                   // dbf d0,-4
  0x33FC, 0x0100, 0x00A1, 0x1200,   // soltar reset
  0x33FC, 0x0000, 0x00A1, 0x1100,   // soltar bus
  0x33FC, 0x8164, 0x00C0, 0x0004,   // VDP reg1: display + VINT
  0x46FC, 0x2000,                   // move #$2000,sr
  0x0C79, 0x0028, 0x00FF, 0x0000,   // cmpi.w #40,$FF0000
  0x6DF6,                           // blt.s espera
  0x33FC, 0x0100, 0x00A1, 0x1100,   // pedir bus
  0x13FC, 0x0081, 0x00A0, 0x1F00,   // move.b #$81,$A01F00  (buzón)
  0x33FC, 0x0000, 0x00A1, 0x1100,   // soltar bus
  0x60FE                            // bra.s *
];
code.forEach((w, i) => W(0x200 + i * 2, w));
W(0x300, 0x5279); L(0x302, 0x00FF0000); W(0x306, 0x4E73); // addq.w #1,$FF0000 ; rte
W(0x310, 0x4E73);
rom.set(z.subarray(0, zlen), 0x1000);
fs.writeFileSync(process.argv[2], rom);
console.log('ROM', rom.length, 'bytes; driver Z80', zlen, 'bytes');
