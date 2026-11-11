# OutfitPackerXIV

*[English version](README.md)*

Un plugin Dalamud pour FINAL FANTASY XIV qui retrouve, dans ta coiffeuse mirage, les pièces d'équipement d'un même
ensemble, pour les ranger en un seul ensemble et libérer des cases.

## Ce qu'il trouve
Ouvre la coiffeuse : la fenêtre d'OutfitPackerXIV s'ouvre à côté (ou tape `/outfitpacker`).

- **Complets** : toutes les pièces de l'ensemble sont dans la coiffeuse, chacune dans sa case.
- **Incomplets** : il manque des pièces (elles sont listées). Le jeu peut quand même ranger l'ensemble, et les autres
  pièces pourront le rejoindre plus tard.
- **À compléter** : l'ensemble est déjà dans la coiffeuse, et certaines de ses pièces sont encore dans leur propre case.
- **En double** : les pièces présentes plusieurs fois dans la coiffeuse, ou déjà dans leur ensemble rangé.

Pour chaque ensemble, le plugin indique combien de cases le ranger libère, et signale les pièces teintes et celles qui
font partie de plusieurs ensembles. Un champ de recherche filtre les ensembles et les pièces par leur nom.

## Regrouper un ensemble
Le bouton **Regrouper** devant un ensemble (onglets Complets, Incomplets et À compléter) le fait à ta place, une
action à la fois : il sort chaque pièce de la coiffeuse, attend que le jeu confirme, puis range les pièces en un
ensemble (ou les ajoute à l'ensemble déjà rangé). Son infobulle dit à l'avance ce qu'il va faire.

- **Prismes mirage** : le jeu en utilise jusqu'à un par pièce rangée dans un ensemble. Le bouton est grisé s'il t'en
  manque.
- **Un seul exemplaire par pièce**, non teint en priorité. Si une pièce n'est dans la coiffeuse qu'en version teinte,
  le plugin demande s'il doit la ranger aussi : le jeu retire la teinture des pièces qu'il range dans un ensemble.
- **Qualité** : le jeu ne mélange pas pièces normales et de haute qualité dans un ensemble. Le plugin prend la qualité
  qui range le plus de pièces (ou celle de l'ensemble déjà rangé) ; les autres restent dans leur case.
- Garde la coiffeuse ouverte : la fermer arrête tout. Le jeu refuse parfois de sortir une pièce juste après l'action
  précédente : le plugin réessaie quelques fois. S'il refuse toujours (inventaire plein), les pièces déjà sorties sont
  rangées dans l'ensemble. **Stop** (ou fermer la fenêtre du plugin) fait de même.
- Le plugin n'utilise que les pièces qu'il vient de sortir de la coiffeuse, jamais les objets qui étaient déjà dans ton
  inventaire. Un message (dans la fenêtre et dans le chat) résume ce qui a été fait, et cite les pièces restées dans
  ton inventaire s'il y en a.

## Enlever les doublons
Dans l'onglet **En double**, le bouton **Enlever** sort de la coiffeuse les exemplaires en trop d'un ensemble, un à la
fois, vers ton inventaire. Un exemplaire reste toujours pour chaque ensemble qui n'a pas encore la pièce (une pièce
partagée entre deux ensembles en garde deux), et c'est le meilleur qui reste : non teint, de la bonne qualité. Le
plugin ne jette rien : à toi de vendre, recycler ou jeter ce qui est sorti. Le jeu lie au personnage les objets qui
sortent de la coiffeuse (plus de vente au marché). Mêmes arrêts que pour « Regrouper » : inventaire plein, coiffeuse
fermée, Stop.

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
