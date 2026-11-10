namespace OutfitPackerXIV.Tests;

/// <summary>Les ensembles lus dans les fichiers du jeu installé (lecture seule).</summary>
public class GameCatalogTests
{
    [Fact]
    public void GameHasOverAThousandOutfits()
    {
        if (GameFiles.Catalog is not { } catalog)
            return;

        // 1 088 au patch du 3 novembre 2026 : le nombre ne peut que grandir.
        Assert.True(catalog.Count >= 1000, $"{catalog.Count} outfits");
    }

    [Fact]
    public void DreamOutfitHasItsThreePieces()
    {
        if (GameFiles.Catalog is not { } catalog)
            return;

        // 45094 « Tenue onirique » : bonnet (tête), tunique (corps), bottes (pieds).
        Assert.True(catalog.TryGetOutfit(45094, out var outfit));
        Assert.Equal(3, outfit.Pieces.Count);
        Assert.Equal(2642u, outfit.Columns[2]);
        Assert.Contains(outfit, catalog.OutfitsWithPiece(2642));
    }

    [Fact]
    public void RowZeroIsNotAnOutfit()
    {
        if (GameFiles.Catalog is not { } catalog)
            return;

        // La ligne 0 ne contient que les gils (objet 1).
        Assert.False(catalog.TryGetOutfit(0, out _));
        Assert.Empty(catalog.OutfitsWithPiece(1));
    }

    [Fact]
    public void NoOutfitHasMorePiecesThanTheGameAccepts()
    {
        if (GameFiles.Catalog is not { } catalog)
            return;

        // StoreNewOutfit lit 9 pièces au plus ; le plus grand ensemble en a 9 (jeu du 10 novembre 2026).
        Assert.True(catalog.MaxPieces <= StoreArguments.MaxPieces, $"{catalog.MaxPieces} pieces");
    }

    [Fact]
    public void SomePiecesBelongToSeveralOutfits()
    {
        if (GameFiles.Catalog is not { } catalog)
            return;

        // 6971 « Chapeau de la Valention » : ensemble tablier et ensemble robe-tablier.
        Assert.Equal(2, catalog.OutfitsWithPiece(6971).Count);
    }
}
