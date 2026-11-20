# NightDrive Player

Reproductor de música para Windows con aspecto de **autorradio 1‑DIN de los 2000**: frontal con rueda de volumen iluminada, joystick multicontrol, teclas 1‑6, pantalla de matriz de puntos animada... y un **ecualizador gráfico de 31 bandas** con analizador de espectro en tiempo real.

## Características

**Frontal**
- 8 frontales intercambiables: Stealth Red, Arctic Blue, Carbon Green, Neon Rainbow (iluminación que cambia sola), Titanium Orange, Violet Night, Champagne Gold y Pearl White.
- Iluminación de teclas personalizable (cualquier color) y color de pantalla independiente (ámbar, azul, verde, rojo, blanco, cian, arcoíris).
- Pantalla de matriz de puntos 256×64 con animación de bienvenida y 8 modos (botón **DISP**, la tecla **D** o clic en la pantalla): Spectrum, Level Meter, Scope, Speaker, Tunnel, Full Spectrum, Clock y FM Channels.
- Botón **OPEN**: el frontal se abate y deja ver la ranura del disco para añadir música (o soltar archivos encima).
- Rueda de volumen 0‑40 (rueda del ratón o girarla arrastrando; un clic hace play/pausa).
- Teclas 1‑6 como en el coche: **1** RPT (pista/todo), **2** RDM, **3** SCAN (10 s de cada pista), **4** PAUSE, **5/6** −10 s / +10 s.
- Joystick: ◀◀ / ▶▶ (mantener pulsado = avance rápido), ▲ playlist, ▼ ecualizador, centro play/pausa.
- **SRC** apaga o enciende la pantalla, **EQ** pasa de preset, **AUDIO** cambia el loudness e **ILLUMI** cambia de frontal.

**Ecualizador**
- 31 bandas ISO de 1/3 de octava (±12 dB), o modo de 10 bandas; al cambiar de modo se conserva la forma de la curva.
- Gráfica con analizador LED por banda, picos que se quedan arriba un momento y la curva de respuesta **real** calculada a partir de los filtros.
- Puedes **dibujar la curva arrastrando el ratón** sobre la gráfica.
- Presets: Flat, Powerful, Natural, Vocal, Super Bass, Rock, Club, V‑Shape, Treble, **Brutal**, y dos memorias Custom.
- Preamp, Bass Boost, Loudness (Off/Low/Mid/High), balance y modo bypass.
- Limitador al final de la cadena para que los presets extremos no saturen.

**Música de Mega Drive (VGM / VGZ)**
- Emulación propia del chip FM **Yamaha YM2612** (6 canales, 8 algoritmos, feedback, LFO, modo especial del canal 3 y DAC de samples) y del **PSG SN76489** (3 canales cuadrados + ruido), dentro de un AudioWorklet.
- Lee las etiquetas GD3 (título, juego, compositor) y respeta los bucles: toca la canción, repite el bucle una vez y hace un fundido de 8 s.
- Soporta samples de DAC por bloques de datos y por *DAC streams*.
- Modo de pantalla **FM CHANNELS** con medidores de los 6 canales FM y los 4 del PSG.
- Pasa por el mismo ecualizador de 31 bandas que el resto de pistas.
- Las bandas sonoras en VGM se encuentran, por ejemplo, en vgmrips.net. No incluye ninguna.
- Limitación: el SSG-EG del YM2612 (lo usan pocos juegos) no está emulado todavía.

**Reproductor**
- MP3, FLAC, WAV, OGG, Opus, M4A/AAC, VGM y VGZ.
- Lee las etiquetas y la carátula (título, artista, álbum, duración, códec y bitrate).
- Playlist con búsqueda, añadir archivos o carpetas (recursivo) y arrastrar y soltar.
- Teclas multimedia del teclado, barra de progreso en la barra de tareas y modo compacto (solo el frontal).
- Siempre encima. Recuerda la playlist, la pista, la posición, el EQ y el frontal al cerrarlo.

## Atajos

| Tecla | Acción |
|---|---|
| Espacio | Play / Pausa |
| ← / → | −5 s / +5 s |
| Ctrl + ← / → | Pista anterior / siguiente |
| ↑ / ↓ | Volumen |
| D | Modo de pantalla |
| Supr | Quitar las pistas seleccionadas |
| Ctrl + O | Añadir archivos |

## Ejecutar y compilar (Windows)

Necesitas [Node.js](https://nodejs.org) 20 o superior.

```bat
cd MusicPlayer
npm install
npm start          :: lo abre en modo desarrollo
npm run dist       :: genera el instalador y la versión portable en MusicPlayer\dist\
```

`npm run dist` deja dos archivos en `dist\`:
- `NightDrive Player Setup 1.0.0.exe`: instalador con acceso directo en el escritorio y asociación de archivos de audio.
- `NightDrive-Player-1.0.0-portable.exe`: un solo `.exe` que no necesita instalación.

Además, cada push que toca `MusicPlayer/` lanza el workflow **NightDrive Player (Windows build)** en GitHub Actions. El workflow compila los dos `.exe` y los publica como artefacto (`NightDrive-Player-Windows`).

## Estructura

```
MusicPlayer/
├── main.js        proceso principal de Electron (ventana, diálogos, etiquetas, teclas multimedia)
├── preload.js     puente seguro entre la ventana y el sistema
├── assets/icon.png
└── src/
    ├── index.html
    ├── styles.css    frontal, teclas, rueda y paneles
    ├── audio.js      motor Web Audio: 31 filtros peaking, shelves, limitador, analizadores
    ├── vgm.js        carga de VGM/VGZ y puente con el AudioWorklet
    ├── vgm-worklet.js
    ├── chips/        ym2612.js, sn76489.js y vgm-core.js (reproductor VGM)
    ├── display.js    pantalla de matriz de puntos y sus animaciones
    ├── eq.js         ecualizador gráfico y analizador
    └── app.js        playlist, reproducción, frontales y controles
```

El diseño y la marca "NightDrive" son originales y están inspirados en el estilo general de las autorradios de la época. No reproducen ningún modelo ni marca comercial.
