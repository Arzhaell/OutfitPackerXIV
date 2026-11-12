# OutfitPackerXIV

*[Version française](README.fr.md)*

A Dalamud plugin for FINAL FANTASY XIV that finds, in your Glamour Dresser, the pieces of gear that belong to the same
outfit, so you can store them as one outfit and free up slots.

## What it finds
Open the Glamour Dresser: the OutfitPackerXIV window opens next to it (or type `/outfitpacker`).

- **Complete**: every piece of the outfit is in the dresser, each in its own slot.
- **Incomplete**: some pieces are missing (they are listed). The game can still store the outfit, and the other pieces
  can join it later.
- **Add to stored**: the outfit is already in the dresser, and some of its pieces are still in their own slot.
- **Duplicates**: pieces that are in the dresser more than once, or already inside their stored outfit.

For each outfit, the plugin shows how many dresser slots storing it would free, and flags dyed pieces and pieces shared
by several outfits. A search field filters outfits and pieces by name.

## Packing an outfit
The **Pack** button in front of an outfit (Complete, Incomplete and Add to stored tabs) does it for you, one action at
a time: it takes each piece out of the dresser, waits for the game to confirm, then stores the pieces as an outfit (or
adds them to the stored one). Its tooltip tells what it will do beforehand.

- **Glamour Prisms**: the game uses up to one per piece stored in an outfit. The button is greyed out if you do not
  have enough.
- **One copy per piece**, undyed first. If a piece is only in the dresser dyed, the plugin asks whether to store it
  too: the game removes the dye of the pieces it stores in an outfit.
- **Quality**: the game does not mix normal and high-quality pieces in one outfit. The plugin takes the quality that
  stores the most pieces (or the one of the stored outfit); the others stay in their slot.
- Keep the dresser open: closing it stops everything. The game sometimes refuses to take a piece out right after the
  previous action: the plugin tries again a few times. If it still refuses (full inventory), the pieces already out are
  stored in the outfit. **Stop** (or closing the plugin window) does the same.
- “Pack” only uses the pieces it has just taken out of the dresser, never the items that were already in your
  inventory (that is what the Inventory tab is for). A message (in the window and in the chat) sums up what was done, and lists any piece left in your
  inventory.

## Storing from your inventory
The **Inventory** tab lists the outfits with pieces in your bags or armory chest (never your equipped gear). A line at
the top of the window tells you when there are some. The **Store** button stores those pieces as an outfit, or adds
them to the stored one, directly: no need to put them in the dresser first. If other pieces of the outfit are in the
dresser, it takes them out first, like “Pack”. When a piece is both in the dresser and in your inventory, the dresser
copy is used: it frees a dresser slot, and the inventory copy can still be sold.

The game wipes some things off a piece it stores in an outfit, so these pieces stay where they are, with the reason
shown: pieces with **materia** (they would be destroyed), with a **glamour**, **used by one of your gear sets** (it
would lose a piece), or not at 100% condition (the game refuses them: repair them first). Dyed pieces: same question as
for “Pack”. Signed pieces are stored, and the tooltip warns that the crafter's signature goes away.

## Removing duplicates
In the **Duplicates** tab, the **Remove** button takes the extra copies of an outfit's pieces out of the dresser, one
at a time, into your inventory. One copy always stays for each outfit that does not have the piece yet (a piece shared
by two outfits keeps two), and the best one stays: undyed, of the right quality. The plugin never throws anything
away: selling, desynthesizing or discarding is up to you. The game binds items taken out of the dresser to your
character (no more market board). Same stops as “Pack”: full inventory, dresser closed, Stop.

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
