// =====================================================================================
//  install-button.test.js
//  Prova del plugin Millennium in Node.js, con una "libreria di Steam" FINTA: voci non Steam
//  (segnaposto GOG, Epic, Ubisoft e Heroic, un gioco normale, un gioco Ubisoft installato) e un
//  gioco Steam non installato.
//  Si lancia con:  node --test tests/plugin
//  (La lancia anche "dotnet test", se Node.js è installato: vedi PluginTests.cs.)
// =====================================================================================

const test = require("node:test");
const assert = require("node:assert");
const { steamAllInstallButton } = require("../../src/SteamAll.MillenniumPlugin/.millennium/Dist/index.js");

function fakeSteam() {
  const overview = (appid, installed) => ({
    appid,
    gameid: String(appid),
    per_client_data: [{ clientid: "0", installed, display_status: installed ? 11 : 9 }],
  });
  const apps = new Map([
    [100, overview(100, false)],                 // un gioco Steam non installato
    [3000000001, overview(3000000001, true)],    // segnaposto GOG
    [3000000002, overview(3000000002, true)],    // segnaposto Epic
    [3000000003, overview(3000000003, true)],    // un gioco non Steam normale
    [3000000004, overview(3000000004, true)],    // segnaposto Ubisoft
    [3000000005, overview(3000000005, true)],    // gioco Ubisoft INSTALLATO (lo avvia Ubisoft Connect)
    [3000000006, overview(3000000006, true)],    // segnaposto Heroic su Linux (xdg-open)
  ]);
  const details = {
    3000000001: { strShortcutExe: '"C:\\WINDOWS\\System32\\rundll32.exe"', strLaunchOptions: "url.dll,FileProtocolHandler goggalaxy://openGameView/gog_1" },
    3000000002: { strShortcutExe: '"C:\\WINDOWS\\System32\\rundll32.exe"', strLaunchOptions: "url.dll,FileProtocolHandler com.epicgames.launcher://apps/a%3Ab%3Ac?action=launch" },
    3000000003: { strShortcutExe: '"C:\\Games\\Kirby\\Kirby.exe"', strLaunchOptions: "" },
    3000000004: { strShortcutExe: '"C:\\WINDOWS\\System32\\rundll32.exe"', strLaunchOptions: "url.dll,FileProtocolHandler uplay://install/42" },
    3000000005: { strShortcutExe: '"C:\\Program Files (x86)\\Ubisoft\\Ubisoft Game Launcher\\upc.exe"', strLaunchOptions: "uplay://launch/42/0" },
    3000000006: { strShortcutExe: '"/usr/bin/xdg-open"', strLaunchOptions: "heroic://launch/gog/1207" },
  };
  const calls = { runGame: [], wizard: [], changes: 0 };
  const win = {
    appStore: { m_mapApps: apps, GetAppOverviewByAppID: id => apps.get(id) },
    appDetailsStore: {
      RegisterForAppData(appid, callback) {
        setTimeout(() => callback(details[appid]), 1);
        return { unregister() {} };
      },
    },
    collectionStore: { OnAppOverviewChange() { calls.changes++; } },
    SteamClient: {
      Apps: { RunGame: (gameId) => calls.runGame.push(gameId) },
      Installs: { OpenInstallWizard: (ids) => calls.wizard.push(ids) },
    },
  };
  return { win, apps, calls };
}

test("store placeholders become 'not installed', other games don't", async () => {
  const { win, apps } = fakeSteam();

  const state = steamAllInstallButton(win);
  await state.started;

  assert.equal(state.placeholders.size, 4);   // GOG, Epic, Ubisoft, Heroic
  assert.equal(apps.get(3000000001).per_client_data[0].installed, false);
  assert.equal(apps.get(3000000002).per_client_data[0].display_status, 9);
  assert.equal(apps.get(3000000004).per_client_data[0].installed, false);
  assert.equal(apps.get(3000000006).per_client_data[0].installed, false);
  assert.equal(apps.get(3000000003).per_client_data[0].installed, true);   // Kirby resta installato
  assert.equal(apps.get(3000000005).per_client_data[0].installed, true);   // e anche il gioco Ubisoft installato
});

test("Install opens the store for placeholders and keeps Steam's wizard for the rest", async () => {
  const { win, calls } = fakeSteam();
  const state = steamAllInstallButton(win);
  await state.started;

  win.SteamClient.Installs.OpenInstallWizard([3000000001, 100]);

  assert.deepEqual(calls.runGame, ["3000000001"]);   // il segnaposto: si avvia (e apre lo store)
  assert.deepEqual(calls.wizard, [[100]]);           // il gioco Steam: la procedura normale
});

test("with the old theme section active, takes only the placeholders the theme doesn't know", async () => {
  const { win, calls } = fakeSteam();
  // La sezione del tema ha già preso i segnaposto GOG ed Epic (conosce solo quei due link).
  win.__setupBoilRStore = { source: "theme", ready: true, placeholders: new Map([[3000000001, "gog"], [3000000002, "epic"]]) };

  const state = steamAllInstallButton(win);
  await state.started;

  assert.deepEqual([...state.placeholders.keys()].sort(), [3000000004, 3000000006]);   // Ubisoft e Heroic
  win.SteamClient.Installs.OpenInstallWizard([3000000004]);
  assert.deepEqual(calls.runGame, ["3000000004"]);
  assert.equal(steamAllInstallButton(win), state);   // e gira una volta sola
});
