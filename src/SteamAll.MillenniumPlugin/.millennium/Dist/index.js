// =====================================================================================
//  index.js — SteamAll Install Button (plugin Millennium, parte nell'interfaccia di Steam)
//
//  SteamAll aggiunge i giochi che possiedi negli store (GOG GALAXY, Epic, Ubisoft Connect, Amazon,
//  itch.io, Heroic...) ma non hai installato come voci non Steam che aprono lo store sulla pagina
//  del gioco: il "programma per aprire i link" del sistema (rundll32.exe "url.dll,FileProtocolHandler
//  <link>" su Windows, xdg-open su Linux) con il link dello store. Steam le mostrerebbe installate,
//  con "Gioca". Questo plugin le fa diventare "non installate": grigie, fuori dalle raccolte degli
//  installati, con il tasto "Installa", che apre lo store per scaricarle.
//  (I giochi installati SteamAll li avvia con il programma dello store, non con quello dei link:
//  così non vengono scambiati per giochi da installare.)
//
//  Come: lavora sui dati della libreria nel contesto JavaScript principale di Steam (dove
//  Millennium carica i plugin). Prima che Steam ricalcoli le raccolte (collectionStore
//  .OnAppOverviewChange) segna queste voci come non installate; e quando premi Installa
//  (SteamClient.Installs.OpenInstallWizard) per queste voci avvia la scorciatoia, che apre lo store.
//  Se un giorno Steam cambia e questo non funziona più, le voci continuano ad aprire lo store con "Gioca".
//
//  È JavaScript "puro": niente librerie e niente passaggi di compilazione. In fondo al file c'è
//  l'aggancio a Millennium (le funzioni che Millennium si aspetta da un plugin).
// =====================================================================================

const MILLENNIUM_IS_CLIENT_MODULE = true;   // per Millennium: questo plugin gira nel "client" di Steam
const pluginName = "steamall-install-button";

/**
 * Il lavoro vero. Gira una volta per sessione di Steam.
 * `win` = la finestra JavaScript con i dati della libreria (di solito `window`); le prove ne passano una finta.
 */
