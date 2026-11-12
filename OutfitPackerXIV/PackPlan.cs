using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitPackerXIV;

/// <summary>Pourquoi une pièce reste à sa place (case de la coiffeuse, sac ou arsenal).</summary>
public enum SkipReason
{
    /// <summary>Il n'en reste que des exemplaires teints : le jeu retirerait la teinture (le plugin demande avant).</summary>
    Dyed,

    /// <summary>Pas de la même qualité (normale ou haute) que les autres pièces : le jeu ne les mélange pas.</summary>
    OtherQuality,

    /// <summary>Utilisée par un ensemble d'équipement : il lui manquerait une pièce.</summary>
    InGearset,

    /// <summary>Des matérias sont serties : le jeu les détruirait.</summary>
    HasMateria,

    /// <summary>Un mirage est projeté dessus : le jeu le retirerait.</summary>
    Glamoured,

    /// <summary>Pas à 100 % de condition : le jeu refuse de la ranger.</summary>
    NotRepaired,
}

/// <summary>Une pièce à ranger dans l'ensemble : à sortir d'abord de la coiffeuse, ou déjà dans un sac ou l'arsenal.</summary>
/// <param name="Dyed">Il n'en reste qu'un exemplaire teint : le jeu retirera sa teinture.</param>
/// <param name="Bag">Là où elle est dans les sacs ou l'arsenal, ou null si elle est dans la coiffeuse.</param>
public readonly record struct PlannedPiece(int Column, uint ItemId, bool HighQuality, bool Dyed, bool AllowDyed, BagItem? Bag = null)
{
    /// <summary>Le numéro de la pièce dans la coiffeuse (avec sa qualité).</summary>
    public uint DresserItemId => ItemIds.WithQuality(ItemId, HighQuality);
}

public readonly record struct SkippedPiece(int Column, uint ItemId, SkipReason Reason);

/// <summary>
/// Ce que « Regrouper » (ou « Ranger », depuis l'inventaire) va faire pour un ensemble : les pièces à ranger, dans l'ordre
/// de leurs colonnes.
/// </summary>
public sealed class PackPlan
{
    public PackPlan(OutfitDefinition outfit, bool isNew, bool highQuality, IReadOnlyList<PlannedPiece> pieces, IReadOnlyList<SkippedPiece> skipped)
    {
        Outfit = outfit;
        IsNew = isNew;
        HighQuality = highQuality;
        Pieces = pieces;
        Skipped = skipped;
        ToRestore = pieces.Where(p => p.Bag == null).ToArray();
        FromBags = pieces.Where(p => p.Bag != null).ToArray();
    }

    public OutfitDefinition Outfit { get; }

    /// <summary>Un nouvel ensemble (sinon : des pièces rejoignent l'ensemble déjà rangé).</summary>
    public bool IsNew { get; }

    /// <summary>Ensemble de haute qualité : toutes ses pièces le sont.</summary>
    public bool HighQuality { get; }

    public IReadOnlyList<PlannedPiece> Pieces { get; }

    public IReadOnlyList<SkippedPiece> Skipped { get; }

    /// <summary>Les pièces à sortir d'abord de la coiffeuse.</summary>
    public IReadOnlyList<PlannedPiece> ToRestore { get; }

    /// <summary>Les pièces déjà dans les sacs ou l'arsenal : le jeu les range directement.</summary>
    public IReadOnlyList<PlannedPiece> FromBags { get; }

    /// <summary>Cases libérées dans la coiffeuse : chaque pièce qui en sort, moins la case d'un nouvel ensemble.</summary>
    public int DresserSlotsFreed => ToRestore.Count - (IsNew ? 1 : 0);

    /// <summary>Cases libérées dans les sacs et l'arsenal.</summary>
    public int BagSlotsFreed => FromBags.Count;

    /// <summary>Le numéro de l'ensemble dans la coiffeuse (avec sa qualité).</summary>
    public uint DresserSetItemId => ItemIds.WithQuality(Outfit.SetItemId, HighQuality);

