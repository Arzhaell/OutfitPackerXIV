namespace OutfitPackerXIV.Tests;

public class PackPlannerTests
{
    private const int Head = 2;
    private const int Body = 3;
    private const int Hands = 4;

    // Ensemble A (1000) : tête 10, corps 11, mains 12.
    private static readonly OutfitCatalog Catalog = new([TestOutfits.Outfit(1000, (Head, 10), (Body, 11), (Hands, 12))]);

    private static PackPlan Plan(DresserSlot[] slots, bool includeDyed = false, Func<int, int, bool>? stored = null)
    {
        var report = DresserScan.Run(Catalog, slots, stored ?? ((_, _) => false));
        return PackPlanner.Plan(report.Outfits.Single(o => o.Outfit.SetItemId == 1000), includeDyed);
    }

    private static DresserSlot Slot(int index, uint itemId, byte stain = 0) => new(index, itemId, stain, 0);

    [Fact]
    public void CompleteOutfitTakesEveryPieceInColumnOrder()
    {
        var plan = Plan([Slot(0, 12), Slot(1, 10), Slot(2, 11)]);

        Assert.True(plan.IsNew);
        Assert.False(plan.HighQuality);
        Assert.Equal([Head, Body, Hands], plan.Pieces.Select(p => p.Column));
        Assert.Equal([10u, 11u, 12u], plan.Pieces.Select(p => p.DresserItemId));
        Assert.Equal(3, plan.PrismCost);
        Assert.Empty(plan.Skipped);
        Assert.Equal(1000u, plan.DresserSetItemId);
    }

    [Fact]
    public void DyedPieceStaysInItsSlotByDefault()
    {
        var slots = new[] { Slot(0, 10), Slot(1, 11, stain: 5), Slot(2, 12) };

        var plan = Plan(slots);
        Assert.Equal([10u, 12u], plan.Pieces.Select(p => p.ItemId));
        Assert.Equal(new SkippedPiece(Body, 11, SkipReason.Dyed), plan.Skipped.Single());

        var withDyed = Plan(slots, includeDyed: true);
        Assert.Equal(3, withDyed.Pieces.Count);
        Assert.True(withDyed.Pieces.Single(p => p.ItemId == 11).Dyed);
        Assert.Empty(withDyed.Skipped);
    }

    [Fact]
    public void UndyedCopyIsTakenFirst()
    {
        var plan = Plan([Slot(0, 10), Slot(1, 11, stain: 5), Slot(2, 11), Slot(3, 12)]);

        var body = plan.Pieces.Single(p => p.ItemId == 11);
        Assert.False(body.Dyed);
        Assert.False(body.AllowDyed);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void QualitiesAreNeverMixed()
    {
        // Le jeu exige des pièces de même qualité : la qualité qui range le plus de pièces l'emporte.
        var plan = Plan([Slot(0, 1_000_010), Slot(1, 11), Slot(2, 12)]);
        Assert.False(plan.HighQuality);
        Assert.Equal([11u, 12u], plan.Pieces.Select(p => p.ItemId));
        Assert.Equal(new SkippedPiece(Head, 10, SkipReason.OtherQuality), plan.Skipped.Single());

        var highQuality = Plan([Slot(0, 1_000_010), Slot(1, 1_000_011), Slot(2, 12)]);
        Assert.True(highQuality.HighQuality);
        Assert.Equal([1_000_010u, 1_000_011u], highQuality.Pieces.Select(p => p.DresserItemId));
        Assert.Equal(1_001_000u, highQuality.DresserSetItemId);
    }

    [Fact]
    public void EqualCountsKeepNormalQuality()
    {
        var plan = Plan([Slot(0, 1_000_010), Slot(1, 11)]);

        Assert.False(plan.HighQuality);
        Assert.Equal([11u], plan.Pieces.Select(p => p.ItemId));
    }

    [Fact]
    public void StoredOutfitOnlyGetsItsMissingPiecesOfItsQuality()
    {
        // Ensemble HQ rangé en case 0 avec sa tête ; corps HQ et mains normales seules dans la coiffeuse, tête en double.
        var plan = Plan(
            [Slot(0, 1_001_000), Slot(1, 1_000_011), Slot(2, 12), Slot(3, 1_000_010)],
            stored: (index, column) => index == 0 && column == Head);

        Assert.False(plan.IsNew);
        Assert.True(plan.HighQuality);
        Assert.Equal([1_000_011u], plan.Pieces.Select(p => p.DresserItemId));
        Assert.Equal(new SkippedPiece(Hands, 12, SkipReason.OtherQuality), plan.Skipped.Single());
        Assert.Equal(1_001_000u, plan.DresserSetItemId);
    }

    [Fact]
    public void NothingToDoWhenEveryPieceIsDyed()
    {
        var plan = Plan([Slot(0, 10, stain: 1), Slot(1, 11, stain: 2)]);

        Assert.False(plan.HasWork);
        Assert.Equal(0, plan.PrismCost);
        Assert.Equal(2, plan.Skipped.Count);
    }

    [Fact]
    public void StoreArgumentsFollowTheGameLayout()
    {
        var (containers, slots) = StoreArguments.Build([new BagItem(3201, 4, 10, false, 30000), new BagItem(0, 7, 11, false, 30000)]);

        // 9 entrées, les vides en InventoryType.Invalid (9999) et case 0.
        Assert.Equal([3201u, 0u, 9999u, 9999u, 9999u, 9999u, 9999u, 9999u, 9999u], containers);
        Assert.Equal([(ushort)4, (ushort)7, (ushort)0, (ushort)0, (ushort)0, (ushort)0, (ushort)0, (ushort)0, (ushort)0], slots);
    }

    [Fact]
    public void StoreArgumentsHoldOneToNinePieces()
    {
        var piece = new BagItem(0, 0, 10, false, 30000);
        Assert.Throws<ArgumentException>(() => StoreArguments.Build([]));
        Assert.Throws<ArgumentException>(() => StoreArguments.Build(Enumerable.Repeat(piece, 10).ToArray()));
        Assert.Equal(9, StoreArguments.Build(Enumerable.Repeat(piece, 9).ToArray()).Containers.Count(c => c == 0));
    }
}
