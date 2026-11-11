namespace OutfitPackerXIV.Tests;

public class DresserScanTests
{
    // Colonnes de MirageStoreSetItem.
    private const int Head = 2;
    private const int Body = 3;
    private const int Hands = 4;
    private const int Legs = 5;
    private const int Feet = 6;

    // Ensemble A (1000) : tête 10, corps 11, mains 12. Ensemble B (2000) : corps 11 (partagé avec A), jambes 21, pieds 22.
    // Ensemble C (3000) : tête 30, corps 31.
    private static readonly OutfitCatalog Catalog = new(
    [
        Outfit(1000, (Head, 10), (Body, 11), (Hands, 12)),
        Outfit(2000, (Body, 11), (Legs, 21), (Feet, 22)),
        Outfit(3000, (Head, 30), (Body, 31)),
    ]);

    private static OutfitDefinition Outfit(uint setItemId, params (int Column, uint ItemId)[] pieces)
    {
        var columns = new uint[OutfitCatalog.ColumnCount];
        foreach (var (column, itemId) in pieces)
            columns[column] = itemId;
        return new OutfitDefinition(setItemId, columns);
    }

    private static DresserSlot[] Dresser(params uint[] itemIds) =>
        itemIds.Select((id, index) => new DresserSlot(index, id, 0, 0)).ToArray();

    private static DresserReport Scan(DresserSlot[] slots, Func<int, int, bool>? stored = null) =>
        DresserScan.Run(Catalog, slots, stored ?? ((_, _) => false));

    private static OutfitStatus Status(DresserReport report, uint setItemId) =>
        report.Outfits.Single(o => o.Outfit.SetItemId == setItemId);

    [Theory]
    [InlineData(1234u, 1234u)]
    [InlineData(1_001_234u, 1234u)]
    [InlineData(501_234u, 1234u)]
    [InlineData(2_000_123u, 2_000_123u)]
    public void ItemIdsDropTheQualityOffset(uint raw, uint expected) => Assert.Equal(expected, ItemIds.Base(raw));

    [Fact]
    public void CompleteOutfitFreesAllButOneSlot()
    {
        var report = Scan(Dresser(10, 0, 11, 12, 99));

        Assert.Equal(4, report.UsedSlots);
        var a = Status(report, 1000);
        Assert.False(a.IsStored);
        Assert.True(a.IsComplete);
        Assert.Equal(3, a.JoinablePieces);
        Assert.Equal(2, a.FreedSlots);
        Assert.Contains(a, report.Complete);
        Assert.Equal([0, 2, 3], a.Pieces.Select(p => p.Loose.Single().Index));
    }

    [Fact]
    public void IncompleteOutfitListsItsMissingPieces()
    {
        var report = Scan(Dresser(21, 22));

        var b = Status(report, 2000);
        Assert.False(b.IsComplete);
        Assert.Equal(1, b.MissingPieces);
        Assert.Equal(1, b.FreedSlots);
        Assert.Equal(11u, b.Pieces.Single(p => p.Missing).ItemId);
        Assert.Contains(b, report.Partial(withSinglePieces: false));
        Assert.Empty(report.Complete);
    }

    [Fact]
    public void SinglePieceIsOnlyShownOnRequest()
    {
        var report = Scan(Dresser(30));

        var c = Status(report, 3000);
        Assert.Equal(0, c.FreedSlots);
        Assert.DoesNotContain(c, report.Partial(withSinglePieces: false));
        Assert.Contains(c, report.Partial(withSinglePieces: true));
        Assert.Equal(0, report.MaxFreedSlots(withSinglePieces: true));
    }

    [Fact]
    public void LoosePieceCanJoinItsStoredOutfit()
    {
        // L'ensemble A est rangé en case 0 avec sa tête ; le corps et les mains sont seuls dans la coiffeuse.
        var report = Scan(Dresser(1000, 11, 12), (index, column) => index == 0 && column == Head);

        var a = Status(report, 1000);
        Assert.True(a.IsStored);
        Assert.Equal(0, a.OutfitIndex);
        Assert.True(a.Pieces.Single(p => p.Column == Head).InOutfit);
        Assert.Equal(2, a.JoinablePieces);
        Assert.Equal(2, a.FreedSlots);
        Assert.True(a.IsComplete);
        Assert.Contains(a, report.AddToStored);
        Assert.DoesNotContain(a, report.Complete);
    }