    /// <summary>Le jeu exige un prisme mirage par pièce rangée dans l'ensemble.</summary>
    public int PrismCost => Pieces.Count;

    public bool HasWork => Pieces.Count > 0;
}

/// <summary>
/// Les deux façons de regrouper un ensemble : sans les pièces qui ne sont là que teintes, ou avec (le jeu retire leur
/// teinture). Une seule pièce par colonne, un exemplaire non teint d'abord.
/// </summary>
public sealed class PackChoice
{
    public PackChoice(PackPlan undyed, PackPlan withDyed)
    {
        Undyed = undyed;
        WithDyed = withDyed;
    }

    /// <summary>Sans les pièces qui ne sont là que teintes : elles restent dans leur case.</summary>
    public PackPlan Undyed { get; }

    /// <summary>Avec les pièces qui ne sont là que teintes.</summary>
    public PackPlan WithDyed { get; }

    /// <summary>Des pièces ne sont là que teintes : demander s'il faut les ranger quand même.</summary>
    public bool AsksAboutDyed => DyedOnly.Count > 0;

    /// <summary>Les pièces dont il ne reste que des exemplaires teints.</summary>
    public IReadOnlyList<PlannedPiece> DyedOnly => WithDyed.Pieces.Where(p => p.Dyed).ToArray();

    /// <summary>Ce qui sert à décrire le bouton : sans les teintes s'il y a de quoi faire, sinon avec.</summary>
    public PackPlan Main => Undyed.HasWork ? Undyed : WithDyed;
}

/// <summary>Choisit les pièces à ranger dans un ensemble. Ne touche à rien.</summary>
public static class PackPlanner
{
    /// <param name="useBags">Prendre aussi les pièces des sacs et de l'arsenal (onglet Inventaire).</param>
    public static PackChoice Choose(OutfitStatus status, bool useBags = false) =>
        new(Plan(status, includeDyed: false, useBags), Plan(status, includeDyed: true, useBags));

    /// <param name="includeDyed">Prendre aussi les pièces qui ne sont là que teintes (le jeu retire leur teinture).</param>
    /// <param name="useBags">Prendre aussi les pièces des sacs et de l'arsenal que le jeu peut ranger sans rien détruire.</param>
    public static PackPlan Plan(OutfitStatus status, bool includeDyed, bool useBags = false)
    {
        var columns = status.Pieces
            .Where(p => !p.InOutfit)
            .Select(p => (Piece: p, Copies: Candidates(p, useBags)))
            .Where(c => c.Copies.Count > 0 || (useBags && c.Piece.InBags.Count > 0))
            .ToArray();

        // Le jeu ne mélange pas les qualités : celle de l'ensemble rangé, sinon celle qui range le plus de pièces.
        bool highQuality;
        if (status.IsStored)
        {
            highQuality = status.StoredHighQuality;
        }
        else
        {
            int Usable(bool hq) => columns.Count(c => c.Copies.Any(x => x.HighQuality == hq && (includeDyed || !x.Dyed)));
            highQuality = Usable(true) > Usable(false);
        }

        var pieces = new List<PlannedPiece>();
        var skipped = new List<SkippedPiece>();
        foreach (var (piece, copies) in columns)
        {
            // Seulement des exemplaires de l'inventaire que le jeu abîmerait ou refuserait.
            if (copies.Count == 0)
            {
                skipped.Add(new SkippedPiece(piece.Column, piece.ItemId, piece.InBags.Min(b => b.Block!.Value)));
                continue;
            }

            // Non teint d'abord ; puis celui de la coiffeuse, qui y libère une case (celui de l'inventaire reste
            // vendable, alors qu'un objet sorti de la coiffeuse est lié au personnage).
            var best = copies
                .Where(c => c.HighQuality == highQuality)
                .OrderBy(c => c.Dyed)
                .ThenBy(c => c.Bag != null)
                .FirstOrDefault();
            if (best == null)
                skipped.Add(new SkippedPiece(piece.Column, piece.ItemId, SkipReason.OtherQuality));
            else if (!includeDyed && best.Dyed)
                skipped.Add(new SkippedPiece(piece.Column, piece.ItemId, SkipReason.Dyed));
            else
                pieces.Add(new PlannedPiece(piece.Column, piece.ItemId, highQuality, best.Dyed, includeDyed, best.Bag));
        }

        return new PackPlan(status.Outfit, !status.IsStored, highQuality, pieces, skipped);
    }

