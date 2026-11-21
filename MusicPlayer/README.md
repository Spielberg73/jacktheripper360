# NightDrive Player

Reproductor de música para Windows con aspecto de **autorradio 1‑DIN de los 2000**: frontal con rueda de volumen iluminada, joystick multicontrol, teclas 1‑6, pantalla de matriz de puntos animada... y un **ecualizador gráfico de 31 bandas** con analizador de espectro en tiempo real.

## Características

**Frontal**
- 8 frontales intercambiables: Stealth Red, Arctic Blue, Carbon Green, Neon Rainbow (iluminación que cambia sola), Titanium Orange, Violet Night, Champagne Gold y Pearl White.
- Iluminación de teclas personalizable (cualquier color) y color de pantalla independiente (ámbar, azul, verde, rojo, blanco, cian, arcoíris).
- Pantalla de matriz de puntos 256×64 con animación de bienvenida y 11 modos (botón **DISP**, la tecla **D** o clic en la pantalla): Spectrum, Level Meter, Scope, Speaker, Tunnel, Full Spectrum, Clock, FM Channels y tres escenas animadas que reaccionan a la música: **Night Sky** (cielo estrellado con luna, montañas y estrellas fugaces), **Highway** (carretera nocturna con farolas, ciudad al fondo y velocímetro) y **Harbor** (puerto pesquero con barcas, faro, gaviotas y reflejo de la luna).
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

**Novedades de esta versión**
- **Crossfade**: las canciones se funden entre sí (0–12 s, ajustable en FRONTALES). No se aplica a VGM, ROM ni radio.
- **Modo noche automático**: atenúa el frontal y la pantalla entre la puesta y la salida del sol, calculadas con la ubicación del GPS o, si no la hay, con la de España. Opciones AUTO / SIEMPRE / NUNCA.
- **Listas guardadas**: guarda la playlist con un nombre, cárgala o bórrala. Ordena por título, artista o álbum.
- **Buscador de radios**: miles de emisoras de todo el mundo (directorio abierto Radio Browser), por nombre, país o estilo.
- **Grabar la radio**: botón **REC**. Graba lo que suena en MP3 a 192 kbps y para sola al cambiar de canción. El archivo se llama "Artista - Canción.mp3" y se guarda en Música\NightDrive (PC) o en la biblioteca y Descargas/NightDrive (móvil).
- **Presintonías**: mantén pulsada una tecla 1–6 para guardar ahí la emisora que suena.
- **Exportar** canciones de VGM y de ROMs a MP3 o WAV (botón EXPORTAR en la playlist).
- **Títulos de canciones**: Sonic 2 trae sus títulos reales. Cualquier pista se puede renombrar tocando su título en "Ahora suena", y el nombre se recuerda para esa ROM.
- **Escenas nuevas**: lluvia con limpiaparabrisas, túnel con luces naranjas, ciudad de neón (con el espectro en una pantalla gigante) y atardecer en la playa.
- **Pitido de autorradio** al arrancar y al apagar. Al cerrar, el frontal se suelta y cae.
- **Editor de frontal**: colores de la placa, acabado (brillo, aluminio cepillado o carbono), teclas, pantalla y logo, con vista previa en directo.

**Radio por internet**
- Se enciende con el botón **RADIO** de la barra de reproducción (junto al play), o con **SRC** en el frontal, como en el coche. Ese mismo botón pasa a llamarse **DISC** para volver a tu música.
- Presintonías en las teclas **1–6**: LOS40 Classic, LOS40, LOS40 Dance, LOS40 Urban, Cadena Dial y Radiolé. En la lista hay también Cadena SER. **◀◀ / ▶▶** pasan a la emisora anterior o siguiente.
- La pantalla muestra la emisora, el artista y la canción que suenan, con un indicador ON AIR. La pestaña **RADIO** enseña además la carátula y lo que suena ahora en cada emisora.
- La radio pasa por el ecualizador de 31 bandas y por los visualizadores.
- Puedes añadir cualquier emisora con la URL de su stream. Si esa emisora no permite procesar su audio, suena igualmente, pero sin ecualizador.
- Si la conexión se corta, reconecta sola. Si pausas más de 15 s, al volver retoma en directo.

**Música de Mega Drive (VGM / VGZ)**
- Emulación propia del chip FM **Yamaha YM2612** (6 canales, 8 algoritmos, feedback, LFO, modo especial del canal 3 y DAC de samples) y del **PSG SN76489** (3 canales cuadrados + ruido), dentro de un AudioWorklet.
- Lee las etiquetas GD3 (título, juego, compositor) y respeta los bucles: toca la canción, repite el bucle una vez y hace un fundido de 8 s.
- Soporta samples de DAC por bloques de datos y por *DAC streams*.
- Modo de pantalla **FM CHANNELS** con medidores de los 6 canales FM y los 4 del PSG.
- Pasa por el mismo ecualizador de 31 bandas que el resto de pistas.
- Las bandas sonoras en VGM se encuentran, por ejemplo, en vgmrips.net. No incluye ninguna.
- Limitación: el SSG-EG del YM2612 (lo usan pocos juegos) no está emulado todavía.