function steamAllInstallButton(win) {
  "use strict";
  // Gira una volta sola per sessione di Steam.
  if (win.__steamAllInstallButton) return win.__steamAllInstallButton;
  const state = { placeholders: new Map(), readyToInstall: 9, ready: false, source: "steamall-plugin" };
  win.__steamAllInstallButton = state;
  // La vecchia sezione del tema di Setup_BoilR fa la stessa cosa solo per GOG ed Epic e usa il
  // "segnale" __setupBoilRStore. Se è già partita, lasciamo a lei le voci che ha preso (così il tasto
  // non apre lo store due volte) e prendiamo le altre (Ubisoft...). Se non è ancora partita, il
  // segnale lo mettiamo noi, e lei non parte più.
  const theme = win.__setupBoilRStore || null;
  if (!theme) win.__setupBoilRStore = state;

  // I link degli store, e i programmi che aprono i link (Windows: rundll32 o explorer; Linux: xdg-open; macOS: open).
  const STORE_LINK = /(^|[\s,"])(goggalaxy|com\.epicgames\.launcher|uplay|origin2|link2ea|amazon-games|itch|heroic):\/\//i;
  const LINK_OPENER = /(^|[\\/])((rundll32|explorer)\.exe|xdg-open|open)"?\s*$/i;
  const FIRST_SHORTCUT_APPID = 0x80000000;   // i giochi non Steam hanno app id da qui in su
  const LAUNCH_SOURCE_LIBRARY = 100;
  const READY_TO_INSTALL = 9;                // EDisplayStatus "ReadyToInstall": il tasto Installa

  const log = (...args) => {
    try { console.log("%c SteamAll ", "background:#2e9e5b;color:#fff;border-radius:3px", ...args); } catch (e) { /* niente console */ }
  };
  const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

  // I dati "locali" di una voce (quelli di questo PC, clientid "0").
  function localData(overview) {
    const all = overview && Array.isArray(overview.per_client_data) ? overview.per_client_data : [];
    return all.filter(data => data && String(data.clientid) === "0");
  }

  function markNotInstalled(overview) {
    for (const data of localData(overview)) {
      if (data.installed !== false) data.installed = false;
      if (data.display_status !== state.readyToInstall) data.display_status = state.readyToInstall;
    }
  }

  // Lo stato che Steam dà ai suoi giochi posseduti e non installati (quello con "Installa");
  // se non ne trova, il valore documentato.
  function readyToInstallStatus() {
    const counts = new Map();
    for (const overview of win.appStore.m_mapApps.values()) {
      if (!overview || overview.appid >= FIRST_SHORTCUT_APPID) continue;
      for (const data of localData(overview)) {
        if (data.installed === false && data.display_status) counts.set(data.display_status, (counts.get(data.display_status) || 0) + 1);
      }
    }
    let best = READY_TO_INSTALL, bestCount = 0;
    for (const [status, count] of counts) if (count > bestCount) { best = status; bestCount = count; }
    return best;
  }

  // I dettagli di una scorciatoia (exe, opzioni di avvio), o null dopo 15 secondi.
  function loadDetails(appid) {
    return new Promise(resolve => {
      let done = false, handle = null, timer = null;
      const finish = details => {
        if (done) return;
        done = true;
        if (timer) clearTimeout(timer);   // arrivati i dati, il timer di riserva non serve più
        resolve(details);
        if (handle) try { handle.unregister(); } catch (e) { /* già tolto */ }
      };
      try {
        handle = win.appDetailsStore.RegisterForAppData(appid, details => {
          if (details && typeof details.strShortcutExe === "string") finish(details);
        });
        if (done && handle) try { handle.unregister(); } catch (e) { /* già tolto */ }
      } catch (e) {
        finish(null);
        return;
      }
      if (!done) timer = setTimeout(() => finish(null), 15000);
    });
  }

  function isPlaceholder(details) {
    return !!details && LINK_OPENER.test(details.strShortcutExe || "") && STORE_LINK.test(details.strLaunchOptions || "");
  }

  // Ogni aggiornamento della libreria di queste voci passa da qui prima che le raccolte si ricalcolino.
  function patchCollections() {
    const store = win.collectionStore;
    const original = store.OnAppOverviewChange;
    if (typeof original !== "function" || original.__steamAll) return;
    const patched = function (changed) {
      try {
        for (const overview of changed || []) if (overview && state.placeholders.has(overview.appid)) markNotInstalled(overview);
      } catch (e) { /* non rompere mai l'aggiornamento di Steam */ }
      return original.apply(this, arguments);
    };
    patched.__steamAll = true;
    store.OnAppOverviewChange = patched;
  }

  function openStore(appid) {
    const overview = win.appStore.GetAppOverviewByAppID(appid);
    if (!overview) return;
    const gameId = typeof overview.GetGameID === "function" ? overview.GetGameID() : overview.gameid;
    win.SteamClient.Apps.RunGame(String(gameId), "", -1, LAUNCH_SOURCE_LIBRARY);
  }

  // Installa (pagina del gioco, menu, più giochi selezionati) chiede la procedura di installazione di Steam.
  function patchInstall() {
    const installs = win.SteamClient && win.SteamClient.Installs;
    const original = installs && installs.OpenInstallWizard;
    if (typeof original !== "function" || original.__steamAll) return;
    const patched = function (appids, ...rest) {
      const list = (Array.isArray(appids) ? appids : [appids]).map(Number);
      const ours = list.filter(id => state.placeholders.has(id));
      if (!ours.length) return original.call(this, appids, ...rest);
      for (const id of ours) {
        try { openStore(id); } catch (e) { log("cannot open the store", e); }
      }
      const others = list.filter(id => !state.placeholders.has(id));
      if (others.length) return original.call(this, others, ...rest);
      return undefined;
    };
    patched.__steamAll = true;
    try { installs.OpenInstallWizard = patched; } catch (e) { /* sola lettura: proviamo sotto */ }
    if (installs.OpenInstallWizard !== patched) {
      try {
        Object.defineProperty(installs, "OpenInstallWizard", { value: patched, writable: true, configurable: true });
      } catch (e) { log("cannot take over Install: store games keep Play", e); }
    }
  }

  async function start() {
    // Aspettiamo che Steam abbia caricato la libreria (fino a 2 minuti).
    for (let i = 0; i < 240; i++) {
      const app = win.appStore;
      if (app && app.m_mapApps && app.m_mapApps.size && win.collectionStore && win.appDetailsStore && win.SteamClient) break;
      await wait(500);
    }
    if (!(win.appStore && win.appStore.m_mapApps && win.collectionStore && win.appDetailsStore)) {
      log("library data not found: store games keep Play");
      return;
    }
    state.readyToInstall = readyToInstallStatus();
    // Con la vecchia sezione del tema attiva: aspettiamo che finisca (al massimo 30 secondi) per
    // sapere quali voci ha già preso lei.
    for (let i = 0; theme && !theme.ready && i < 60; i++) await wait(500);
    const theirs = theme && theme.placeholders instanceof Map ? theme.placeholders : new Map();
    const shortcuts = [...win.appStore.m_mapApps.values()].filter(o => o && o.appid >= FIRST_SHORTCUT_APPID && !theirs.has(o.appid));
    const found = [];
    await Promise.all(shortcuts.map(async overview => {
      const details = await loadDetails(overview.appid);
      if (isPlaceholder(details)) {
        state.placeholders.set(overview.appid, details.strLaunchOptions.trim());
        found.push(overview.appid);
      }
    }));
    if (!found.length) return;
    patchCollections();
    patchInstall();
    const overviews = found.map(id => win.appStore.GetAppOverviewByAppID(id)).filter(Boolean);
    win.collectionStore.OnAppOverviewChange(overviews, []);
    state.ready = true;
    log(`${overviews.length} store games shown as not installed`);
  }

  state.started = start().catch(e => log("error", e));
  return state;
}

// --- L'aggancio a Millennium ---------------------------------------------------------
// Millennium carica questo file e si aspetta: PluginEntryPointMain(), che restituisce un oggetto
// con una funzione "default" da chiamare all'avvio, e la registrazione in window.PLUGIN_LIST.
// Nelle prove (Node.js) "window" non esiste: lì si usa direttamente steamAllInstallButton.

function PluginEntryPointMain() {
  return {
    default: async function () {
      steamAllInstallButton(window);
    },
  };
}

if (typeof window !== "undefined") {
  window.PLUGIN_LIST = window.PLUGIN_LIST || {};
  window.PLUGIN_LIST[pluginName] = window.PLUGIN_LIST[pluginName] || {};
  window.MILLENNIUM_PLUGIN_SETTINGS_STORE = window.MILLENNIUM_PLUGIN_SETTINGS_STORE || {};
  window.MILLENNIUM_PLUGIN_SETTINGS_STORE[pluginName] = window.MILLENNIUM_PLUGIN_SETTINGS_STORE[pluginName] || {};
  const entry = PluginEntryPointMain();
  Object.assign(window.PLUGIN_LIST[pluginName], { ...entry, __millennium_internal_plugin_name_do_not_use_or_change__: pluginName });
  entry.default();
}

if (typeof module !== "undefined") module.exports = { steamAllInstallButton };   // per le prove
