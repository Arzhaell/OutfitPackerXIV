using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitPackerXIV;

/// <summary>Une case de la coiffeuse telle que le jeu la donne : l'objet (0 si la case est vide) et ses deux teintures.</summary>
public readonly record struct DresserSlot(int Index, uint ItemId, byte Stain0, byte Stain1);

/// <summary>Un exemplaire d'une pièce rangé seul dans la coiffeuse (pas dans un ensemble).</summary>
public readonly record struct LoosePiece(int Index, bool Dyed);

/// <summary>Une pièce d'un ensemble, et où elle se trouve dans la coiffeuse.</summary>
public sealed class PieceStatus
{
    public PieceStatus(int column, uint itemId, bool inOutfit, IReadOnlyList<LoosePiece> loose, IReadOnlyList<uint> sharedWith)
    {
        Column = column;
        ItemId = itemId;
        InOutfit = inOutfit;
        Loose = loose;
        SharedWith = sharedWith;
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

    /// <summary>Exemplaires en trop : tous si l'ensemble a déjà la pièce, sinon tous sauf un.</summary>
    public int ExtraCopies => InOutfit ? Loose.Count : Math.Max(0, Loose.Count - 1);
}

/// <summary>Un ensemble dont au moins une pièce est rangée seule dans la coiffeuse.</summary>
public sealed class OutfitStatus
{
    public OutfitStatus(OutfitDefinition outfit, int? outfitIndex, IReadOnlyList<PieceStatus> pieces)
    {
        Outfit = outfit;
        OutfitIndex = outfitIndex;
        Pieces = pieces;
    }

    public OutfitDefinition Outfit { get; }

    /// <summary>Case de la coiffeuse où l'ensemble est déjà rangé, ou null s'il n'y est pas.</summary>
    public int? OutfitIndex { get; }

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

/// <summary>Ce que la coiffeuse permet de regrouper.</summary>
public sealed class DresserReport
{
    public DresserReport(int usedSlots, IReadOnlyList<OutfitStatus> outfits)
    {
        UsedSlots = usedSlots;
        Outfits = outfits;
    }

    /// <summary>Cases occupées de la coiffeuse.</summary>
    public int UsedSlots { get; }

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
    public static DresserReport Run(OutfitCatalog catalog, IReadOnlyList<DresserSlot> slots, Func<int, int, bool> isOutfitColumnStored)
    {
        var used = 0;
        var loose = new Dictionary<uint, List<LoosePiece>>();
        var stored = new Dictionary<uint, int>();
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
                if (stored.TryAdd(itemId, slot.Index))
                    outfitIds.Add(itemId);
                continue;
            }

            var outfits = catalog.OutfitsWithPiece(itemId);
            if (outfits.Count == 0)
                continue;

            if (!loose.TryGetValue(itemId, out var copies))
                loose[itemId] = copies = [];
            copies.Add(new LoosePiece(slot.Index, slot.Stain0 != 0 || slot.Stain1 != 0));

            foreach (var outfit in outfits)
            {
                if (!outfitIds.Contains(outfit.SetItemId))
                    outfitIds.Add(outfit.SetItemId);
            }
        }

        var result = new List<OutfitStatus>();
        foreach (var setItemId in outfitIds)
        {
            catalog.TryGetOutfit(setItemId, out var outfit);
            int? outfitIndex = stored.TryGetValue(setItemId, out var index) ? index : null;

            var pieces = outfit.Pieces.Select(p => new PieceStatus(
                p.Column,
                p.ItemId,
                outfitIndex is { } i && isOutfitColumnStored(i, p.Column),
                loose.TryGetValue(p.ItemId, out var copies) ? copies : [],
                catalog.OutfitsWithPiece(p.ItemId).Where(o => o != outfit).Select(o => o.SetItemId).ToArray())).ToArray();

            var status = new OutfitStatus(outfit, outfitIndex, pieces);
            if (status.JoinablePieces > 0 || status.ExtraCopies > 0)
                result.Add(status);
        }

        return new DresserReport(used, result
            .OrderByDescending(o => o.FreedSlots)
            .ThenBy(o => o.MissingPieces)
            .ThenBy(o => o.Outfit.SetItemId)
            .ToArray());
    }
}