    private sealed record Candidate(bool HighQuality, bool Dyed, BagItem? Bag);

    // Les exemplaires seuls dans la coiffeuse, et avec useBags ceux des sacs et de l'arsenal qu'on peut ranger.
    private static List<Candidate> Candidates(PieceStatus piece, bool useBags)
    {
        var copies = piece.Loose.Select(l => new Candidate(l.HighQuality, l.Dyed, null)).ToList();
        if (useBags)
            copies.AddRange(piece.InBags.Where(b => b.Usable).Select(b => new Candidate(b.Item.HighQuality, b.Item.Dyed, b.Item)));
        return copies;
    }
}

/// <summary>
/// Une pièce d'équipement dans les sacs ou l'arsenal : où elle est, ce que le jeu vérifie avant de la ranger, et ce qu'il
/// efface quand il la range en ensemble (teintures, matérias, mirage, signature).
/// </summary>
/// <param name="Container">Numéro du conteneur du jeu (InventoryType).</param>
/// <param name="Condition">Condition de l'objet : 30 000 = 100 %.</param>
/// <param name="HasMateria">Des matérias sont serties : le jeu les détruirait.</param>
/// <param name="Glamoured">Un mirage est projeté dessus : le jeu le retirerait.</param>
/// <param name="Signed">Signée par son artisan : le jeu retirerait la signature.</param>
public readonly record struct BagItem(
    uint Container,
    int Slot,
    uint ItemId,
    bool HighQuality,
    ushort Condition,
    byte Stain0 = 0,
    byte Stain1 = 0,
    bool HasMateria = false,
    bool Glamoured = false,
    bool Signed = false)
{
    /// <summary>Le jeu refuse de ranger en ensemble une pièce qui n'est pas à 100 %.</summary>
    public const ushort FullCondition = 30_000;

    /// <summary>Les conteneurs de l'arsenal ont des numéros à partir de 3200 ; les quatre sacs, de 0 à 3.</summary>
    private const uint FirstArmoryContainer = 3200;

    public (uint Container, int Slot) Position => (Container, Slot);

    public bool Dyed => Stain0 != 0 || Stain1 != 0;

    public bool InArmory => Container >= FirstArmoryContainer;
}

/// <summary>Les tableaux que <c>MirageManager.StoreNewOutfit</c> et <c>StoreExistingOutfit</c> attendent.</summary>
public static class StoreArguments
{
    /// <summary>Le jeu lit 9 pièces au plus (le plus grand ensemble en a 9).</summary>
    public const int MaxPieces = 9;

    /// <summary>InventoryType.Invalid : marque la fin de la liste des pièces.</summary>
    public const uint InvalidContainer = 9999;

    /// <summary>
    /// Les conteneurs et les cases des pièces, dans l'ordre des colonnes de l'ensemble, sans trou : le jeu lit jusqu'au
    /// premier conteneur invalide et compare chaque pièce à la colonne suivante de l'ensemble.
    /// </summary>
    public static (uint[] Containers, ushort[] Slots) Build(IReadOnlyList<BagItem> piecesInColumnOrder)
    {
        if (piecesInColumnOrder.Count is 0 or > MaxPieces)
            throw new ArgumentException($"An outfit is stored with 1 to {MaxPieces} pieces, not {piecesInColumnOrder.Count}.", nameof(piecesInColumnOrder));

        var containers = Enumerable.Repeat(InvalidContainer, MaxPieces).ToArray();
        var slots = new ushort[MaxPieces];
        for (var i = 0; i < piecesInColumnOrder.Count; i++)
        {
            containers[i] = piecesInColumnOrder[i].Container;
            slots[i] = checked((ushort)piecesInColumnOrder[i].Slot);
        }

        return (containers, slots);
    }
}
