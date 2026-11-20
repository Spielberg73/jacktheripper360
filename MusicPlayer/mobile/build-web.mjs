// Copia la app web (../src) a www/ para empaquetarla con Capacitor
import fs from 'fs';
import path from 'path';
const src = path.resolve('../src'), out = path.resolve('www');
fs.rmSync(out, { recursive: true, force: true });
fs.cpSync(src, out, { recursive: true });
// en Android el selector de archivos filtra mal por extensión: aceptamos cualquier archivo
const idx = path.join(out, 'index.html');
let html = fs.readFileSync(idx, 'utf8');
html = html.replace(/<input type="file" id="fileInput" multiple accept="[^"]*" hidden>/, '<input type="file" id="fileInput" multiple hidden>');
html = html.replace('<html lang="es">', '<html lang="es" class="mobile">');
// Capacitor inyecta su puente nativo como script en línea: hay que permitirlo
html = html.replace("script-src 'self'", "script-src 'self' 'unsafe-inline'");
// núcleo JS de Capacitor (registerPlugin) antes que la app
fs.copyFileSync(path.resolve('node_modules/@capacitor/core/dist/capacitor.js'), path.join(out, 'capacitor.js'));
html = html.replace('<script src="audio.js"></script>', '<script src="capacitor.js"></script>\n<script src="audio.js"></script>');
fs.writeFileSync(idx, html);
console.log('www listo');
