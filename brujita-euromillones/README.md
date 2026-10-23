# Brujita de la Suerte

App para móvil que genera combinaciones al azar para los sorteos de Loterías:

| Sorteo | Combinación |
|---|---|
| Euromillones | 5 números (1–50) + 2 estrellas (1–12) |
| La Primitiva | 6 números (1–49) |
| Bonoloto | 6 números (1–49) |
| El Gordo de la Primitiva | 5 números (1–54) + número clave (0–9) |
| EuroDreams | 6 números (1–40) + 1 sueño (1–5) |
| Lotería Nacional | número de 5 cifras para buscar un décimo |

Se elige el sorteo en el menú principal. Los sorteos se definen en la lista `GAMES` de `www/app.js`, así que añadir otro es añadir una entrada.

- Brujita y gato animados en SVG/CSS (parpadean, flotan, la cola se mueve).
- Música de fondo mágica: un vals de caja de música en re menor, sintetizado en bucle (botón con la nota para quitarla).
- Sonidos generados con Web Audio: campanitas del hechizo, una nota por bola, maullido del gato y risita de la brujita.
- Toca la bola de cristal o el botón **Lanzar hechizo**. Toca al gato o a la brujita para que reaccionen.
- **Modo Trébol 🍀**: sigue siendo azar, pero descarta las combinaciones que juega mucha gente (casi todo fechas del 1 al 31, tres números seguidos, progresiones, muchos en la misma decena o con la misma terminación; en Lotería Nacional, capicúas, fechas y terminaciones como 7, 13, 69 o 00) y las que ya te salieron. No aumenta la probabilidad de acertar: si toca, el premio se reparte con menos ganadores. La regla está en `isPopular()` de `www/app.js`.
- Historial de las últimas 10 combinaciones de cada sorteo (se guarda en el propio móvil).
- Botón para copiar o compartir la combinación.
- Instalable en la pantalla de inicio y funciona sin conexión (manifest + service worker).

Los números salen de `crypto.getRandomValues` sin sesgo: son puro azar y no mejoran las probabilidades.

## Estructura

- `www/`: la app (HTML, CSS, JS, iconos y fuentes locales). Es lo mismo que se ve en el navegador.
- `android/`: proyecto Android generado con [Capacitor](https://capacitorjs.com) que mete `www/` dentro de una app nativa.

## App de Android (APK)

Requisitos: Node 20+, JDK 21 y el SDK de Android (`ANDROID_HOME`).

```sh
cd brujita-euromillones
npm ci
npm run apk   # copia www/ al proyecto Android y compila
```

El APK queda en `android/app/build/outputs/apk/debug/app-debug.apk`. Pásalo al móvil y ábrelo (Android pedirá permitir "instalar apps de origen desconocido").

GitHub Actions también compila el APK en cada cambio de esta carpeta (workflow **Brujita APK**); se descarga desde la pestaña *Actions* → la ejecución → *Artifacts*.

Para publicarla en Google Play hace falta una versión firmada: en Android Studio, *Build → Generate Signed App Bundle*.

## Probarla en el navegador

```sh
cd brujita-euromillones
npm run serve
```

Abre `http://localhost:8080`. Para instalarla como PWA en el móvil, súbela con HTTPS (GitHub Pages, Netlify…).
