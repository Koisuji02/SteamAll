# Contributing to SteamAll

SteamAll lives in this repository and is maintained by Mattia Domizio. Help is welcome, above all new stores (see [TODO.md](TODO.md)), but also fixes, emulators and testing on Linux and macOS.

The idea is one SteamAll that keeps getting better for everyone. So please bring your work back here with a pull request instead of keeping a separate fork, and give modified versions you distribute a different name and logo.

## Before you start

- For anything bigger than a small fix, open an issue first and say what you want to do. It avoids work that can't be merged.
- Look at how the existing code does it, and follow it. Store readers are in `src/SteamAll.Core/Stores`, their tests in `tests/SteamAll.Tests/StoreSourceTests.cs`.

## What every pull request needs

1. **It builds with no warnings**: `dotnet build`.
2. **All tests pass**: `dotnet test`. The same tests run on GitHub for every pull request, and a red check can't be merged.
3. **Tests for the new code.** They work on fake copies of the files (a fake Steam, a fake store folder), never on the real files of your PC. Copy a few real files, remove anything personal, and use them as test data.
4. **Proof that it works, in the pull request description.** Say what you tried, on which system, and attach screenshots or logs. For a new store, for example:
   - how many games SteamAll finds, installed and not installed, compared with the store app;
   - that an installed game starts from Steam through the store app;
   - that Install on a game you don't have opens the store on that game's page;
   - that a second Apply after Refresh changes nothing.
5. **No regressions.** What already works keeps working the same way, unless we agreed otherwise in the issue. Say clearly what you could not test.

Pull requests are reviewed and merged by the maintainer: `main` only changes through approved pull requests.

## Rules the project keeps

- **Store libraries are read from the files the store app keeps on the computer.** No logins, no passwords, no account tokens. The SteamGridDB key (for artwork) is the only key SteamAll asks for.
- **Each store opens its own app.** An Ubisoft game opens Ubisoft Connect, not GOG GALAXY or another launcher.
- **Nothing is deleted for good.** Files SteamAll removes go to the Recycle Bin (the Trash on Linux and macOS).
- **Steam's files are written only with Steam closed**, and a backup of the library is kept.
- **Secrets are never printed or saved in plain text.**
- **Shortcuts added to Steam by hand, or by other tools, are left alone.**

## Adding a store

A store is one class that implements `IStoreProvider` (see `EpicStore`, `GogGalaxyStore` and `UbisoftStore`) and one line in `StoreCatalog.For`. It:

- says whether the store app is installed;
- lists the games, installed and owned, from the app's own files;
- gives the command that starts an installed game through the store app, and the link that opens a game's store page (used by Install).

Then add tests with fake copies of the store's files, and the proof described above.

## Code style

Match the code around you: names, structure, and plenty of comments that explain *why*. The existing comments are in Italian because the project was written while learning; new comments in English are fine.

## License

By sending a pull request you agree that your contribution is released under the project's license, the [GNU GPL v3.0](LICENSE).
