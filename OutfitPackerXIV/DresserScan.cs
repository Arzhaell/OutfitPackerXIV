using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitPackerXIV;

/// <summary>Une case de la coiffeuse telle que le jeu la donne : l'objet (0 si la case est vide) et ses deux teintures.</summary>
public readonly record struct DresserSlot(int Index, uint ItemId, byte Stain0, byte Stain1);

/// <summary>Un exemplaire d'une pièce rangé seul dans la coiffeuse (pas dans un ensemble), avec ses deux teintures.</summary>
public readonly record struct LoosePiece(int Index, byte Stain0, byte Stain1, bool HighQuality)
{
    public bool Dyed => Stain0 != 0 || Stain1 != 0;
}

/// <summary>
/// Un exemplaire d'une pièce dans les sacs ou l'arsenal, et pourquoi le plugin n'y touche pas (null : il peut le ranger).
/// </summary>
public readonly record struct BagCopy(BagItem Item, SkipReason? Block)
{
    public bool Usable => Block == null;

    /// <summary>
    /// Ce qui empêche de ranger cet exemplaire : ce qu'une pièce d'ensemble d'équipement perdrait, ce que le jeu
    /// détruirait (matérias, mirage), ou ce qu'il refuse (condition).
    /// </summary>
    public static SkipReason? BlockOf(BagItem item, IReadOnlySet<uint> gearsetItemIds)
    {
        if (gearsetItemIds.Contains(ItemIds.Base(item.ItemId)))
            return SkipReason.InGearset;
        if (item.HasMateria)
            return SkipReason.HasMateria;
        if (item.Glamoured)
            return SkipReason.Glamoured;
        if (item.Condition < BagItem.FullCondition)
            return SkipReason.NotRepaired;
        return null;
    }
}

/// <summary>Une pièce d'un ensemble, et où elle se trouve : coiffeuse, sacs, arsenal.</summary>
public sealed class PieceStatus
{
    public PieceStatus(
        int column,
        uint itemId,
        bool inOutfit,
        IReadOnlyList<LoosePiece> loose,
        IReadOnlyList<uint> sharedWith,
        IReadOnlyList<LoosePiece> extra,
        IReadOnlyList<BagCopy>? inBags = null)
    {
        Column = column;
        ItemId = itemId;
        InOutfit = inOutfit;
        Loose = loose;
        SharedWith = sharedWith;
        Extra = extra;
        InBags = inBags ?? [];
    }

    /// <summary>Colonne de la pièce dans l'ensemble (feuille MirageStoreSetItem).</summary>
    public int Column { get; }

    public uint ItemId { get; }

    /// <summary>La pièce est déjà dans l'ensemble rangé dans la coiffeuse.</summary>
    public bool InOutfit { get; }

    /// <summary>Exemplaires rangés seuls dans la coiffeuse.</summary>
    public IReadOnlyList<LoosePiece> Loose { get; }

    /// <summary>Les autres ensembles dont cette pièce fait aussi partie.</summary>
    public IReadOnlyList<uint> SharedWith { get; }

    /// <summary>Ni dans l'ensemble, ni seule dans la coiffeuse.</summary>
    public bool Missing => !InOutfit && Loose.Count == 0;

    /// <summary>Seule dans la coiffeuse et pas encore dans l'ensemble : on peut l'y ranger.</summary>
    public bool CanJoin => !InOutfit && Loose.Count > 0;

    /// <summary>
    /// Exemplaires seuls en trop, qui peuvent sortir de la coiffeuse : un exemplaire reste pour chaque ensemble du jeu à
    /// qui la pièce manque (rangé ou pas encore), le meilleur d'abord. La pièce partagée compte pour tous ses ensembles.
    /// </summary>
    public IReadOnlyList<LoosePiece> Extra { get; }

    public int ExtraCopies => Extra.Count;

    /// <summary>Exemplaires dans les sacs et l'arsenal (jamais l'équipement porté).</summary>
    public IReadOnlyList<BagCopy> InBags { get; }

    /// <summary>Pas encore dans l'ensemble, et au moins un exemplaire dans les sacs ou l'arsenal.</summary>
    public bool CanJoinFromBags => !InOutfit && InBags.Count > 0;
}

/// <summary>Un ensemble dont au moins une pièce est rangée seule dans la coiffeuse.</summary>
public sealed class OutfitStatus
{
    public OutfitStatus(OutfitDefinition outfit, int? outfitIndex, IReadOnlyList<PieceStatus> pieces, bool storedHighQuality = false)
    {
        Outfit = outfit;
        OutfitIndex = outfitIndex;
        Pieces = pieces;
        StoredHighQuality = storedHighQuality;
    }

    public OutfitDefinition Outfit { get; }

    /// <summary>Case de la coiffeuse où l'ensemble est déjà rangé, ou null s'il n'y est pas.</summary>
    public int? OutfitIndex { get; }

    /// <summary>L'ensemble rangé est de haute qualité : seules des pièces de haute qualité peuvent le rejoindre.</summary>
    public bool StoredHighQuality { get; }

    public IReadOnlyList<PieceStatus> Pieces { get; }

