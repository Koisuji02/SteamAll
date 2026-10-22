# TODO

What comes next for SteamAll. Pull requests are welcome for any of these: read [CONTRIBUTING.md](CONTRIBUTING.md) first.

## Stores (help wanted)

Today SteamAll reads GOG GALAXY, Epic Games and Ubisoft Connect: the stores the maintainer uses and could test for real. The next step is adding the stores that are left. It's best done by someone who uses that store and can test it on their own library.

The notes below are starting points from early research, not tested code.

- [ ] **EA app**: installed games are easy to find. The list of owned games is kept in an encrypted file whose key depends on the computer; an early attempt to read it didn't work.
- [ ] **Amazon Games**: the app keeps owned and installed games in SQLite files under `%LOCALAPPDATA%\Amazon Games\Data\Games\Sql`.
- [ ] **itch.io**: the itch app keeps its library in `butler.db` (SQLite).
- [ ] **Battle.net**: installed games only; the owned list doesn't seem to be stored on the computer.
- [ ] **Xbox app / Microsoft Store** (PC Game Pass): installed games.
- [ ] **Linux**: Heroic (Epic, GOG and Amazon), Lutris.

## Platforms

- [ ] **Linux**: SteamAll builds for Linux, but hasn't been run on a real Linux PC yet (Steam folders, Millennium, the Trash).
- [ ] **macOS**: same, and no stores yet.
- [ ] **Steam Deck**: Desktop Mode, then games in Game Mode.

## More

- [ ] Automatic controller setup for more emulators (today: Ryujinx, Cemu, Dolphin and RPCS3).
- [ ] Translations of the interface (today it's in English only).
