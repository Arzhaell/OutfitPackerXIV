# OutfitPackerXIV

*[English version](README.md)*

Un plugin Dalamud pour FINAL FANTASY XIV qui retrouve, dans ta coiffeuse mirage, les pièces d'équipement d'un même
ensemble, pour les ranger en un seul ensemble et libérer des cases.

## Ce qu'il montre
Ouvre la coiffeuse : la fenêtre d'OutfitPackerXIV s'ouvre à côté (ou tape `/outfitpacker`).

- **Complets** : toutes les pièces de l'ensemble sont dans la coiffeuse, chacune dans sa case.
- **Incomplets** : il manque des pièces (elles sont listées). Le jeu peut quand même ranger l'ensemble, et les autres
  pièces pourront le rejoindre plus tard.
- **À compléter** : l'ensemble est déjà dans la coiffeuse, et certaines de ses pièces sont encore dans leur propre case.
- **En double** : les pièces présentes plusieurs fois dans la coiffeuse, ou déjà dans leur ensemble rangé.

Pour chaque ensemble, le plugin indique combien de cases le ranger libère, et signale les pièces teintes et celles qui
font partie de plusieurs ensembles. Un champ de recherche filtre les ensembles et les pièces par leur nom.

Cette version **ne fait que lire** la coiffeuse : elle ne déplace rien. Le regroupement automatique viendra dans une
prochaine version.

En français et en anglais (suit la langue de Dalamud, réglable dans les réglages).

## Installation
Ajoute ce dépôt dans `/xlsettings`, onglet **Expérimental**, puis installe OutfitPackerXIV depuis `/xlplugins` :

```
https://raw.githubusercontent.com/Arzhaell/OutfitPackerXIV/main/repo.json
```

## Compiler
SDK .NET 10 et XIVLauncher (Dalamud) installés.

```bash
dotnet build -c Release
```

```bash
dotnet test OutfitPackerXIV.Tests/OutfitPackerXIV.Tests.csproj
```

Les tests lisent les ensembles dans les fichiers du jeu installé (en lecture seule). Sur une machine sans le jeu, mettre
`OUTFITPACKER_TESTS_WITHOUT_GAME=1` : ces tests ne vérifient alors rien.

Plugin de dev : ajouter `OutfitPackerXIV\bin\Debug\OutfitPackerXIV.dll` aux emplacements des plugins de dev
(`/xlsettings`, onglet **Expérimental**) ; Dalamud le recharge tout seul après chaque compilation, même jeu ouvert. Il ne
charge pas en même temps la version installée depuis le dépôt : la désactiver.

## Publier une version
Il suffit de pousser un tag annoté `vX.Y.Z` : le workflow GitHub lance les tests, compile le plugin, crée la release (ses
notes sont le message du tag) et met à jour `repo.json`.

```bash
git tag -a v0.1.0 --cleanup=verbatim -F notes.txt
```

```bash
git push origin v0.1.0
```

## Crédits
- [Dalamud](https://github.com/goatcorp/Dalamud) (AGPL-3.0) : chargement du plugin, accès au jeu.
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs) (MIT) : structures du jeu (`MirageManager`, le contenu
  de la coiffeuse).
- [Lumina](https://github.com/NotAdam/Lumina) (MIT) : lecture des données du jeu (feuille `MirageStoreSetItem`, qui donne
  les pièces de chaque ensemble).
- [Dear ImGui](https://github.com/ocornut/imgui) (MIT) : la bibliothèque d'interface utilisée par Dalamud.
- [xUnit](https://xunit.net/) pour les tests.

OutfitPackerXIV est publié sous licence **AGPL-3.0** (voir `LICENSE`), comme Dalamud.

FINAL FANTASY XIV © SQUARE ENIX CO., LTD. Ce plugin n'est ni affilié à Square Enix ni approuvé par Square Enix.
