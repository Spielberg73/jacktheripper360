// Pruebas del núcleo 68000 con programas ensamblados a mano.
// Ejecutar: node test/m68k.test.mjs
import { M68K } from '../src/md/m68k.js';
const mem = new Uint8Array(0x10000);
const bus = {
  read8: a => mem[a & 0xFFFF], read16: a => (mem[a & 0xFFFF] << 8) | mem[(a + 1) & 0xFFFF],
  write8: (a, v) => { mem[a & 0xFFFF] = v; }, write16: (a, v) => { mem[a & 0xFFFF] = v >> 8; mem[(a + 1) & 0xFFFF] = v & 0xFF; }
};
const cpu = new M68K(bus);
let pass = 0, fail = 0;
function w16(a, v) { mem[a] = v >> 8; mem[a + 1] = v & 0xFF; }
function load(words, at = 0x1000) { mem.fill(0); w16(0, 0); w16(2, 0x8000); w16(4, 0); w16(6, at); words.forEach((w, i) => w16(at + i * 2, w)); cpu.d.fill(0); cpu.a.fill(0); cpu.ccr = 0; cpu.setIRQ(0); cpu.reset(); }
function steps(n) { for (let i = 0; i < n; i++) cpu.run(1); }
function check(name, cond, info = '') { if (cond) pass++; else { fail++; console.log('FALLO', name, info); } }
const hex = v => (v >>> 0).toString(16);

