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
    public void SharedPieceKeepsOneCopyPerOutfit()
    {
        // Le corps 11 fait partie de A et de B : deux exemplaires, un pour chacun, aucun en trop.
        var report = Scan(Dresser(11, 11));

        Assert.All(report.Outfits, o => Assert.Equal(0, o.ExtraCopies));
        Assert.Empty(report.Duplicates);
    }

    [Fact]
    public void ThirdCopyOfASharedPieceIsExtra()
    {
        var report = Scan(Dresser(11, 11, 11));

        Assert.Equal(1, Status(report, 1000).Pieces.Single(p => p.ItemId == 11).ExtraCopies);
        Assert.Equal(1, Status(report, 2000).Pieces.Single(p => p.ItemId == 11).ExtraCopies);
    }

    [Fact]
    public void SharedPieceInOneStoredOutfitIsStillNeededByTheOther()
    {
        // A est rangé avec son corps ; le corps seul reste nécessaire à B, qui n'est pas rangé.
        var report = Scan(Dresser(1000, 11), (index, column) => index == 0 && column == Body);

        // A n'a rien à ajouter et rien en trop : il n'est pas listé. B peut prendre le corps.
        Assert.DoesNotContain(report.Outfits, o => o.Outfit.SetItemId == 1000);
        Assert.Empty(report.Duplicates);
        Assert.True(Status(report, 2000).Pieces.Single(p => p.ItemId == 11).CanJoin);
    }

    [Fact]
    public void UndyedCopyIsKeptAndTheDyedOneIsExtra()
    {
        DresserSlot[] slots = [new(0, 10, 3, 0), new(1, 10, 0, 0)];

        var head = Status(Scan(slots), 1000).Pieces.Single(p => p.ItemId == 10);

        Assert.Equal(0, head.Extra.Single().Index);
        Assert.True(head.Extra.Single().Dyed);
    }

    [Fact]
    public void StoredHighQualityOutfitKeepsTheHighQualityCopy()
    {
        // Ensemble A rangé en HQ sans sa tête : seule une tête HQ pourra le rejoindre, la tête normale est en trop.
        var report = Scan(Dresser(1_001_000, 10, 1_000_010), (index, column) => index == 0 && column != Head);

        var head = Status(report, 1000).Pieces.Single(p => p.ItemId == 10);
        Assert.Equal(1, head.Extra.Single().Index);
        Assert.False(head.Extra.Single().HighQuality);
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

    private static BagItem Bag(int slot, uint itemId, uint container = 0) => new(container, slot, itemId, false, BagItem.FullCondition);

    [Fact]
    public void OutfitWithPiecesOnlyInTheBagsIsListed()
    {
        var report = DresserScan.Run(Catalog, Dresser(99), (_, _) => false, [Bag(0, 30), Bag(1, 31, container: 3202)]);

        var outfit = Assert.Single(report.WithBagPieces);
        Assert.Equal(3000u, outfit.Outfit.SetItemId);
        Assert.All(outfit.Pieces, p => Assert.True(p.CanJoinFromBags));
        Assert.True(outfit.Pieces[1].InBags.Single().Item.InArmory);

        // Les onglets de la coiffeuse ne changent pas : rien n'y est.
        Assert.Empty(report.Complete);
        Assert.Empty(report.Partial(withSinglePieces: true));
    }

    [Fact]
    public void BagCopiesTheGameWouldDamageCarryTheirReason()
    {
        var bags = new[]
        {
            Bag(0, 10) with { HasMateria = true },
            Bag(1, 11) with { Glamoured = true },
            Bag(2, 12) with { Condition = 29_999 },
            Bag(3, 30),
            Bag(4, 31) with { Signed = true, Stain0 = 3 },
        };

        var report = DresserScan.Run(Catalog, Dresser(99), (_, _) => false, bags, new HashSet<uint> { 30 });

        SkipReason? Block(uint itemId) => report.Outfits.SelectMany(o => o.Pieces).First(p => p.ItemId == itemId).InBags.Single().Block;
        Assert.Equal(SkipReason.HasMateria, Block(10));
        Assert.Equal(SkipReason.Glamoured, Block(11));
        Assert.Equal(SkipReason.NotRepaired, Block(12));
        Assert.Equal(SkipReason.InGearset, Block(30));

        // Signée et teinte : le plugin peut la ranger (il prévient, et demande pour la teinture).
        Assert.Null(Block(31));
    }

    [Fact]
    public void GearsetBlocksBothQualities()
    {
        var hq = new BagItem(0, 0, 30, true, BagItem.FullCondition);

        var report = DresserScan.Run(Catalog, Dresser(99), (_, _) => false, [hq], new HashSet<uint> { 30 });

        Assert.Equal(SkipReason.InGearset, report.Outfits.Single().Pieces.First(p => p.ItemId == 30).InBags.Single().Block);
    }

    [Fact]
    public void BagCopyOfAPieceAlreadyInTheStoredOutfitIsNotListed()
    {
        // Ensemble C rangé en case 0 avec ses deux pièces (aucun bit « manquante »).
        var slots = new[] { new DresserSlot(0, 3000, 0, 0) };

        var report = DresserScan.Run(Catalog, slots, (_, _) => true, [Bag(0, 30)]);

        Assert.Empty(report.WithBagPieces);
        Assert.DoesNotContain(report.Outfits, o => o.Outfit.SetItemId == 3000);
    }

    [Fact]
    public void ReportKnowsTheFreeDresserSlots()
    {
        var report = Scan([new DresserSlot(0, 10, 0, 0), new DresserSlot(1, 0, 0, 0), new DresserSlot(2, 0, 0, 0)]);

        Assert.Equal(3, report.TotalSlots);
        Assert.Equal(2, report.FreeSlots);
    }
}