    [Fact]
    public void StoredOutfitAfterOneOfItsPiecesIsListedOnce()
    {
        // Le corps seul est rangé avant l'ensemble A dans la coiffeuse (seule la tête est dans l'ensemble).
        var report = Scan(Dresser(11, 1000, 12), (index, column) => index == 1 && column == Head);

        var a = Status(report, 1000);
        Assert.True(a.IsStored);
        Assert.Equal(1, a.OutfitIndex);
        Assert.Equal(2, a.JoinablePieces);
        Assert.Single(report.AddToStored, o => o.Outfit.SetItemId == 1000);
    }

    [Fact]
    public void PieceAlreadyInItsOutfitIsADuplicate()
    {
        var report = Scan(Dresser(1000, 10), (_, column) => column == Head);

        var a = Status(report, 1000);
        Assert.Equal(0, a.JoinablePieces);
        Assert.Equal(1, a.ExtraCopies);
        Assert.Contains(a, report.Duplicates);
        Assert.DoesNotContain(a, report.AddToStored);
    }

    [Fact]
    public void SecondCopyOfAPieceIsADuplicate()
    {
        var report = Scan(Dresser(10, 10, 11, 12));

        var a = Status(report, 1000);
        Assert.Equal(2, a.FreedSlots);
        Assert.Equal(1, a.ExtraCopies);
        Assert.Contains(a, report.Duplicates);
    }

    [Fact]
    public void StoredOutfitWithNothingToAddIsLeftOut()
    {
        var report = Scan(Dresser(1000), (_, _) => true);

        Assert.Empty(report.Outfits);
        Assert.Equal(1, report.UsedSlots);
    }

    [Fact]
    public void SharedPieceIsListedInBothOutfits()
    {
        var report = Scan(Dresser(11, 21, 22, 10, 12));

        var a = Status(report, 1000);
        var b = Status(report, 2000);
        Assert.True(a.IsComplete);
        Assert.True(b.IsComplete);
        Assert.Equal([2000u], a.Pieces.Single(p => p.ItemId == 11).SharedWith);
        Assert.Equal([1000u], b.Pieces.Single(p => p.ItemId == 11).SharedWith);
        Assert.Empty(a.Pieces.Single(p => p.ItemId == 10).SharedWith);

        // Le corps 11 compte dans les deux : c'est bien un maximum.
        Assert.Equal(4, report.MaxFreedSlots(withSinglePieces: false));
    }

    [Fact]
    public void HighQualityPiecesCount()
    {
        var report = Scan(Dresser(1_000_010, 11, 1_000_012));

        Assert.True(Status(report, 1000).IsComplete);
    }

    [Fact]
    public void DyedPiecesAreFlaggedButNotTheOutfitSlots()
    {
        // Une case « ensemble » a des valeurs dans ses teintures (le jeu y note ses pièces) : ce n'est pas une teinture.
        DresserSlot[] slots =
        [
            new(0, 1000, 0b101, 0),
            new(1, 11, 0, 7),
        ];
        var report = Scan(slots, (_, column) => column == Head);

        var a = Status(report, 1000);
        Assert.True(a.HasDyedPieces);
        Assert.True(a.Pieces.Single(p => p.ItemId == 11).Loose.Single().Dyed);
        Assert.False(Status(Scan(Dresser(10, 11)), 1000).HasDyedPieces);
    }

    [Fact]
    public void BiggestGainsComeFirst()
    {
        var report = Scan(Dresser(30, 31, 10, 11, 12));

        Assert.Equal(1000u, report.Outfits[0].Outfit.SetItemId);
        Assert.Equal(3000u, report.Outfits[1].Outfit.SetItemId);
    }

    [Fact]
    public void OutfitNeedsElevenColumns() =>
        Assert.Throws<ArgumentException>(() => new OutfitDefinition(1, [1, 2, 3]));
}
