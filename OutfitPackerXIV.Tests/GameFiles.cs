using Lumina;
using Lumina.Excel.Sheets;

namespace OutfitPackerXIV.Tests;

/// <summary>
/// Lecture seule des fichiers du jeu installé. Sans le jeu, les tests qui en ont besoin échouent, sauf avec la variable
/// d'environnement OUTFITPACKER_TESTS_WITHOUT_GAME=1 (machine sans le jeu, comme GitHub Actions) : ils ne vérifient alors rien.
/// </summary>
internal static class GameFiles
{
    private const string SqPackPath = @"C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn\game\sqpack";

    private static readonly Lazy<GameData?> Data = new(() =>
    {
        if (!Directory.Exists(SqPackPath))
        {
            if (Environment.GetEnvironmentVariable("OUTFITPACKER_TESTS_WITHOUT_GAME") == "1")
                return null;
            throw new InvalidOperationException($"Game not found in {SqPackPath} (set OUTFITPACKER_TESTS_WITHOUT_GAME=1 to skip).");
        }

        return new GameData(SqPackPath, new LuminaOptions { PanicOnSheetChecksumMismatch = false });
    });

    private static readonly Lazy<OutfitCatalog?> CatalogValue = new(() =>
        Data.Value is { } data ? OutfitCatalog.FromSheet(data.GetExcelSheet<MirageStoreSetItem>()!) : null);

    /// <summary>Les ensembles du jeu installé, ou null si on a demandé de se passer du jeu.</summary>
    public static OutfitCatalog? Catalog => CatalogValue.Value;
}