    public bool IsStored => OutfitIndex != null;

    /// <summary>Pièces qu'on peut ranger dans l'ensemble (seules dans la coiffeuse et pas encore dedans).</summary>
    public int JoinablePieces => Pieces.Count(p => p.CanJoin);

    public int MissingPieces => Pieces.Count(p => p.Missing);

    /// <summary>Toutes les pièces sont là (dans l'ensemble ou seules dans la coiffeuse).</summary>
    public bool IsComplete => MissingPieces == 0;

    /// <summary>
    /// Cases libérées en rangeant l'ensemble : chaque pièce rangée libère sa case, mais un nouvel ensemble en prend une.
    /// </summary>
    public int FreedSlots => IsStored ? JoinablePieces : Math.Max(0, JoinablePieces - 1);

    /// <summary>Exemplaires de ses pièces qui sont en trop dans la coiffeuse.</summary>
    public int ExtraCopies => Pieces.Sum(p => p.ExtraCopies);

    /// <summary>Une des pièces à ranger est teinte.</summary>
    public bool HasDyedPieces => Pieces.Any(p => p.CanJoin && p.Loose.Any(l => l.Dyed));
}

/// <summary>Ce que la coiffeuse (et, si on les donne, les sacs et l'arsenal) permet de regrouper.</summary>
public sealed class DresserReport
{
    public DresserReport(int usedSlots, IReadOnlyList<OutfitStatus> outfits, int totalSlots)
    {
        UsedSlots = usedSlots;
        Outfits = outfits;
        TotalSlots = totalSlots;
    }

    /// <summary>Cases occupées de la coiffeuse.</summary>
    public int UsedSlots { get; }

    /// <summary>Cases de la coiffeuse en tout.</summary>
    public int TotalSlots { get; }

    /// <summary>Cases vides de la coiffeuse : un nouvel ensemble en prend une.</summary>
    public int FreeSlots => TotalSlots - UsedSlots;

    /// <summary>Les ensembles concernés, du plus grand gain au plus petit.</summary>
    public IReadOnlyList<OutfitStatus> Outfits { get; }

    /// <summary>Nouveaux ensembles dont toutes les pièces sont seules dans la coiffeuse.</summary>
    public IEnumerable<OutfitStatus> Complete => Outfits.Where(o => !o.IsStored && o.IsComplete);

    /// <summary>
    /// Nouveaux ensembles incomplets. Avec une seule pièce, les ranger ne libère rien : on ne les montre que sur demande.
    /// </summary>
    public IEnumerable<OutfitStatus> Partial(bool withSinglePieces) =>
        Outfits.Where(o => !o.IsStored && !o.IsComplete && o.JoinablePieces >= (withSinglePieces ? 1 : 2));

    /// <summary>Ensembles déjà rangés que des pièces seules peuvent rejoindre.</summary>
    public IEnumerable<OutfitStatus> AddToStored => Outfits.Where(o => o.IsStored && o.JoinablePieces > 0);

    /// <summary>Ensembles dont des pièces sont en trop dans la coiffeuse.</summary>
    public IEnumerable<OutfitStatus> Duplicates => Outfits.Where(o => o.ExtraCopies > 0);

    /// <summary>Ensembles dont des pièces sont dans les sacs ou l'arsenal, utilisables ou non.</summary>
    public IEnumerable<OutfitStatus> WithBagPieces => Outfits.Where(o => o.Pieces.Any(p => p.CanJoinFromBags));

    /// <summary>
    /// Cases libérées si on range tout. C'est un maximum : une pièce partagée entre deux ensembles compte dans les deux,
    /// alors qu'elle ne peut rejoindre que l'un d'eux.
    /// </summary>
    public int MaxFreedSlots(bool withSinglePieces) =>
        Complete.Concat(Partial(withSinglePieces)).Concat(AddToStored).Sum(o => o.FreedSlots);
}

