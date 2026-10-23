# Brujita de la Suerte

App web para móvil (PWA) que genera combinaciones al azar para el Euromillones: 5 números del 1 al 50 y 2 estrellas del 1 al 12.

- Brujita y gato animados en SVG/CSS (parpadean, flotan, la cola se mueve).
- Sonidos generados con Web Audio: campanitas del hechizo, una nota por bola, maullido del gato y risita de la brujita.
- Toca la bola de cristal o el botón **Lanzar hechizo**. Toca al gato o a la brujita para que reaccionen.
- Historial de las últimas 10 combinaciones (se guarda en el propio móvil).
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
