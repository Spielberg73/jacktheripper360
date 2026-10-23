# Brujita de la Suerte

App web para móvil (PWA) que genera combinaciones al azar para el Euromillones: 5 números del 1 al 50 y 2 estrellas del 1 al 12.

- Brujita y gato animados en SVG/CSS (parpadean, flotan, la cola se mueve).
- Sonidos generados con Web Audio: campanitas del hechizo, una nota por bola, maullido del gato y risita de la brujita.
- Toca la bola de cristal o el botón **Lanzar hechizo**. Toca al gato o a la brujita para que reaccionen.
- Historial de las últimas 10 combinaciones (se guarda en el propio móvil).
- Botón para copiar o compartir la combinación.
- Instalable en la pantalla de inicio y funciona sin conexión (manifest + service worker).

Los números salen de `crypto.getRandomValues` sin sesgo: son puro azar y no mejoran las probabilidades.

## Probarla

```sh
cd brujita-euromillones
python3 -m http.server 8080
```

Abre `http://localhost:8080` (o sube la carpeta a GitHub Pages / Netlify para usarla con HTTPS e instalarla en el móvil).