/// <summary>Lit les cases de la coiffeuse et retrouve les pièces d'un même ensemble. Ne touche à rien.</summary>
public static class DresserScan
{
    /// <param name="catalog">Les ensembles du jeu.</param>
    /// <param name="slots">Les cases de la coiffeuse.</param>
    /// <param name="isOutfitColumnStored">
    /// Pour un ensemble rangé : la pièce de cette colonne est-elle dedans ? (case de la coiffeuse, colonne)
    /// </param>
    /// <param name="bags">Les objets des sacs et de l'arsenal, ou null pour ne regarder que la coiffeuse.</param>
    /// <param name="gearsetItemIds">Les objets utilisés par les ensembles d'équipement (numéro sans la qualité).</param>
    public static DresserReport Run(
        OutfitCatalog catalog,
        IReadOnlyList<DresserSlot> slots,
        Func<int, int, bool> isOutfitColumnStored,
        IReadOnlyList<BagItem>? bags = null,
        IReadOnlySet<uint>? gearsetItemIds = null)
    {
        var used = 0;
        var loose = new Dictionary<uint, List<LoosePiece>>();
        var stored = new Dictionary<uint, DresserSlot>();
        var outfitIds = new List<uint>();

        foreach (var slot in slots)
        {
            if (slot.ItemId == 0)
                continue;

            used++;
            var itemId = ItemIds.Base(slot.ItemId);
            if (catalog.TryGetOutfit(itemId, out _))
            {
                // Les teintures d'une case « ensemble » ne sont pas des teintures : le jeu y note les pièces rangées.
                // Une pièce seule de cet ensemble a pu être vue avant lui : il ne doit être listé qu'une fois.
                if (stored.TryAdd(itemId, slot) && !outfitIds.Contains(itemId))
                    outfitIds.Add(itemId);
                continue;
            }

            var outfits = catalog.OutfitsWithPiece(itemId);
            if (outfits.Count == 0)
                continue;

            if (!loose.TryGetValue(itemId, out var copies))
                loose[itemId] = copies = [];
            copies.Add(new LoosePiece(slot.Index, slot.Stain0, slot.Stain1, ItemIds.IsHighQuality(slot.ItemId)));

            foreach (var outfit in outfits)
            {
                if (!outfitIds.Contains(outfit.SetItemId))
                    outfitIds.Add(outfit.SetItemId);
            }
        }

        // Les pièces d'ensemble des sacs et de l'arsenal, avec ce qui empêche de les ranger.
        var inBags = new Dictionary<uint, List<BagCopy>>();
        foreach (var item in bags ?? [])
        {
            var itemId = ItemIds.Base(item.ItemId);
            var outfits = catalog.OutfitsWithPiece(itemId);
            if (outfits.Count == 0)
                continue;

            if (!inBags.TryGetValue(itemId, out var copies))
                inBags[itemId] = copies = [];
            copies.Add(new BagCopy(item, BagCopy.BlockOf(item, gearsetItemIds ?? new HashSet<uint>())));

            foreach (var outfit in outfits)
            {
                if (!outfitIds.Contains(outfit.SetItemId))
                    outfitIds.Add(outfit.SetItemId);
            }
        }

        var extra = loose.ToDictionary(l => l.Key, l => ExtraCopies(catalog, l.Key, l.Value, stored, isOutfitColumnStored));
        var result = new List<OutfitStatus>();
        foreach (var setItemId in outfitIds)
        {
            catalog.TryGetOutfit(setItemId, out var outfit);
            var isStored = stored.TryGetValue(setItemId, out var storedSlot);
            int? outfitIndex = isStored ? storedSlot.Index : null;

            var pieces = outfit.Pieces.Select(p => new PieceStatus(
                p.Column,
                p.ItemId,
                outfitIndex is { } i && isOutfitColumnStored(i, p.Column),
                loose.TryGetValue(p.ItemId, out var copies) ? copies : [],
                catalog.OutfitsWithPiece(p.ItemId).Where(o => o != outfit).Select(o => o.SetItemId).ToArray(),
                extra.TryGetValue(p.ItemId, out var extraCopies) ? extraCopies : [],
                inBags.TryGetValue(p.ItemId, out var bagCopies) ? bagCopies : [])).ToArray();

            var status = new OutfitStatus(outfit, outfitIndex, pieces, isStored && ItemIds.IsHighQuality(storedSlot.ItemId));
            if (status.JoinablePieces > 0 || status.ExtraCopies > 0 || status.Pieces.Any(p => p.CanJoinFromBags))
                result.Add(status);
        }

        var ordered = result
            .OrderByDescending(o => o.FreedSlots)
            .ThenBy(o => o.MissingPieces)
            .ThenBy(o => o.Outfit.SetItemId)
            .ToArray();
        return new DresserReport(used, ordered, slots.Count);
    }

    // Un exemplaire reste pour chaque ensemble du jeu à qui la pièce manque, rangé ou pas encore : ainsi aucun ensemble ne
    // perd la pièce, et elle reste toujours dans la coiffeuse (seule ou dans un ensemble). Les ensembles rangés d'abord,
    // car leur qualité est imposée ; puis le meilleur exemplaire : non teint, puis de qualité normale.
    private static IReadOnlyList<LoosePiece> ExtraCopies(
        OutfitCatalog catalog,
        uint itemId,
        List<LoosePiece> copies,
        Dictionary<uint, DresserSlot> stored,
        Func<int, int, bool> isOutfitColumnStored)
    {
        var needers = new List<(bool IsStored, bool HighQuality)>();
        foreach (var outfit in catalog.OutfitsWithPiece(itemId))
        {
            var column = outfit.Pieces.First(p => p.ItemId == itemId).Column;
            if (!stored.TryGetValue(outfit.SetItemId, out var slot))
                needers.Add((false, false));
            else if (!isOutfitColumnStored(slot.Index, column))
                needers.Add((true, ItemIds.IsHighQuality(slot.ItemId)));
        }

        var remaining = copies.ToList();
        foreach (var (isStored, highQuality) in needers.OrderByDescending(n => n.IsStored))
        {
            if (remaining.Count == 0)
                break;

            var kept = remaining
                .OrderBy(c => isStored && c.HighQuality != highQuality)
                .ThenBy(c => c.Dyed)
                .ThenBy(c => c.HighQuality)
                .First();
            remaining.Remove(kept);
        }

        return remaining;
    }
}
