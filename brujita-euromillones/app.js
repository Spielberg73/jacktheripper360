(() => {
  "use strict";

  const NUMBERS = 5, NUMBER_MAX = 50, STARS = 2, STAR_MAX = 12, HISTORY_MAX = 10;
  const $ = (id) => document.getElementById(id);
  const scene = $("scene"), bubble = $("bubble"), balls = $("balls"), hint = $("hint");
  const castBtn = $("castBtn"), shareBtn = $("shareBtn"), soundBtn = $("soundBtn");
  const witch = $("witch"), cat = $("cat"), orb = $("orb"), sparks = $("sparks");
  const histList = $("histList"), histEmpty = $("histEmpty"), clearBtn = $("clearBtn");

  const PHRASES = [
    "¡Abracadabra, números a la vista!",
    "Mi bola de cristal nunca duerme…",
    "Misifú dice que hoy es tu noche.",
    "¡Que la magia te acompañe!",
    "Estrellas, estrellitas… ¡salid!",
    "Un poquito de polvo de luna y listo.",
    "Shhh… las estrellas están hablando.",
  ];
  const CAT_PHRASES = ["¡Miau!", "Miaaau~", "Prrrr…", "¡Miau miau!"];

  /* ---------- Azar uniforme con crypto ---------- */
  function randInt(max) { // 1..max sin sesgo
    const buf = new Uint32Array(1);
    const limit = Math.floor(0x100000000 / max) * max;
    let x;
    do { crypto.getRandomValues(buf); x = buf[0]; } while (x >= limit);
    return (x % max) + 1;
  }
  function pick(count, max) {
    const set = new Set();
    while (set.size < count) set.add(randInt(max));
    return [...set].sort((a, b) => a - b);
  }
  const pad = (n) => String(n).padStart(2, "0");

  /* ---------- Almacenamiento seguro ---------- */
  const store = {
    get(key, fallback) {
      try { const v = localStorage.getItem(key); return v === null ? fallback : JSON.parse(v); }
      catch { return fallback; }
    },
    set(key, value) { try { localStorage.setItem(key, JSON.stringify(value)); } catch { /* sin almacenamiento */ } },
  };

  /* ---------- Sonidos sintetizados (Web Audio) ---------- */
  let soundOn = store.get("brujita.sound", true);
  let ctx = null;
  function audio() {
    if (!soundOn) return null;
    if (!ctx) {
      const AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return null;
      ctx = new AC();
    }
    if (ctx.state === "suspended") ctx.resume();
    return ctx;
  }
  function tone({ freq, type = "sine", start = 0, dur = 0.3, vol = 0.2, slideTo }) {
    const ac = audio(); if (!ac) return;
    const t = ac.currentTime + start;
    const osc = ac.createOscillator(), gain = ac.createGain();
    osc.type = type;
    osc.frequency.setValueAtTime(freq, t);
    if (slideTo) osc.frequency.exponentialRampToValueAtTime(slideTo, t + dur);
    gain.gain.setValueAtTime(0.0001, t);
    gain.gain.exponentialRampToValueAtTime(vol, t + 0.015);
    gain.gain.exponentialRampToValueAtTime(0.0001, t + dur);
    osc.connect(gain).connect(ac.destination);
    osc.start(t); osc.stop(t + dur + 0.05);
  }
  const sfx = {
    shimmer() { // cascada de campanitas
      const notes = [1568, 1760, 2093, 2349, 2637, 3136, 2637, 3520];
      notes.forEach((f, i) => tone({ freq: f, start: i * 0.07, dur: 0.6, vol: 0.07, type: "triangle" }));
      tone({ freq: 220, slideTo: 880, dur: 1.4, vol: 0.05, type: "sine" });
    },
    ball(i) { // escala pentatónica ascendente
      const scale = [523, 587, 659, 784, 880];
      tone({ freq: scale[i % 5], dur: 0.35, vol: 0.18 });
      tone({ freq: scale[i % 5] * 2, dur: 0.25, vol: 0.05, type: "triangle" });
    },
    star(i) { // campana brillante
      const f = i ? 1319 : 1175;
      tone({ freq: f, dur: 0.9, vol: 0.14, type: "triangle" });
      tone({ freq: f * 2.76, dur: 0.5, vol: 0.04 });
    },
    fanfare() {
      [523, 659, 784, 1047].forEach((f, i) => tone({ freq: f, start: i * 0.09, dur: 0.9, vol: 0.09, type: "triangle" }));
    },
    giggle() { // "jijiji"
      [880, 990, 880, 1046].forEach((f, i) => tone({ freq: f, slideTo: f * 1.25, start: i * 0.12, dur: 0.1, vol: 0.12, type: "square" }));
    },
    meow() {
      const ac = audio(); if (!ac) return;
      const t = ac.currentTime;
      const osc = ac.createOscillator(), filter = ac.createBiquadFilter(), gain = ac.createGain();
      osc.type = "sawtooth";
      osc.frequency.setValueAtTime(520, t);
      osc.frequency.exponentialRampToValueAtTime(820, t + 0.18);
      osc.frequency.exponentialRampToValueAtTime(430, t + 0.6);
      filter.type = "bandpass"; filter.Q.value = 6;
      filter.frequency.setValueAtTime(900, t);
      filter.frequency.exponentialRampToValueAtTime(2200, t + 0.2);
      filter.frequency.exponentialRampToValueAtTime(800, t + 0.6);
      gain.gain.setValueAtTime(0.0001, t);
      gain.gain.exponentialRampToValueAtTime(0.35, t + 0.06);
      gain.gain.exponentialRampToValueAtTime(0.0001, t + 0.65);
      osc.connect(filter).connect(gain).connect(ac.destination);
      osc.start(t); osc.stop(t + 0.7);
    },
  };
  const buzz = (p) => { try { navigator.vibrate && navigator.vibrate(p); } catch { /* sin vibración */ } };

  function setSound(on) {
    soundOn = on;
    store.set("brujita.sound", on);
    soundBtn.setAttribute("aria-pressed", String(on));
    soundBtn.setAttribute("aria-label", on ? "Silenciar sonidos" : "Activar sonidos");
  }
  setSound(soundOn);
  soundBtn.addEventListener("click", () => { setSound(!soundOn); if (soundOn) sfx.ball(2); });

  /* ---------- Decorado: cielo estrellado ---------- */
  const sky = $("sky");
  for (let i = 0; i < 34; i++) {
    const s = document.createElement("i");
    s.style.left = Math.random() * 100 + "%";
    s.style.top = Math.random() * 60 + "%";
    s.style.animationDelay = (Math.random() * 3).toFixed(2) + "s";
    const size = (Math.random() * 2 + 1.5).toFixed(1) + "px";
    s.style.width = s.style.height = size;
    sky.appendChild(s);
  }

  function say(text) {
    bubble.textContent = text;
    bubble.classList.remove("again"); void bubble.offsetWidth; bubble.classList.add("again");
  }
  function burst(n = 18) {
    const r = orb.getBoundingClientRect(), base = scene.getBoundingClientRect();
    const cx = r.left - base.left + r.width / 2, cy = r.top - base.top + r.width / 2;
    for (let i = 0; i < n; i++) {
      const s = document.createElement("i");
      const ang = Math.random() * Math.PI * 2, dist = 50 + Math.random() * 110;
      s.style.left = cx - 5 + "px"; s.style.top = cy - 5 + "px";
      s.style.setProperty("--dx", Math.cos(ang) * dist + "px");
      s.style.setProperty("--dy", Math.sin(ang) * dist - 30 + "px");
      if (i % 3 === 0) s.style.background = "#9fb2ff";
      if (i % 4 === 0) s.style.background = "#fff";
      s.style.animationDelay = (Math.random() * 0.25).toFixed(2) + "s";
      sparks.appendChild(s);
      setTimeout(() => s.remove(), 1400);
    }
  }
  const wait = (ms) => new Promise((res) => setTimeout(res, ms));
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* ---------- Historial ---------- */
  let history = store.get("brujita.history", []);
  if (!Array.isArray(history)) history = [];
  function renderHistory() {
    histList.innerHTML = "";
    history.forEach((h) => {
      const li = document.createElement("li");
      const combo = document.createElement("span");
      combo.innerHTML = `<span class="n">${h.n.map(pad).join(" · ")}</span> &nbsp;★ <span class="s">${h.s.map(pad).join(" · ")}</span>`;
      const when = document.createElement("time");
      const d = new Date(h.t);
      when.dateTime = d.toISOString();
      when.textContent = d.toLocaleString("es-ES", { day: "2-digit", month: "short", hour: "2-digit", minute: "2-digit" });
      li.append(combo, when);
      histList.appendChild(li);
    });
    histEmpty.hidden = history.length > 0;
    clearBtn.hidden = history.length === 0;
  }
  clearBtn.addEventListener("click", () => {
    history = []; store.set("brujita.history", history); renderHistory();
    say("¡Puf! Historial desvanecido.");
  });
  renderHistory();

  /* ---------- El hechizo ---------- */
  let current = { n: [7, 14, 23, 34, 46], s: [3, 11] };
  let casting = false;

  async function cast() {
    if (casting) return;
    casting = true;
    castBtn.disabled = true;
    const result = { n: pick(NUMBERS, NUMBER_MAX), s: pick(STARS, STAR_MAX), t: Date.now() };

    say(PHRASES[randInt(PHRASES.length) - 1]);
    hint.textContent = "La bola de cristal está pensando…";
    scene.classList.add("casting");
    const ballEls = [...balls.children];
    ballEls.forEach((b) => { b.classList.remove("reveal"); b.classList.add("hidden"); });
    sfx.shimmer(); buzz(30);
    burst(14);
    await wait(reduced ? 100 : 1100);

    const values = [...result.n, ...result.s];
    for (let i = 0; i < ballEls.length; i++) {
      const el = ballEls[i];
      el.textContent = pad(values[i]);
      el.classList.remove("hidden");
      void el.offsetWidth;
      el.classList.add("reveal");
      if (i < NUMBERS) sfx.ball(i); else sfx.star(i - NUMBERS);
      buzz(12);
      if (i >= NUMBERS) burst(8);
      await wait(reduced ? 40 : 320);
    }

    scene.classList.remove("casting");
    sfx.fanfare();
    current = result;
    history.unshift(result);
    history = history.slice(0, HISTORY_MAX);
    store.set("brujita.history", history);
    renderHistory();
    hint.textContent = `Tus números: ${result.n.map(pad).join(", ")} · Estrellas: ${result.s.map(pad).join(" y ")}`;
    say("¡Aquí tienes tu combinación!");
    await wait(reduced ? 100 : 700);
    catMeow(); // el gato da el visto bueno
    castBtn.textContent = "Otro hechizo";
    castBtn.disabled = false;
    casting = false;
  }
  castBtn.addEventListener("click", cast);
  orb.addEventListener("click", cast);

  witch.addEventListener("click", () => {
    if (casting) return;
    say(PHRASES[randInt(PHRASES.length) - 1]);
    sfx.giggle();
    witch.classList.remove("giggle"); void witch.offsetWidth; witch.classList.add("giggle");
  });
  function catMeow() {
    sfx.meow(); buzz(20);
    cat.classList.remove("meow"); void cat.offsetWidth; cat.classList.add("meow");
  }
  cat.addEventListener("click", () => {
    catMeow();
    say(CAT_PHRASES[randInt(CAT_PHRASES.length) - 1]);
  });

  /* ---------- Copiar / compartir ---------- */
  function toast(msg) {
    const t = document.createElement("div");
    t.className = "toast"; t.setAttribute("role", "status"); t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 1800);
  }
  shareBtn.addEventListener("click", async () => {
    const text = `Mis números de la suerte para el Euromillones 🔮\nNúmeros: ${current.n.map(pad).join(" - ")}\nEstrellas: ${current.s.map(pad).join(" - ")}`;
    if (navigator.share) {
      try { await navigator.share({ title: "Brujita de la Suerte", text }); return; }
      catch (e) { if (e && e.name === "AbortError") return; }
    }
    try { await navigator.clipboard.writeText(text); toast("Combinación copiada"); }
    catch { toast(`${current.n.map(pad).join(" ")} ★ ${current.s.map(pad).join(" ")}`); }
  });

  /* ---------- App instalable / sin conexión ---------- */
  if ("serviceWorker" in navigator && location.protocol === "https:") {
    navigator.serviceWorker.register("sw.js").catch(() => { /* no disponible aquí */ });
  }
})();
