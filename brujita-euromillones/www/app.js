(() => {
  "use strict";

  /* ---------- Sorteos ---------- */
  // Cada grupo: cuántas bolas, rango, estilo y si se ordenan sin repetir (las cifras de un décimo sí se repiten).
  const GAMES = [
    {
      id: "euromillones", name: "Euromillones", color: "#ffcd4a",
      rule: "5 números del 1 al 50 + 2 estrellas del 1 al 12",
      days: "Martes y viernes",
      groups: [
        { count: 5, min: 1, max: 50, style: "gold", label: "Números" },
        { count: 2, min: 1, max: 12, style: "star", label: "Estrellas" },
      ],
    },
    {
      id: "primitiva", name: "La Primitiva", color: "#3ccf5c",
      rule: "6 números del 1 al 49",
      days: "Lunes, jueves y sábados",
      groups: [{ count: 6, min: 1, max: 49, style: "green", label: "Números" }],
    },
    {
      id: "bonoloto", name: "Bonoloto", color: "#ff9a3c",
      rule: "6 números del 1 al 49",
      days: "De lunes a sábado",
      groups: [{ count: 6, min: 1, max: 49, style: "orange", label: "Números" }],
    },
    {
      id: "gordo", name: "El Gordo de la Primitiva", color: "#ff5a5f",
      rule: "5 números del 1 al 54 + número clave del 0 al 9",
      days: "Domingos",
      groups: [
        { count: 5, min: 1, max: 54, style: "red", label: "Números" },
        { count: 1, min: 0, max: 9, style: "key", label: "Clave" },
      ],
    },
    {
      id: "eurodreams", name: "EuroDreams", color: "#b678ff",
      rule: "6 números del 1 al 40 + 1 sueño del 1 al 5",
      days: "Lunes y jueves",
      groups: [
        { count: 6, min: 1, max: 40, style: "violet", label: "Números" },
        { count: 1, min: 1, max: 5, style: "dream", label: "Sueño" },
      ],
    },
    {
      id: "nacional", name: "Lotería Nacional", color: "#f1e2c0",
      rule: "Un número de 5 cifras para buscar tu décimo",
      days: "Jueves y sábados",
      groups: [{ count: 5, min: 0, max: 9, style: "digit", label: "Número", digits: true }],
    },
  ];
  const HISTORY_MAX = 10;

  const $ = (id) => document.getElementById(id);
  const scene = $("scene"), bubble = $("bubble"), balls = $("balls"), hint = $("hint");
  const castBtn = $("castBtn"), shareBtn = $("shareBtn"), soundBtn = $("soundBtn");
  const witch = $("witch"), cat = $("cat"), orb = $("orb"), sparks = $("sparks");
  const histList = $("histList"), histEmpty = $("histEmpty"), clearBtn = $("clearBtn");
  const menu = $("menu"), gamesList = $("games"), gameView = $("game"), backBtn = $("backBtn");

  const PHRASES = [
    "¡Abracadabra, números a la vista!",
    "Mi bola de cristal nunca duerme…",
    "Misifú dice que hoy es tu noche.",
    "¡Que la magia te acompañe!",
    "Estrellas, estrellitas… ¡salid!",
    "Un poquito de polvo de luna y listo.",
    "Shhh… las estrellas están hablando.",
  ];
  const MENU_PHRASES = ["¿A qué sorteo jugamos hoy?", "Elige un sorteo y yo hago la magia.", "¡Misifú y yo estamos listas!"];
  const CAT_PHRASES = ["¡Miau!", "Miaaau~", "Prrrr…", "¡Miau miau!"];

  /* ---------- Azar uniforme con crypto ---------- */
  function randInt(max) { // 1..max sin sesgo
    const buf = new Uint32Array(1);
    const limit = Math.floor(0x100000000 / max) * max;
    let x;
    do { crypto.getRandomValues(buf); x = buf[0]; } while (x >= limit);
    return (x % max) + 1;
  }
  const randRange = (min, max) => min - 1 + randInt(max - min + 1);
  const anyOf = (list) => list[randInt(list.length) - 1];
  function drawGroup(g) {
    if (g.digits) return Array.from({ length: g.count }, () => randRange(g.min, g.max));
    const set = new Set();
    while (set.size < g.count) set.add(randRange(g.min, g.max));
    return [...set].sort((a, b) => a - b);
  }
  const fmt = (g, n) => (g.digits || g.max < 10 ? String(n) : String(n).padStart(2, "0"));
  const describe = (game, values, sep = " · ") =>
    game.groups.map((g, i) => `${g.label}: ${values[i].map((n) => fmt(g, n)).join(g.digits ? "" : sep)}`).join("  |  ");

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
      const scale = [523, 587, 659, 784, 880, 1047];
      const f = scale[i % scale.length];
      tone({ freq: f, dur: 0.35, vol: 0.18 });
      tone({ freq: f * 2, dur: 0.25, vol: 0.05, type: "triangle" });
    },
    star(i) { // campana brillante
      const f = i ? 1319 : 1175;
      tone({ freq: f, dur: 0.9, vol: 0.14, type: "triangle" });
      tone({ freq: f * 2.76, dur: 0.5, vol: 0.04 });
    },
    fanfare() {
      [523, 659, 784, 1047].forEach((f, i) => tone({ freq: f, start: i * 0.09, dur: 0.9, vol: 0.09, type: "triangle" }));
    },
    tap() { tone({ freq: 660, slideTo: 990, dur: 0.12, vol: 0.1, type: "triangle" }); },
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

  /* ---------- Bolas ---------- */
  function ballEl(g, n) {
    const el = document.createElement("span");
    el.className = "ball " + g.style;
    el.textContent = fmt(g, n);
    return el;
  }
  // Pinta la combinación; devuelve las bolas en orden de aparición.
  function renderBalls(game, values) {
    balls.innerHTML = "";
    const els = [];
    const total = game.groups.reduce((a, g) => a + g.count, 0);
    balls.style.setProperty("--n", total + (game.groups.length - 1) * 0.4);
    balls.setAttribute("aria-label", describe(game, values, ", "));
    game.groups.forEach((g, gi) => {
      if (gi) { const sep = document.createElement("span"); sep.className = "sep"; balls.appendChild(sep); }
      values[gi].forEach((n) => { const el = ballEl(g, n); el.dataset.group = gi; els.push(el); balls.appendChild(el); });
    });
    return els;
  }

  /* ---------- Menú ---------- */
  GAMES.forEach((game) => {
    const li = document.createElement("li");
    const btn = document.createElement("button");
    btn.type = "button"; btn.className = "game-card";
    btn.style.setProperty("--c", game.color);
    const mini = document.createElement("span");
    mini.className = "mini"; mini.setAttribute("aria-hidden", "true");
    game.groups.forEach((g) => { for (let i = 0; i < Math.min(g.count, 3); i++) mini.appendChild(ballEl(g, g.digits ? 7 : [7, 13, 21][i])); });
    const name = document.createElement("strong"); name.textContent = game.name;
    const rule = document.createElement("span"); rule.className = "rule"; rule.textContent = game.rule;
    const days = document.createElement("span"); days.className = "days"; days.textContent = "Sorteos: " + game.days;
    btn.append(mini, name, rule, days);
    btn.addEventListener("click", () => { sfx.tap(); openGame(game.id, true); });
    li.appendChild(btn);
    gamesList.appendChild(li);
  });

  /* ---------- Historial por sorteo ---------- */
  let game = null, history = [], current = null, casting = false;
  const histKey = (id) => "brujita.history." + id;
  (function migrate() { // historial de la primera versión (solo Euromillones)
    const old = store.get("brujita.history", null);
    if (Array.isArray(old) && old.length && !store.get(histKey("euromillones"), null)) {
      store.set(histKey("euromillones"), old.map((h) => ({ v: [h.n, h.s], t: h.t })));
    }
    try { localStorage.removeItem("brujita.history"); } catch { /* sin almacenamiento */ }
  })();

  function renderHistory() {
    histList.innerHTML = "";
    history.forEach((h) => {
      const li = document.createElement("li");
      const combo = document.createElement("span");
      combo.className = "combo";
      game.groups.forEach((g, gi) => {
        if (gi) combo.append(" ★ ");
        const part = document.createElement("span");
        part.className = gi ? "s" : "n";
        part.textContent = h.v[gi].map((n) => fmt(g, n)).join(g.digits ? "" : " · ");
        combo.appendChild(part);
      });
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
    history = []; store.set(histKey(game.id), history); renderHistory();
    say("¡Puf! Historial desvanecido.");
  });

  /* ---------- Navegación menú ⇄ sorteo ---------- */
  function showMenu() {
    game = null;
    gameView.hidden = true;
    menu.hidden = false;
    $("sub").textContent = "Números al azar para tus sorteos";
    say(anyOf(MENU_PHRASES));
  }
  function openGame(id, push) {
    const next = GAMES.find((g) => g.id === id);
    if (!next) return showMenu();
    if (push) { try { window.history.pushState({ game: id }, "", "#" + id); } catch { /* sin historial */ } }
    game = next;
    menu.hidden = true;
    gameView.hidden = false;
    $("gameName").textContent = game.name;
    $("gameRule").textContent = game.rule;
    $("sub").textContent = game.name + " · " + game.days.toLowerCase();
    const saved = store.get(histKey(game.id), []);
    history = Array.isArray(saved) ? saved.filter((h) => h && Array.isArray(h.v)) : [];
    current = history[0] || null;
    if (current) {
      renderBalls(game, current.v);
      hint.textContent = "Tu última combinación. Lanza el hechizo para otra.";
      castBtn.textContent = "Otro hechizo";
    } else {
      renderBalls(game, game.groups.map(drawGroup)).forEach((el) => el.classList.add("hidden"));
      hint.textContent = "Toca la bola de cristal o el botón para lanzar el hechizo.";
      castBtn.textContent = "Lanzar hechizo";
    }
    shareBtn.disabled = !current;
    renderHistory();
    say(`¡Vamos con ${game.name}!`);
    window.scrollTo({ top: 0, behavior: reduced ? "auto" : "smooth" });
  }
  backBtn.addEventListener("click", () => {
    sfx.tap();
    if (window.history.state && window.history.state.game) window.history.back();
    else { try { window.history.replaceState(null, "", location.pathname + location.search); } catch { /* sin historial */ } showMenu(); }
  });
  window.addEventListener("popstate", () => {
    const id = location.hash.slice(1);
    if (id) openGame(id, false); else showMenu();
  });

  /* ---------- El hechizo ---------- */
  async function cast() {
    if (casting) return;
    if (!game) { say("Primero elige un sorteo aquí abajo."); sfx.giggle(); return; }
    casting = true;
    castBtn.disabled = true;
    const playing = game;
    const result = { v: playing.groups.map(drawGroup), t: Date.now() };

    say(anyOf(PHRASES));
    hint.textContent = "La bola de cristal está pensando…";
    scene.classList.add("casting");
    const els = renderBalls(playing, result.v);
    els.forEach((b) => b.classList.add("hidden"));
    sfx.shimmer(); buzz(30);
    burst(14);
    await wait(reduced ? 100 : 1100);

    let mainIdx = 0, extraIdx = 0;
    for (const el of els) {
      el.classList.remove("hidden");
      void el.offsetWidth;
      el.classList.add("reveal");
      if (el.dataset.group === "0") sfx.ball(mainIdx++);
      else { sfx.star(extraIdx++); burst(8); }
      buzz(12);
      await wait(reduced ? 40 : 320);
    }

    scene.classList.remove("casting");
    sfx.fanfare();
    const list = store.get(histKey(playing.id), []);
    const updated = [result, ...(Array.isArray(list) ? list : [])].slice(0, HISTORY_MAX);
    store.set(histKey(playing.id), updated);
    if (game === playing) {
      current = result;
      history = updated;
      renderHistory();
      hint.textContent = describe(playing, result.v, ", ");
      castBtn.textContent = "Otro hechizo";
      shareBtn.disabled = false;
    }
    say(playing.id === "nacional" ? "¡Busca un décimo con este número!" : "¡Aquí tienes tu combinación!");
    await wait(reduced ? 100 : 700);
    catMeow(); // el gato da el visto bueno
    castBtn.disabled = false;
    casting = false;
  }
  castBtn.addEventListener("click", cast);
  orb.addEventListener("click", cast);

  witch.addEventListener("click", () => {
    if (casting) return;
    say(game ? anyOf(PHRASES) : anyOf(MENU_PHRASES));
    sfx.giggle();
    witch.classList.remove("giggle"); void witch.offsetWidth; witch.classList.add("giggle");
  });
  function catMeow() {
    sfx.meow(); buzz(20);
    cat.classList.remove("meow"); void cat.offsetWidth; cat.classList.add("meow");
  }
  cat.addEventListener("click", () => {
    catMeow();
    say(anyOf(CAT_PHRASES));
  });

  /* ---------- Copiar / compartir ---------- */
  function toast(msg) {
    const t = document.createElement("div");
    t.className = "toast"; t.setAttribute("role", "status"); t.textContent = msg;
    document.body.appendChild(t);
    setTimeout(() => t.remove(), 1800);
  }
  shareBtn.addEventListener("click", async () => {
    if (!current || !game) return;
    const text = `Mis números de la suerte para ${game.name} 🔮\n` +
      game.groups.map((g, i) => `${g.label}: ${current.v[i].map((n) => fmt(g, n)).join(g.digits ? "" : " - ")}`).join("\n");
    if (navigator.share) {
      try { await navigator.share({ title: "Brujita de la Suerte", text }); return; }
      catch (e) { if (e && e.name === "AbortError") return; }
    }
    try { await navigator.clipboard.writeText(text); toast("Combinación copiada"); }
    catch { toast(describe(game, current.v, " ")); }
  });

  /* ---------- Arranque ---------- */
  const startId = location.hash.slice(1);
  if (GAMES.some((g) => g.id === startId)) {
    try { window.history.replaceState({ game: startId }, "", "#" + startId); } catch { /* sin historial */ }
    openGame(startId, false);
  } else {
    showMenu();
  }

  /* ---------- App instalable / sin conexión (en la app nativa ya va todo dentro) ---------- */
  const isNativeApp = !!(window.Capacitor && window.Capacitor.isNativePlatform && window.Capacitor.isNativePlatform());
  if (!isNativeApp && "serviceWorker" in navigator && location.protocol === "https:") {
    navigator.serviceWorker.register("sw.js").catch(() => { /* no disponible aquí */ });
  }
})();