**ROMs de Mega Drive (nuevo en 1.2)**
- Abre una ROM (`.md`, `.gen`, `.bin`, `.smd`, también dentro de un `.zip`) y saca sus canciones como pistas.
- Emula la consola entera sin vídeo: procesadores **68000** y **Z80** propios, el mapa de memoria, el arbitraje del bus, el VDP (interrupciones), los timers del YM2612 y los mandos.
- **Cómo encuentra las canciones**: arranca el juego y observa cómo el 68000 le pide la música al driver de sonido del Z80, que la toca al escribir un número en un "buzón" de la RAM del Z80. Guarda el estado de la consola justo antes de esa petición y, desde ahí, prueba los 255 valores posibles y escucha qué hace el driver con cada uno. Si suenan notas en varios canales durante segundos es una canción; si duran poco, un jingle; si no suena nada, lo descarta. Las repetidas se eliminan.
- El análisis se hace en segundo plano (Web Worker) y se guarda en caché, así que la segunda vez es instantáneo.
- No depende del tipo de driver: aprende la "transacción" con la que el juego pide música (un buzón de un byte, o una cola de comandos de varios bytes como la del driver **GEMS** que usan Aladdin y muchos juegos americanos) y la repite cambiando solo el número de canción. También detecta los drivers que reciben la canción como **parámetro al cargarse** (como el de Sound Images en Shadow of the Beast): cambia el parámetro y reinicia el driver.
- Funciona con los juegos cuyo driver de sonido corre en el Z80, que son la mayoría. Si el juego toca la música desde el 68000 (por ejemplo, el primer Sonic), o no se encuentra el buzón, se añade una sola pista en **modo libre**, que reproduce el juego tal cual arranca (logo, título, demo).
- Las canciones se llaman "Track 01", "Track 02"… porque la ROM no guarda sus títulos. Las que se repiten en bucle duran 2:30 con un fundido final.
- No incluye ninguna ROM. Usa tus propios volcados.

**Reproductor**
- MP3, FLAC, WAV, OGG, Opus, M4A/AAC, VGM, VGZ y ROMs de Mega Drive.
- **ZIP**: abre los `.zip` y añade todas las canciones que tengan dentro (MP3, FLAC, VGZ, ROMs…).
- En la playlist: un clic selecciona, doble clic o el botón ▶ de la fila reproduce, y Enter reproduce la selección.
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

## Android

La carpeta `mobile/` empaqueta la misma app con Capacitor (Android 6.0 o superior):

- La interfaz se adapta al móvil. En vertical, el frontal queda arriba y debajo van el ecualizador (los deslizadores se arrastran en horizontal) y la playlist. En horizontal parece un frontal de verdad.
- **Modo coche** (botón de arriba): pantalla completa solo con el frontal, la pantalla siempre encendida y la escena **HIGHWAY**:
  - el velocímetro marca la **velocidad real del GPS** y la carretera avanza a ese ritmo (parado en un semáforo, la carretera se para y se encienden las luces de freno del coche de delante);
  - la carretera **se curva cuando el coche gira**, gracias al giroscopio del móvil. La ciudad y la luna se desplazan en el horizonte. Funciona coloques como coloques el móvil en el soporte.
- **Botón de apagado** (arriba a la derecha): dos toques para salir del todo de la app, con la música y la notificación incluidas.
- **Biblioteca**: los archivos que importas (MP3, ZIP, VGZ, ROMs…) se guardan dentro de la app, así que siguen ahí al volver a abrirla.
- **Segundo plano**: un servicio de Android mantiene la música con la pantalla apagada. La notificación tiene anterior/pausa/siguiente, y responde a los botones multimedia de los auriculares y del Bluetooth del coche.
- Un toque en una canción la reproduce. Una pulsación larga la selecciona, para poder QUITARLA.
- El botón "Atrás" deja la app en segundo plano en lugar de cerrarla.

Para compilarla: `cd mobile && npm install && npm run apk` (necesita el SDK de Android y Java 21). El workflow **NightDrive Player (Android APK)** de GitHub Actions la compila sola en cada cambio.

## Pruebas

```bat
npm test
```

Comprueba el Z80 (63 casos) y el 68000 (39 casos) con resultados de la documentación de los fabricantes. Después genera una ROM de Mega Drive original de prueba, con un driver de sonido propio de dos canciones y un jingle. Por último verifica que el escáner encuentra las tres y que la primera nota suena a la frecuencia correcta.

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
    ├── md/           m68k.js, z80.js, megadrive.js, scan.js y md-player.js (ROMs)
    ├── rom.js / rom-worker.js   análisis de ROMs en segundo plano
    ├── zip.js        lector de ZIP
    ├── drive.js      GPS y giroscopio (modo coche)
    ├── mp3-worker.js / rec-worklet.js   grabación de radio y exportación a MP3/WAV
    └── vendor/       lamejs (codificador MP3, LGPL-3.0)
    ├── display.js    pantalla de matriz de puntos y sus animaciones
    ├── eq.js         ecualizador gráfico y analizador
    └── app.js        playlist, reproducción, frontales y controles
```

El diseño y la marca "NightDrive" son originales y están inspirados en el estilo general de las autorradios de la época. No reproducen ningún modelo ni marca comercial.
