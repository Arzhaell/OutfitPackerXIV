# OutfitPackerXIV

*[Version française](README.fr.md)*

A Dalamud plugin for FINAL FANTASY XIV that finds, in your Glamour Dresser, the pieces of gear that belong to the same
outfit, so you can store them as one outfit and free up slots.

## What it shows
Open the Glamour Dresser: the OutfitPackerXIV window opens next to it (or type `/outfitpacker`).

- **Complete**: every piece of the outfit is in the dresser, each in its own slot.
- **Incomplete**: some pieces are missing (they are listed). The game can still store the outfit, and the other pieces
  can join it later.
- **Add to stored**: the outfit is already in the dresser, and some of its pieces are still in their own slot.
- **Duplicates**: pieces that are in the dresser more than once, or already inside their stored outfit.

For each outfit, the plugin shows how many dresser slots storing it would free, and flags dyed pieces and pieces shared
by several outfits. A search field filters outfits and pieces by name.

This version **only reads** the dresser: it never moves anything. Grouping the outfits for you will come in a later
version.

Available in English and French (follows Dalamud's language, can be forced in the settings).

## Installing
Add this custom repository in `/xlsettings`, **Experimental** tab, then install OutfitPackerXIV from `/xlplugins`:

```
https://raw.githubusercontent.com/Arzhaell/OutfitPackerXIV/main/repo.json
```

## Building
.NET 10 SDK and XIVLauncher (Dalamud) installed.

```bash
dotnet build -c Release
```

```bash
dotnet test OutfitPackerXIV.Tests/OutfitPackerXIV.Tests.csproj
```

The tests read the outfits from the installed game files (read-only). On a machine without the game, set
`OUTFITPACKER_TESTS_WITHOUT_GAME=1`: those tests then check nothing.

Dev plugin: add `OutfitPackerXIV\bin\Debug\OutfitPackerXIV.dll` to the dev plugin locations (`/xlsettings`,
**Experimental** tab); Dalamud reloads it by itself after each build, even with the game running. It does not load the
version installed from the repository at the same time: disable that one.

## Releasing a new version
Pushing an annotated `vX.Y.Z` tag is all it takes: the GitHub workflow runs the tests, builds the plugin, creates the
release (its notes are the tag message) and updates `repo.json`.

```bash
git tag -a v0.1.0 --cleanup=verbatim -F notes.txt
```

```bash
git push origin v0.1.0
```

## Credits
- [Dalamud](https://github.com/goatcorp/Dalamud) (AGPL-3.0): plugin loading, access to the game.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) (MIT): game structures (`MirageManager`, the Glamour
  Dresser contents).
- [Lumina](https://github.com/NotAdam/Lumina) (MIT): reading the game data (`MirageStoreSetItem` sheet, which lists the
  pieces of each outfit).
- [Dear ImGui](https://github.com/ocornut/imgui) (MIT): the user interface library used by Dalamud.
- [xUnit](https://xunit.net/) for the tests.

OutfitPackerXIV is released under the **AGPL-3.0** license (see `LICENSE`), like Dalamud.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. This plugin is not affiliated with or endorsed by Square Enix.
