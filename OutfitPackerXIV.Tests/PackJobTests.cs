namespace OutfitPackerXIV.Tests;

public class PackJobTests
{
    private const int Head = 2;
    private const int Body = 3;
    private const int Hands = 4;
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    // Ensemble A (1000) : tête 10, corps 11, mains 12. Objet 99 : sans ensemble.
    private static readonly OutfitCatalog Catalog = new([TestOutfits.Outfit(1000, (Head, 10), (Body, 11), (Hands, 12))]);

    private static DresserSlot Slot(int index, uint itemId, byte stain0 = 0, byte stain1 = 0) => new(index, itemId, stain0, stain1);

    private static FakeGame Game(params DresserSlot[] dresser) => new(Catalog, dresser);

    private static PackPlan PlanFor(FakeGame game, bool includeDyed = false)
    {
        var report = DresserScan.Run(Catalog, game.Slots, game.IsOutfitColumnStored);
        return PackPlanner.Plan(report.Outfits.Single(o => o.Outfit.SetItemId == 1000), includeDyed);
    }

    /// <summary>Fait tourner le regroupement image par image, comme Framework.Update.</summary>
    private static PackResult Run(FakeGame game, PackPlan plan, Action<PackJob, TimeSpan>? eachFrame = null)
    {
        var job = new PackJob(game, plan);
        var now = TimeSpan.Zero;
        for (var frame = 0; frame < 5000 && job.Result == null; frame++)
        {
            eachFrame?.Invoke(job, now);
            game.Tick();
            job.Update(now);
            now += Frame;
        }

        Assert.NotNull(job.Result);
        Assert.False(game.OverlappingActions, "Two actions were sent at the same time.");
        return job.Result!;
    }

    [Fact]
    public void PacksACompleteOutfit()
    {
        var game = Game(Slot(0, 12), Slot(1, 99), Slot(2, 10), Slot(3, 11));

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(3, result.StoredPieces);
        Assert.Empty(result.LeftInInventory);
        Assert.Equal(["restore 2", "restore 3", "restore 0", "new 1000"], game.Calls);
        Assert.Empty(game.Bags);
        Assert.Equal(96, game.Prisms);

        // L'ensemble est rangé avec ses trois pièces, l'objet sans ensemble n'a pas bougé.
        var outfit = game.Slots.Single(s => s.ItemId == 1000);
        Assert.True(game.IsOutfitColumnStored(outfit.Index, Head));
        Assert.True(game.IsOutfitColumnStored(outfit.Index, Body));
        Assert.True(game.IsOutfitColumnStored(outfit.Index, Hands));
        Assert.Equal(2, game.Slots.Count(s => s.ItemId != 0));
    }

