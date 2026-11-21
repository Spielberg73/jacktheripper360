// NightDrive Player — sensores del coche (modo coche en el móvil)
// · Velocidad real con el GPS (Geolocation API).
// · Giro del coche con el giroscopio: la velocidad de giro se proyecta sobre la
//   gravedad, así da igual cómo esté colocado el móvil en el soporte.
(function () {
  const D = {
    active: false,
    speed: null,      // km/h (GPS)
    yaw: 0,           // grados/s, positivo = giro a la izquierda
    gpsAt: 0, motionAt: 0,
    error: null,
    get gpsOk() { return this.active && this.speed !== null && performance.now() - this.gpsAt < 6000; },
    get sensorOk() { return this.active && performance.now() - this.motionAt < 1500; }
  };
  let g = [0, 0, 9.8], watchId = null, last = null;

  function haversine(a, b) {
    const R = 6371000, toR = Math.PI / 180;
    const dLat = (b.latitude - a.latitude) * toR, dLon = (b.longitude - a.longitude) * toR;
    const h = Math.sin(dLat / 2) ** 2 + Math.cos(a.latitude * toR) * Math.cos(b.latitude * toR) * Math.sin(dLon / 2) ** 2;
    return 2 * R * Math.asin(Math.sqrt(h));
  }

  function onMotion(e) {
    const a = e.accelerationIncludingGravity, r = e.rotationRate;
    if (!a || !r || a.x === null || a.x === undefined) return;
    // gravedad filtrada (apunta hacia arriba en coordenadas del móvil)
    g = [g[0] * 0.92 + a.x * 0.08, g[1] * 0.92 + a.y * 0.08, g[2] * 0.92 + a.z * 0.08];
    const n = Math.hypot(g[0], g[1], g[2]) || 1;
    // rotationRate: alpha = eje z, beta = eje x, gamma = eje y (grados/s)
    const yaw = ((r.beta || 0) * g[0] + (r.gamma || 0) * g[1] + (r.alpha || 0) * g[2]) / n;
    D.yaw += (yaw - D.yaw) * 0.12;
    D.motionAt = performance.now();
  }

  function onPos(p) {
    let v = p.coords.speed;
    if ((v === null || v === undefined || isNaN(v)) && last) {
      const dt = (p.timestamp - last.t) / 1000;
      if (dt > 0.5) v = haversine(last.c, p.coords) / dt;
    }
    last = { c: p.coords, t: p.timestamp };
    if (v === null || v === undefined || isNaN(v)) return;
    const kmh = Math.max(0, v * 3.6);
    D.speed = D.speed === null ? kmh : D.speed + (kmh - D.speed) * 0.5;
    if (D.speed < 1.5) D.speed = 0;
    D.gpsAt = performance.now();
    D.error = null;
  }

  D.start = function () {
    if (D.active) return;
    D.active = true;
    window.addEventListener('devicemotion', onMotion);
    if (navigator.geolocation) {
      try {
        watchId = navigator.geolocation.watchPosition(onPos, err => { D.error = err.message || 'GPS'; },
          { enableHighAccuracy: true, maximumAge: 1000, timeout: 20000 });
      } catch (e) { D.error = String(e); }
    }
  };
  D.stop = function () {
    D.active = false;
    window.removeEventListener('devicemotion', onMotion);
    if (watchId !== null && navigator.geolocation) navigator.geolocation.clearWatch(watchId);
    watchId = null; last = null;
    D.speed = null; D.yaw = 0;
  };

  window.NDDrive = D;
})();
