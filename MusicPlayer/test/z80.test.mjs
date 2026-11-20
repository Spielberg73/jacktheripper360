// Pruebas del núcleo Z80 con resultados de la documentación de Zilog.
// Ejecutar: node test/z80.test.mjs
import { Z80 } from '../src/md/z80.js';
const mem = new Uint8Array(65536);
const cpu = new Z80({ read: a => mem[a], write: (a, v) => { mem[a] = v; }, in: p => (p >> 8) & 0xFF, out: () => {} });
let pass = 0, fail = 0;
function run(name, setup, bytes, expect, steps = 1) {
  mem.fill(0); cpu.reset(); cpu.sp = 0xF000; cpu.f = 0;
  Object.assign(cpu, setup.regs || {});
  if (setup.mem) for (const [a, v] of setup.mem) mem[a] = v;
  bytes.forEach((b, i) => { mem[0x100 + i] = b; }); cpu.pc = 0x100;
  for (let i = 0; i < steps; i++) cpu.step();
  const bad = [];
  for (const [k, v] of Object.entries(expect)) {
    if (k === 'mem') { for (const [a, b] of v) if (mem[a] !== b) bad.push(`mem[${a.toString(16)}]=${mem[a].toString(16)} esperado ${b.toString(16)}`); continue; }
    let got = k === 'F' ? cpu.f & 0xD7 : cpu[k]; let exp = k === 'F' ? v & 0xD7 : v;
    if (got !== exp) bad.push(`${k}=${got.toString(16)} esperado ${exp.toString(16)}`);
  }
  if (bad.length) { fail++; console.log('FALLO', name, bad.join(', ')); } else pass++;
}
// ALU 8 bits
run('ADD 7F+1', { regs: { a: 0x7F } }, [0xC6, 0x01], { a: 0x80, F: 0x94 });
run('ADD FF+1', { regs: { a: 0xFF } }, [0xC6, 0x01], { a: 0x00, F: 0x51 });
run('ADC con carry', { regs: { a: 0x0E, f: 1 } }, [0xCE, 0x01], { a: 0x10, F: 0x10 });
run('SUB 0-1', { regs: { a: 0x00 } }, [0xD6, 0x01], { a: 0xFF, F: 0xBB });
run('SUB 80-1 overflow', { regs: { a: 0x80 } }, [0xD6, 0x01], { a: 0x7F, F: 0x16 | 0x28 });
run('SBC con carry', { regs: { a: 0x10, f: 1 } }, [0xDE, 0x0F], { a: 0x00, F: 0x52 });
run('CP igual', { regs: { a: 0x10 } }, [0xFE, 0x10], { a: 0x10, F: 0x42 });
run('CP menor', { regs: { a: 0x10 } }, [0xFE, 0x20], { a: 0x10, F: 0x83 });
run('AND', { regs: { a: 0xF0 } }, [0xE6, 0x0F], { a: 0, F: 0x54 });
run('XOR A', { regs: { a: 0x55 } }, [0xAF], { a: 0, F: 0x44 });
run('OR', { regs: { a: 0x80 } }, [0xF6, 0x01], { a: 0x81, F: 0x84 });
run('INC 7F', { regs: { a: 0x7F, f: 1 } }, [0x3C], { a: 0x80, F: 0x95 });
run('DEC 80', { regs: { a: 0x80 } }, [0x3D], { a: 0x7F, F: 0x16 });
run('DEC 01', { regs: { b: 0x01 } }, [0x05], { b: 0x00, F: 0x42 });
run('DAA tras ADD', { regs: { a: 0x09 } }, [0xC6, 0x01, 0x27], { a: 0x10, F: 0x10 }, 2);
run('DAA tras SUB', { regs: { a: 0x10 } }, [0xD6, 0x01, 0x27], { a: 0x09, F: 0x06 }, 2);
run('NEG', { regs: { a: 0x01 } }, [0xED, 0x44], { a: 0xFF, F: 0xBB });
run('CPL', { regs: { a: 0x0F } }, [0x2F], { a: 0xF0, F: 0x12 });
run('SCF/CCF', { regs: { a: 0 } }, [0x37, 0x3F], { F: 0x10 }, 2);
// 16 bits
run('ADD HL,BC', { regs: { h: 0x0F, l: 0xFF, b: 0, c: 1, f: 0xC4 } }, [0x09], { h: 0x10, l: 0x00, F: 0xC4 | 0x10 });
run('ADC HL,DE', { regs: { h: 0x7F, l: 0xFF, d: 0, e: 1 } }, [0xED, 0x5A], { h: 0x80, l: 0x00, F: 0x94 });
run('SBC HL,DE', { regs: { h: 0, l: 0, d: 0, e: 1 } }, [0xED, 0x52], { h: 0xFF, l: 0xFF, F: 0xBB });
run('INC BC wrap', { regs: { b: 0xFF, c: 0xFF } }, [0x03], { b: 0, c: 0 });
run('DEC SP', { regs: { sp: 0 } }, [0x3B], { sp: 0xFFFF });
// rotaciones / desplazamientos
run('RLCA', { regs: { a: 0x81 } }, [0x07], { a: 0x03, F: 0x01 });
run('RRA', { regs: { a: 0x01 } }, [0x1F], { a: 0x00, F: 0x01 });
run('RL B', { regs: { b: 0x80, f: 1 } }, [0xCB, 0x10], { b: 0x01, F: 0x01 });
run('SRA C', { regs: { c: 0x81 } }, [0xCB, 0x29], { c: 0xC0, F: 0x85 });
run('SRL (HL)', { regs: { h: 0x20, l: 0x00 }, mem: [[0x2000, 0x01]] }, [0xCB, 0x3E], { mem: [[0x2000, 0]], F: 0x45 });
run('BIT 7,A', { regs: { a: 0x80 } }, [0xCB, 0x7F], { F: 0x90 });
run('BIT 0,A cero', { regs: { a: 0x80 } }, [0xCB, 0x47], { F: 0x54 });
run('SET 3,(HL)', { regs: { h: 0x20, l: 0 } }, [0xCB, 0xDE], { mem: [[0x2000, 0x08]] });
run('RES 0,E', { regs: { e: 0xFF } }, [0xCB, 0x83], { e: 0xFE });
run('RLD', { regs: { a: 0x7A, h: 0x50, l: 0 }, mem: [[0x5000, 0x31]] }, [0xED, 0x6F], { a: 0x73, mem: [[0x5000, 0x1A]] });
// cargas, IX/IY
run('LD (IX+5),A', { regs: { a: 0x42, ix: 0x3000 } }, [0xDD, 0x77, 0x05], { mem: [[0x3005, 0x42]] });
run('LD B,(IY-2)', { regs: { iy: 0x3002 }, mem: [[0x3000, 0x99]] }, [0xFD, 0x46, 0xFE], { b: 0x99 });
run('LD (IX+1),n', { regs: { ix: 0x3000 } }, [0xDD, 0x36, 0x01, 0x77], { mem: [[0x3001, 0x77]] });
run('INC (IX+0)', { regs: { ix: 0x3000 }, mem: [[0x3000, 0xFF]] }, [0xDD, 0x34, 0x00], { mem: [[0x3000, 0]], F: 0x50 });
run('LD IXH,n', { regs: { ix: 0x1234 } }, [0xDD, 0x26, 0xAB], { ix: 0xAB34 });
run('ADD A,IXL', { regs: { a: 1, ix: 0x1234 } }, [0xDD, 0x85], { a: 0x35 });
run('LD H,(IX+0) usa H', { regs: { ix: 0x3000, h: 0 }, mem: [[0x3000, 0x55]] }, [0xDD, 0x66, 0x00], { h: 0x55, ix: 0x3000 });
run('SET 1,(IX+2)', { regs: { ix: 0x3000 } }, [0xDD, 0xCB, 0x02, 0xCE], { mem: [[0x3002, 0x02]] });
run('BIT 2,(IY+0)', { regs: { iy: 0x3000 }, mem: [[0x3000, 0x04]] }, [0xFD, 0xCB, 0x00, 0x56], { F: 0x10 });
run('LD HL,(nn)', { mem: [[0x4000, 0x34], [0x4001, 0x12]] }, [0x2A, 0x00, 0x40], { h: 0x12, l: 0x34 });
run('LD (nn),SP', { regs: { sp: 0xBEEF } }, [0xED, 0x73, 0x00, 0x40], { mem: [[0x4000, 0xEF], [0x4001, 0xBE]] });
run('LD IX,nn', {}, [0xDD, 0x21, 0x34, 0x12], { ix: 0x1234 });
run('EX (SP),HL', { regs: { h: 0x12, l: 0x34, sp: 0x5000 }, mem: [[0x5000, 0x78], [0x5001, 0x56]] }, [0xE3], { h: 0x56, l: 0x78, mem: [[0x5000, 0x34], [0x5001, 0x12]] });
// saltos y pila
run('JP nn', {}, [0xC3, 0x00, 0x20], { pc: 0x2000 });
run('JR -2', {}, [0x18, 0xFE], { pc: 0x100 });
run('JR NZ no salta', { regs: { f: 0x40 } }, [0x20, 0x10], { pc: 0x102 });
run('DJNZ', { regs: { b: 2 } }, [0x10, 0xFE], { b: 1, pc: 0x100 });
run('CALL/RET', {}, [0xCD, 0x10, 0x01], { pc: 0x110, sp: 0xEFFE, mem: [[0xEFFE, 0x03], [0xEFFF, 0x01]] });
run('CALL+RET', { mem: [[0x110, 0xC9]] }, [0xCD, 0x10, 0x01], { pc: 0x103, sp: 0xF000 }, 2);
run('PUSH AF/POP BC', { regs: { a: 0x12, f: 0x34 } }, [0xF5, 0xC1], { b: 0x12, c: 0x34 }, 2);
run('RST 38', {}, [0xFF], { pc: 0x38 });
run('JP (HL)', { regs: { h: 0x30, l: 0 } }, [0xE9], { pc: 0x3000 });
run('EXX', { regs: { b: 1, b_: 2 } }, [0xD9], { b: 2, b_: 1 });
// bloques
run('LDIR', { regs: { h: 0x20, l: 0, d: 0x30, e: 0, b: 0, c: 3 }, mem: [[0x2000, 1], [0x2001, 2], [0x2002, 3]] }, [0xED, 0xB0], { mem: [[0x3000, 1], [0x3001, 2], [0x3002, 3]], b: 0, c: 0, pc: 0x102 }, 3);
run('CPIR encuentra', { regs: { a: 2, h: 0x20, l: 0, b: 0, c: 5 }, mem: [[0x2000, 1], [0x2001, 2]] }, [0xED, 0xB1], { l: 2, c: 3, pc: 0x102 }, 2);
// interrupciones
{ mem.fill(0); cpu.reset(); cpu.sp = 0xF000; cpu.pc = 0x100; mem[0x100] = 0xFB; mem[0x101] = 0x00; mem[0x102] = 0x00; cpu.im = 1;
  cpu.setIRQ(true); cpu.step(); const pcAfterEI = cpu.pc; cpu.step(); const pc2 = cpu.pc; cpu.step();
  if (pcAfterEI === 0x101 && pc2 === 0x102 && cpu.pc === 0x38 && cpu.iff1 === 0) pass++; else { fail++; console.log('FALLO IM1 con retardo de EI', pcAfterEI.toString(16), pc2.toString(16), cpu.pc.toString(16)); } }
{ mem.fill(0); cpu.reset(); cpu.sp = 0xF000; cpu.pc = 0x100; mem[0x100] = 0x76; cpu.im = 1; cpu.iff1 = cpu.iff2 = 1;
  cpu.step(); cpu.step(); const halted = cpu.halted; cpu.setIRQ(true); cpu.step();
  if (halted && !cpu.halted && cpu.pc === 0x38 && mem[0xEFFE] === 0x01 && mem[0xEFFF] === 0x01) pass++; else { fail++; console.log('FALLO HALT+INT', halted, cpu.pc.toString(16), mem[0xEFFE], mem[0xEFFF]); } }
{ mem.fill(0); cpu.reset(); cpu.sp = 0xF000; cpu.pc = 0x100; cpu.im = 2; cpu.i = 0x40; cpu.iff1 = 1; mem[0x40FF] = 0x00; mem[0x4100] = 0x25;
  cpu.setIRQ(true); cpu.step(); if (cpu.pc === 0x2500) pass++; else { fail++; console.log('FALLO IM2', cpu.pc.toString(16)); } }
// ciclos
{ mem.fill(0); cpu.reset(); cpu.pc = 0x100; mem[0x100] = 0xDD; mem[0x101] = 0x7E; mem[0x102] = 0; const c = cpu.step(); if (c === 19) pass++; else { fail++; console.log('FALLO ciclos LD A,(IX+d)', c); } }
console.log(`Z80: ${pass} correctas, ${fail} fallos`);
process.exit(fail ? 1 : 0);
