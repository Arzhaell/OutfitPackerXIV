using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitPackerXIV;

/// <summary>Pourquoi une pièce de la coiffeuse reste dans sa case.</summary>
public enum SkipReason
{
    /// <summary>Teinte : le jeu retirerait sa teinture (option « inclure les pièces teintes »).</summary>
    Dyed,

    /// <summary>Pas de la même qualité (normale ou haute) que les autres pièces : le jeu ne les mélange pas.</summary>
    OtherQuality,
}

/// <summary>Une pièce à sortir de la coiffeuse puis à ranger dans l'ensemble.</summary>
/// <param name="Dyed">Il n'en reste qu'un exemplaire teint : le jeu retirera sa teinture.</param>
public readonly record struct PlannedPiece(int Column, uint ItemId, bool HighQuality, bool Dyed, bool AllowDyed)
{
    /// <summary>Le numéro de la pièce dans la coiffeuse (avec sa qualité).</summary>
    public uint DresserItemId => ItemIds.WithQuality(ItemId, HighQuality);
}

public readonly record struct SkippedPiece(int Column, uint ItemId, SkipReason Reason);

/// <summary>Ce que « Regrouper » va faire pour un ensemble : les pièces à sortir, dans l'ordre de leurs colonnes.</summary>
public sealed class PackPlan
{
    public PackPlan(OutfitDefinition outfit, bool isNew, bool highQuality, IReadOnlyList<PlannedPiece> pieces, IReadOnlyList<SkippedPiece> skipped)
    {
        Outfit = outfit;
        IsNew = isNew;
        HighQuality = highQuality;
        Pieces = pieces;
        Skipped = skipped;
    }

    public OutfitDefinition Outfit { get; }

    /// <summary>Un nouvel ensemble (sinon : des pièces rejoignent l'ensemble déjà rangé).</summary>
    public bool IsNew { get; }

    /// <summary>Ensemble de haute qualité : toutes ses pièces le sont.</summary>
    public bool HighQuality { get; }

    public IReadOnlyList<PlannedPiece> Pieces { get; }

    public IReadOnlyList<SkippedPiece> Skipped { get; }

    /// <summary>Le numéro de l'ensemble dans la coiffeuse (avec sa qualité).</summary>
    public uint DresserSetItemId => ItemIds.WithQuality(Outfit.SetItemId, HighQuality);

    /// <summary>Le jeu exige un prisme mirage par pièce rangée dans l'ensemble.</summary>
    public int PrismCost => Pieces.Count;

    public bool HasWork => Pieces.Count > 0;
}

/// <summary>Choisit les pièces à ranger dans un ensemble. Ne touche à rien.</summary>
public static class PackPlanner
{
    /// <param name="includeDyed">Prendre aussi les pièces teintes (le jeu retire leur teinture).</param>
    public static PackPlan Plan(OutfitStatus status, bool includeDyed)
    {
        var joinable = status.Pieces.Where(p => p.CanJoin).ToArray();

        // Le jeu ne mélange pas les qualités : celle de l'ensemble rangé, sinon celle qui range le plus de pièces.
        bool highQuality;
        if (status.IsStored)
        {
            highQuality = status.StoredHighQuality;
        }
        else
        {
            int Usable(bool hq) => joinable.Count(p => p.Loose.Any(l => l.HighQuality == hq && (includeDyed || !l.Dyed)));
            highQuality = Usable(true) > Usable(false);
        }

        var pieces = new List<PlannedPiece>();
        var skipped = new List<SkippedPiece>();
        foreach (var piece in joinable)
        {
            var copies = piece.Loose.Where(l => l.HighQuality == highQuality).ToArray();
            if (copies.Length == 0)
                skipped.Add(new SkippedPiece(piece.Column, piece.ItemId, SkipReason.OtherQuality));
            else if (!includeDyed && copies.All(c => c.Dyed))
                skipped.Add(new SkippedPiece(piece.Column, piece.ItemId, SkipReason.Dyed));
            else
                pieces.Add(new PlannedPiece(piece.Column, piece.ItemId, highQuality, copies.All(c => c.Dyed), includeDyed));
        }

        return new PackPlan(status.Outfit, !status.IsStored, highQuality, pieces, skipped);
    }
}

/// <summary>Une pièce d'équipement dans les sacs ou l'arsenal : où elle est et ce que le jeu vérifie avant de la ranger.</summary>
/// <param name="Container">Numéro du conteneur du jeu (InventoryType).</param>
/// <param name="Condition">Condition de l'objet : 30 000 = 100 %.</param>
public readonly record struct BagItem(uint Container, int Slot, uint ItemId, bool HighQuality, ushort Condition)
{
    /// <summary>Le jeu refuse de ranger en ensemble une pièce qui n'est pas à 100 %.</summary>
    public const ushort FullCondition = 30_000;

    public (uint Container, int Slot) Position => (Container, Slot);
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
