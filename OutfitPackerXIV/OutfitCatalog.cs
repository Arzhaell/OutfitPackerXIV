using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace OutfitPackerXIV;

/// <summary>
/// Un ensemble du jeu (feuille MirageStoreSetItem) : l'objet « tenue » rangé dans la coiffeuse et ses pièces, une par
/// colonne : main droite, main gauche, tête, corps, mains, jambes, pieds, oreilles, cou, poignets, bague.
/// </summary>
public sealed class OutfitDefinition
{
    public OutfitDefinition(uint setItemId, IReadOnlyList<uint> columns)
    {
        if (columns.Count != OutfitCatalog.ColumnCount)
            throw new ArgumentException($"An outfit has {OutfitCatalog.ColumnCount} columns, not {columns.Count}.", nameof(columns));

        SetItemId = setItemId;
        Columns = columns.ToArray();
        Pieces = Columns.Select((itemId, column) => (column, itemId)).Where(p => p.itemId != 0).ToArray();
    }

    /// <summary>L'objet « tenue » : c'est lui qui occupe une case de la coiffeuse une fois l'ensemble rangé.</summary>
    public uint SetItemId { get; }

    /// <summary>Pièce de chaque colonne (<see cref="OutfitCatalog.ColumnCount"/>), 0 pour une colonne vide.</summary>
    public IReadOnlyList<uint> Columns { get; }

    /// <summary>Les pièces de l'ensemble avec leur colonne, dans l'ordre des colonnes.</summary>
    public IReadOnlyList<(int Column, uint ItemId)> Pieces { get; }
}

/// <summary>Tous les ensembles du jeu, et pour chaque pièce, les ensembles dont elle fait partie.</summary>
public sealed class OutfitCatalog
{
    /// <summary>Colonnes de la feuille MirageStoreSetItem (le jeu numérote les pièces d'un ensemble dans cet ordre).</summary>
    public const int ColumnCount = 11;

    private readonly Dictionary<uint, OutfitDefinition> outfits = [];
    private readonly Dictionary<uint, List<OutfitDefinition>> outfitsByPiece = [];

    public OutfitCatalog(IEnumerable<OutfitDefinition> definitions)
    {
        foreach (var outfit in definitions)
        {
            if (outfit.Pieces.Count == 0 || !outfits.TryAdd(outfit.SetItemId, outfit))
                continue;

            foreach (var (_, itemId) in outfit.Pieces)
            {
                if (!outfitsByPiece.TryGetValue(itemId, out var list))
                    outfitsByPiece[itemId] = list = [];
                if (!list.Contains(outfit))
                    list.Add(outfit);
            }
        }
    }

    public int Count => outfits.Count;

    /// <summary>Le nombre de pièces du plus grand ensemble.</summary>
    public int MaxPieces => outfits.Values.Max(o => o.Pieces.Count);

    /// <summary>
    /// Les ensembles de la feuille du jeu. La ligne 0 n'est pas un ensemble (elle ne contient que les gils) et des lignes
    /// sont vides : elles sont ignorées.
    /// </summary>
    public static OutfitCatalog FromSheet(IEnumerable<MirageStoreSetItem> rows) => new(rows
        .Where(row => row.RowId != 0)
        .Select(row => new OutfitDefinition(row.RowId,
        [
            row.MainHand.RowId, row.OffHand.RowId, row.Head.RowId, row.Body.RowId, row.Hands.RowId, row.Legs.RowId,
            row.Feet.RowId, row.Earrings.RowId, row.Necklace.RowId, row.Bracelets.RowId, row.Ring.RowId,
        ])));

    /// <summary>L'objet est-il un ensemble (l'objet « tenue ») ?</summary>
    public bool TryGetOutfit(uint setItemId, out OutfitDefinition outfit) => outfits.TryGetValue(setItemId, out outfit!);

    /// <summary>Les ensembles dont l'objet est une pièce (souvent un seul, parfois plusieurs).</summary>
    public IReadOnlyList<OutfitDefinition> OutfitsWithPiece(uint itemId) =>
        outfitsByPiece.TryGetValue(itemId, out var list) ? list : [];
}

/// <summary>Numéros d'objets tels que le jeu les range.</summary>
public static class ItemIds
{
    private const uint HighQualityOffset = 1_000_000;

    /// <summary>L'objet est-il de haute qualité (le jeu ajoute 1 000 000 à son numéro) ?</summary>
    public static bool IsHighQuality(uint itemId) => itemId is >= HighQualityOffset and < 2 * HighQualityOffset;

    /// <summary>Le numéro que le jeu donne à l'objet dans cette qualité.</summary>
    public static uint WithQuality(uint baseItemId, bool highQuality) => highQuality ? baseItemId + HighQualityOffset : baseItemId;

    /// <summary>
    /// Le numéro de l'objet lui-même : le jeu ajoute 1 000 000 pour la haute qualité et 500 000 pour les objets de
    /// collection. Au-delà de 2 000 000 (objets clés), rien n'est retiré.
    /// </summary>
    public static uint Base(uint itemId) => itemId switch
    {
        >= 2_000_000 => itemId,
        >= 1_000_000 => itemId - 1_000_000,
        >= 500_000 => itemId - 500_000,
        _ => itemId,
    };
}