load([0x70FF]); steps(1); check('MOVEQ -1', cpu.d[0] === -1 && cpu.n === 1 && cpu.z === 0, hex(cpu.d[0]));
load([0x223C, 0x1234, 0x5678]); steps(1); check('MOVE.L #imm,D1', (cpu.d[1] >>> 0) === 0x12345678);
load([0x7001, 0x223C, 0x7FFF, 0xFFFF, 0xD280]); steps(3); check('ADD.L desborde', (cpu.d[1] >>> 0) === 0x80000000 && cpu.v === 1 && cpu.n === 1 && cpu.c === 0, hex(cpu.d[1]));
load([0x7001, 0x7200, 0x9240]); steps(3); check('SUB.W 0-1', (cpu.d[1] & 0xFFFF) === 0xFFFF && cpu.c === 1 && cpu.x === 1 && cpu.n === 1);
// suma 9..0 con DBF
load([0x7000, 0x7209, 0xD041, 0x51C9, 0xFFFC]); steps(2 + 20); check('Bucle DBF suma=45', cpu.d[0] === 45 && (cpu.d[1] & 0xFFFF) === 0xFFFF, cpu.d[0]);
load([0x303C, 300, 0xC0FC, 200]); steps(2); check('MULU 300*200', cpu.d[0] === 60000, cpu.d[0]);
load([0x303C, 0xFFFF, 0xC1FC, 0x0002]); steps(2); check('MULS -1*2', cpu.d[0] === -2, cpu.d[0]);
load([0x203C, 0x0001, 0x86A0, 0x80FC, 7]); steps(2); check('DIVU 100000/7', (cpu.d[0] >>> 0) === 0x000537CD, hex(cpu.d[0]));
load([0x203C, 0xFFFF, 0xFFF9, 0x81FC, 2]); steps(2); check('DIVS -7/2', (cpu.d[0] >>> 0) === 0xFFFFFFFD, hex(cpu.d[0]));
// MOVEM ida y vuelta
load([0x7011, 0x7222, 0x207C, 0x0000, 0x3333, 0x48E7, 0xC080, 0x7000, 0x7200, 0x91C8, 0x4CDF, 0x0103]);
steps(8); check('MOVEM.L guardar/restaurar', cpu.d[0] === 0x11 && cpu.d[1] === 0x22 && cpu.a[0] === 0x3333 && (cpu.a[7] >>> 0) === 0x8000, `${hex(cpu.d[0])} ${hex(cpu.d[1])} ${hex(cpu.a[0])} sp=${hex(cpu.a[7])}`);
// JSR / RTS: jsr $1100 ; moveq #5,d2 ;  en $1100: moveq #7,d3 ; rts
load([0x4EB9, 0x0000, 0x1100, 0x7405]); w16(0x1100, 0x7607); w16(0x1102, 0x4E75);
steps(4); check('JSR/RTS', cpu.d[2] === 5 && cpu.d[3] === 7 && (cpu.a[7] >>> 0) === 0x8000);
// BSR.S / LEA / PEA
load([0x41F9, 0x00AB, 0xCDEF]); steps(1); check('LEA abs.L', (cpu.a[0] >>> 0) === 0xABCDEF);
load([0x6100, 0x0004, 0x4E71, 0x7801, 0x4E75]); steps(1); check('BSR.W', cpu.pc === 0x1006 && (cpu.a[7] >>> 0) === 0x7FFC);
// desplazamientos
load([0x303C, 0x0123, 0xE948]); steps(2); check('LSL.W #4', (cpu.d[0] & 0xFFFF) === 0x1230, hex(cpu.d[0]));
load([0x203C, 0x8000, 0x0001, 0xE280]); steps(2); check('ASR.L #1', (cpu.d[0] >>> 0) === 0xC0000000 && cpu.c === 1 && cpu.x === 1, hex(cpu.d[0]));
load([0x7081, 0xE318]); steps(2); check('ROL.B #1', (cpu.d[0] & 0xFF) === 0x03 && cpu.c === 1, hex(cpu.d[0]));
load([0x7040, 0x44FC, 0x0010, 0xE310]); steps(3); check('ROXL.B con X', (cpu.d[0] & 0xFF) === 0x81 && cpu.c === 0, hex(cpu.d[0]));
load([0x7001, 0xE14A /* lsl.w #8,d2 */]); steps(2); check('LSL #8 (count 0=8)', true);
// BCD
load([0x7019, 0x7228, 0xC101]); steps(3); check('ABCD 19+28=47', (cpu.d[0] & 0xFF) === 0x47, hex(cpu.d[0]));
load([0x7047, 0x7228, 0x8101]); steps(3); check('SBCD 47-28=19', (cpu.d[0] & 0xFF) === 0x19, hex(cpu.d[0]));
// varios
load([0x203C, 0x1234, 0x5678, 0x4840]); steps(2); check('SWAP', (cpu.d[0] >>> 0) === 0x56781234);
load([0x7080, 0x4880]); steps(2); check('EXT.W', (cpu.d[0] & 0xFFFF) === 0xFF80);
load([0x7000, 0x08C0, 0x0005, 0x0800, 0x0005]); steps(3); check('BSET/BTST', cpu.d[0] === 32 && cpu.z === 0);
load([0x7005, 0xB03C, 0x0005, 0x57C1]); steps(3); check('CMP + SEQ', (cpu.d[1] & 0xFF) === 0xFF);
load([0x7005, 0x0C40, 0x0006, 0x6602, 0x7401, 0x7602]); steps(4); check('CMPI + BNE', cpu.d[2] === 0 && cpu.d[3] === 2);
load([0x31FC, 0xBEEF, 0x2000, 0x3238, 0x2000]); steps(2); check('MOVE.W abs.W', (cpu.d[1] & 0xFFFF) === 0xBEEF && mem[0x2000] === 0xBE);
load([0x207C, 0, 0x2000, 0x30FC, 0x1111, 0x30FC, 0x2222, 0x3220]); steps(4); check('(A0)+ y -(A0)', (cpu.d[1] & 0xFFFF) === 0x2222 && (cpu.a[0] >>> 0) === 0x2002);
load([0x41FA, 0x0010]); steps(1); check('LEA d16(PC)', (cpu.a[0] >>> 0) === 0x1012, hex(cpu.a[0]));
load([0x207C, 0, 0x2000, 0x7204, 0x1030, 0x1002]); mem[0x2006] = 0x5A; steps(3); check('d8(A0,D1.W)', (cpu.d[0] & 0xFF) === 0x5A);
load([0x4E56, 0xFFF0, 0x4E5E]); steps(1); check('LINK', (cpu.a[6] >>> 0) === 0x7FFC && (cpu.a[7] >>> 0) === 0x7FEC); steps(1); check('UNLK', (cpu.a[7] >>> 0) === 0x8000);
load([0x7001, 0x4A80, 0x4280]); steps(3); check('TST/CLR', cpu.d[0] === 0 && cpu.z === 1);
load([0x7003, 0x4480]); steps(2); check('NEG.L', cpu.d[0] === -3 && cpu.c === 1);
load([0x7203, 0xC342 /* exg d1,d2 */]); steps(2); check('EXG', cpu.d[2] === 3 && cpu.d[1] === 0);
// excepciones: TRAP #0 -> vector 32 (0x80)
load([0x4E40, 0x7401]); w16(0x80, 0); w16(0x82, 0x1200); w16(0x1200, 0x7605); w16(0x1202, 0x4E73);
steps(4); check('TRAP + RTE', cpu.d[3] === 5 && cpu.d[2] === 1 && cpu.s === 1 && (cpu.a[7] >>> 0) === 0x8000, `pc=${hex(cpu.pc)}`);
// interrupción nivel 6 (vector 30 = 0x78)
load([0x46FC, 0x2000, 0x4E71, 0x4E71]); w16(0x78, 0); w16(0x7A, 0x1300); w16(0x1300, 0x7A09); w16(0x1302, 0x4E73);
steps(1); cpu.setIRQ(6); steps(1); check('Interrupción nivel 6', cpu.mask === 6 && cpu.d[5] === 9, `pc=${hex(cpu.pc)} d5=${cpu.d[5]}`);
cpu.setIRQ(0); steps(1); check('RTE de la interrupción', cpu.mask === 0 && cpu.pc === 0x1004, hex(cpu.pc));
// STOP espera a la interrupción
load([0x4E72, 0x2000, 0x7C01]); w16(0x78, 0); w16(0x7A, 0x1300); w16(0x1300, 0x4E73);
steps(3); const st = cpu.stopped; cpu.setIRQ(6); steps(1); cpu.setIRQ(0); steps(2); check('STOP + IRQ', st && cpu.d[6] === 1);
// privilegio: en modo usuario MOVE #,SR -> vector 8
load([0x46FC, 0x0000, 0x46FC, 0x2700]); w16(0x20, 0); w16(0x22, 0x1400); steps(2); check('Violación de privilegio', cpu.pc === 0x1400 && cpu.s === 1, hex(cpu.pc));
console.log(`68000: ${pass} correctas, ${fail} fallos`);
process.exit(fail ? 1 : 0);