    [Fact]
    public void WaitsForTheGameAndPausesBetweenActions()
    {
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));
        game.ServerDelay = 40;
        var calls = new List<TimeSpan>();

        Run(game, PlanFor(game), (_, now) =>
        {
            if (game.Calls.Count > calls.Count)
                calls.Add(now);
        });

        // Chaque action attend la réponse du serveur (40 images) puis la pause d'une demi-seconde.
        Assert.Equal(4, calls.Count);
        for (var i = 1; i < calls.Count; i++)
            Assert.True(calls[i] - calls[i - 1] >= Frame * 40 + PackJob.PauseBetweenActions, $"Action {i} came too early.");
    }

    [Fact]
    public void FindsPiecesAgainWhenTheSlotsShift()
    {
        var game = Game(Slot(0, 10), Slot(1, 99), Slot(2, 11), Slot(3, 12));
        game.CompactOnRemove = true;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(3, result.StoredPieces);
        Assert.Equal(["restore 0", "restore 1", "restore 1", "new 1000"], game.Calls);
    }

    [Fact]
    public void PiecesInTheArmouryChestWorkToo()
    {
        var game = Game(Slot(0, 10), Slot(1, 11));
        game.RestoreContainer = 3201;

        var result = Run(game, PlanFor(game));

        Assert.Equal(2, result.StoredPieces);
        Assert.Equal(3201u, game.LastStore!.Value.Containers[0]);
    }

    [Fact]
    public void AddsPiecesToTheStoredOutfit()
    {
        // Ensemble rangé en case 0 avec sa tête seulement : corps et mains manquent (bits 3 et 4).
        var game = Game(Slot(0, 1000, 0b0001_1000), Slot(1, 12), Slot(2, 11));

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(2, result.StoredPieces);
        Assert.Equal(["restore 2", "restore 1", "existing 0"], game.Calls);
        Assert.Equal(Slot(0, 1000), game.Slots[0]);
        Assert.Equal(1, game.Slots.Count(s => s.ItemId != 0));
    }

    [Fact]
    public void PiecesArePassedInColumnOrder()
    {
        var game = Game(Slot(0, 12), Slot(1, 11), Slot(2, 10));
        game.CompactOnRemove = true;

        Run(game, PlanFor(game));

        // Sorties dans l'ordre tête, corps, mains : cases 0, 1, 2 des sacs.
        Assert.Equal([0, 1, 2], game.LastStore!.Value.Slots.Take(3).Select(s => (int)s));
    }

    [Fact]
    public void FullInventoryStoresThePiecesAlreadyOut()
    {
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));
        game.BagCapacity = 2;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.InventoryFull, result.Problem);
        Assert.Equal(12u, result.ProblemItemId);
        Assert.Equal(2, result.StoredPieces);
        Assert.Empty(result.LeftInInventory);
        Assert.Equal(["restore 0", "restore 1", "restore 2", "restore 2", "restore 2", "new 1000"], game.Calls);
        Assert.Contains(game.Slots, s => s.ItemId == 12);
    }

    [Fact]
    public void RefusalJustAfterAnActionIsRetried()
    {
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));
        game.RefuseNextRestores = PackJob.RestoreAttempts - 1;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(3, result.StoredPieces);
        Assert.Equal(["restore 0", "restore 0", "restore 0", "restore 1", "restore 2", "new 1000"], game.Calls);
    }

    [Fact]
    public void EachPieceGetsItsOwnAttempts()
    {
        // Chaque pièce est refusée deux fois, puis acceptée : aucune n'épuise ses essais.
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));
        game.RefuseEachSlot = PackJob.RestoreAttempts - 1;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(3, result.StoredPieces);
        Assert.Equal(3 * PackJob.RestoreAttempts + 1, game.Calls.Count);
    }

    [Fact]
    public void RefusalWithRoomLeftIsNotAFullInventory()
    {
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));
        game.RefuseAllRestores = true;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.RestoreRefused, result.Problem);
        Assert.Equal(10u, result.ProblemItemId);
        Assert.Equal(0, result.StoredPieces);
        Assert.Equal(["restore 0", "restore 0", "restore 0"], game.Calls);
    }

    [Fact]
    public void ClosedDresserStopsAtOnce()
    {
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));

        var result = Run(game, PlanFor(game), (job, _) =>
        {
            if (job.TakenOut == 1)
                game.DresserOpen = false;
        });

        Assert.Equal(PackProblem.DresserClosed, result.Problem);
        Assert.Equal(0, result.StoredPieces);
        Assert.Equal([10u], result.LeftInInventory);
        Assert.DoesNotContain(game.Calls, c => c.StartsWith("new"));
    }

    [Fact]
    public void WaitsWhileTheDresserContentsReload()
    {
        var game = Game(Slot(0, 10), Slot(1, 11));
        var frames = 0;

        var result = Run(game, PlanFor(game), (job, _) =>
        {
            // Une seconde sans contenu chargé juste après la première pièce, fenêtre toujours ouverte.
            if (job.TakenOut == 1 && frames++ < 60)
                game.Loaded = false;
            else
                game.Loaded = true;
        });

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(2, result.StoredPieces);
    }

    [Fact]
    public void ContentsThatNeverReloadCountAsAClosedDresser()
    {
        var game = Game(Slot(0, 10), Slot(1, 11));

        var result = Run(game, PlanFor(game), (job, _) =>
        {
            if (job.TakenOut == 1)
                game.Loaded = false;
        });

        Assert.Equal(PackProblem.DresserClosed, result.Problem);
        Assert.Equal([10u], result.LeftInInventory);
    }

    [Fact]
    public void GivesUpWhenTheGameNeverAnswers()
    {
        var game = Game(Slot(0, 10), Slot(1, 11));
        game.ServerSilent = true;
        var end = TimeSpan.Zero;

        var result = Run(game, PlanFor(game), (_, now) => end = now);

        Assert.Equal(PackProblem.Timeout, result.Problem);
        Assert.Equal(10u, result.ProblemItemId);
        Assert.Equal(["restore 0"], game.Calls);
        Assert.InRange(end, PackJob.StepTimeout, PackJob.StepTimeout + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void StopStoresThePiecesAlreadyOut()
    {
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));

        var result = Run(game, PlanFor(game), (job, _) =>
        {
            if (job.TakenOut == 1)
                job.Stop();
        });

        Assert.Equal(PackProblem.Stopped, result.Problem);
        Assert.Equal(1, result.StoredPieces);
        Assert.Equal(["restore 0", "new 1000"], game.Calls);
        Assert.Contains(game.Slots, s => s.ItemId == 11);
    }

    [Fact]
    public void NotEnoughPrismsChangesNothing()
    {
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));
        game.Prisms = 2;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.NotEnoughPrisms, result.Problem);
        Assert.Empty(game.Calls);
    }

    [Fact]
    public void ItemsAlreadyInTheInventoryAreNeverUsed()
    {
        // Une tête à soi, déjà dans les sacs (case 0) : le plugin ne range que celle qu'il sort de la coiffeuse.
        var game = Game(Slot(0, 10), Slot(1, 11));
        var own = new BagItem(FakeGame.Inventory1, 0, 10, false, BagItem.FullCondition);
        game.Bags.Add(own);

        var result = Run(game, PlanFor(game));

        Assert.Equal(2, result.StoredPieces);
        Assert.Equal([own], game.Bags);
        Assert.Equal([1, 2], game.LastStore!.Value.Slots.Take(2).Select(s => (int)s));
    }

    [Fact]
    public void PieceGoneFromTheDresserIsSkipped()
    {
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 12));
        var plan = PlanFor(game);
        game.RemoveFromDresser(1);

        var result = Run(game, plan);

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(2, result.StoredPieces);
        Assert.Equal([11u], result.NoLongerInDresser);
    }

    [Fact]
    public void WornPiecesAreLeftInTheInventory()
    {
        var game = Game(Slot(0, 10), Slot(1, 11));
        game.RestoredCondition = 20_000;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.NotFullCondition, result.Problem);
        Assert.Equal(0, result.StoredPieces);
        Assert.Equal([10u, 11u], result.LeftInInventory);
        Assert.DoesNotContain(game.Calls, c => c.StartsWith("new"));
    }

    [Fact]
    public void OutfitAlreadyStoredIsNotStoredAgain()
    {
        // Calculé avant qu'un ensemble du même type (ici HQ) n'arrive dans la coiffeuse.
        var plan = PlanFor(Game(Slot(0, 10), Slot(1, 11)));
        var game = Game(Slot(0, 10), Slot(1, 11), Slot(2, 1_001_000));

        var result = Run(game, plan);

        Assert.Equal(PackProblem.AlreadyStored, result.Problem);
        Assert.Empty(game.Calls);
    }

    [Fact]
    public void DyedCopyIsOnlyTakenWhenAllowed()
    {
        var game = Game(Slot(0, 11, stain0: 4), Slot(1, 10), Slot(2, 11));

        var result = Run(game, PlanFor(game));

        Assert.Equal(2, result.StoredPieces);
        Assert.Equal(["restore 1", "restore 2", "new 1000"], game.Calls);
        Assert.Equal(Slot(0, 11, stain0: 4), game.Slots[0]);
    }

    [Fact]
    public void AnsweringYesTakesTheDyedCopyOnlyWhenNoOtherIsLeft()
    {
        // Tête : seulement teinte. Corps : une teinte et une non teinte.
        var game = Game(Slot(0, 10, stain0: 7), Slot(1, 11, stain1: 2), Slot(2, 11));

        var result = Run(game, PlanFor(game, includeDyed: true));

        Assert.Equal(2, result.StoredPieces);
        Assert.Equal(["restore 0", "restore 2", "new 1000"], game.Calls);
        Assert.Equal(Slot(1, 11, stain1: 2), game.Slots[1]);
    }

    [Fact]
    public void HighQualityPiecesMakeAHighQualityOutfit()
    {
        var game = Game(Slot(0, 1_000_010), Slot(1, 1_000_011), Slot(2, 12));

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(2, result.StoredPieces);
        Assert.Contains("new 1001000", game.Calls);
        Assert.Contains(game.Slots, s => s.ItemId == 12);
    }

    private static BagItem BagPiece(int slot, uint itemId, uint container = FakeGame.Inventory1) =>
        new(container, slot, itemId, false, BagItem.FullCondition);

    private static PackPlan PlanWithBags(FakeGame game, bool includeDyed = false)
    {
        var report = DresserScan.Run(Catalog, game.Slots, game.IsOutfitColumnStored, game.ReadBags());
        return PackPlanner.Plan(report.Outfits.Single(o => o.Outfit.SetItemId == 1000), includeDyed, useBags: true);
    }

    [Fact]
    public void StoresBagPiecesWithoutTakingAnythingOut()
    {
        var game = Game(Slot(0, 99));
        game.Bags.AddRange([BagPiece(5, 10), BagPiece(0, 11, container: 3202), BagPiece(7, 12)]);

        var result = Run(game, PlanWithBags(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(3, result.StoredPieces);
        Assert.Equal(3, result.StoredFromBags);
        Assert.Equal(["new 1000"], game.Calls);
        Assert.Equal([0u, 3202u, 0u], game.LastStore!.Value.Containers.Take(3));
        Assert.Equal([5, 0, 7], game.LastStore.Value.Slots.Take(3).Select(s => (int)s));
        Assert.Empty(game.Bags);
        Assert.Equal(96, game.Prisms);
    }

    [Fact]
    public void TakesDresserPiecesOutThenStoresThemWithTheBagPieces()
    {
        var game = Game(Slot(0, 11));
        game.Bags.AddRange([BagPiece(3, 10), BagPiece(4, 12)]);

        var result = Run(game, PlanWithBags(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(3, result.StoredPieces);
        Assert.Equal(2, result.StoredFromBags);
        Assert.Equal(["restore 0", "new 1000"], game.Calls);
        Assert.Empty(game.Bags);
    }

    [Fact]
    public void BagPieceJoinsTheStoredOutfit()
    {
        // Ensemble rangé en case 0 sans ses mains (bit 4).
        var game = Game(Slot(0, 1000, 0b0001_0000));
        game.Bags.Add(BagPiece(2, 12));

        var result = Run(game, PlanWithBags(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(["existing 0"], game.Calls);
        Assert.Equal(Slot(0, 1000), game.Slots[0]);
        Assert.Empty(game.Bags);
    }

    [Fact]
    public void BagPieceMovedInTheMeantimeIsNotStored()
    {
        var game = Game(Slot(0, 10));
        game.Bags.AddRange([BagPiece(3, 11), BagPiece(4, 12)]);
        var plan = PlanWithBags(game);
        game.Bags.RemoveAll(b => b.ItemId == 12);
        game.Bags.Add(BagPiece(9, 12));

        var result = Run(game, plan);

        Assert.Equal(PackProblem.PieceMoved, result.Problem);
        Assert.Equal(12u, result.ProblemItemId);
        Assert.Equal(2, result.StoredPieces);
        Assert.Contains(game.Bags, b => b is { ItemId: 12, Slot: 9 });
    }

    [Fact]
    public void BagPieceSwappedForAMeldedCopyIsNotStored()
    {
        var game = Game(Slot(0, 99));
        game.Bags.AddRange([BagPiece(3, 10), BagPiece(4, 11)]);
        var plan = PlanWithBags(game);

        // Le même objet, à la même place, mais serti de matérias : le jeu les détruirait.
        game.Bags[1] = BagPiece(4, 11) with { HasMateria = true };

        var result = Run(game, plan);

        Assert.Equal(PackProblem.PieceMoved, result.Problem);
        Assert.Equal(1, result.StoredPieces);
        Assert.Contains(game.Bags, b => b is { ItemId: 11, HasMateria: true });
    }

    [Fact]
    public void NewOutfitFromTheBagsNeedsAFreeDresserSlot()
    {
        var game = new FakeGame(Catalog, [Slot(0, 99), Slot(1, 98)], size: 2);
        game.Bags.AddRange([BagPiece(0, 10), BagPiece(1, 11)]);

        var result = Run(game, PlanWithBags(game));

        Assert.Equal(PackProblem.DresserFull, result.Problem);
        Assert.Empty(game.Calls);
        Assert.Equal(2, game.Bags.Count);
    }

    [Fact]
    public void FullDresserIsFineWhenAPieceComesOutOfIt()
    {
        var game = new FakeGame(Catalog, [Slot(0, 10), Slot(1, 98)], size: 2);
        game.Bags.Add(BagPiece(0, 11));

        var result = Run(game, PlanWithBags(game));

        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(["restore 0", "new 1000"], game.Calls);
    }

    [Fact]
    public void StopStillStoresTheBagPieces()
    {
        var game = Game(Slot(0, 10), Slot(1, 11));
        game.Bags.Add(BagPiece(0, 12));

        var result = Run(game, PlanWithBags(game), (job, _) =>
        {
            if (job.TakenOut == 1)
                job.Stop();
        });

        // Une pièce sortie, la seconde reste ; la pièce des sacs est rangée avec la première.
        Assert.Equal(PackProblem.Stopped, result.Problem);
        Assert.Equal(2, result.StoredPieces);
        Assert.Equal(1, result.StoredFromBags);
        Assert.Contains(game.Slots, s => s.ItemId == 11);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void DyedBagPieceIsStoredOnlyWhenAllowed(bool includeDyed, int stored)
    {
        var game = Game(Slot(0, 99));
        game.Bags.AddRange([BagPiece(0, 10), BagPiece(1, 11) with { Stain0 = 2 }]);

        var result = Run(game, PlanWithBags(game, includeDyed));

        Assert.Equal(stored, result.StoredPieces);
        Assert.Equal(!includeDyed, game.Bags.Any(b => b.ItemId == 11));
    }
}
