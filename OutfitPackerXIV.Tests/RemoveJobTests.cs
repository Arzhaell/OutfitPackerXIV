namespace OutfitPackerXIV.Tests;

public class RemoveJobTests
{
    private const int Head = 2;
    private const int Body = 3;
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    // Ensemble A (1000) : tête 10, corps 11.
    private static readonly OutfitCatalog Catalog = new([TestOutfits.Outfit(1000, (Head, 10), (Body, 11))]);

    private static DresserSlot Slot(int index, uint itemId, byte stain0 = 0) => new(index, itemId, stain0, 0);

    private static FakeGame Game(params DresserSlot[] dresser) => new(Catalog, dresser);

    private static RemovePlan PlanFor(FakeGame game) =>
        RemovePlan.For(DresserScan.Run(Catalog, game.Slots, game.IsOutfitColumnStored).Duplicates.Single());

    private static RemoveResult Run(FakeGame game, RemovePlan plan, Action<RemoveJob>? eachFrame = null)
    {
        var job = new RemoveJob(game, plan);
        var now = TimeSpan.Zero;
        for (var frame = 0; frame < 5000 && job.Result == null; frame++)
        {
            eachFrame?.Invoke(job);
            game.Tick();
            job.Update(now);
            now += Frame;
        }

        Assert.NotNull(job.Result);
        Assert.False(game.OverlappingActions, "Two actions were sent at the same time.");
        return job.Result!;
    }

    [Fact]
    public void RemovesOnlyTheExtraCopies()
    {
        // Trois têtes (une teinte) : la non teinte reste, les deux autres sortent.
        var game = Game(Slot(0, 10), Slot(1, 10, stain0: 4), Slot(2, 10), Slot(3, 11));

        var plan = PlanFor(game);
        var result = Run(game, plan);

        Assert.Equal(2, plan.Copies.Count);
        Assert.Equal(PackProblem.None, result.Problem);
        Assert.Equal(2, result.Removed);
        Assert.Equal(["restore 1", "restore 0"], game.Calls);
        Assert.Single(game.Slots, s => s.ItemId == 10);
        Assert.Contains(game.Slots, s => s.ItemId == 11);
        Assert.Equal(2, game.Bags.Count);
    }

    [Fact]
    public void LoosePieceAlreadyInTheOutfitIsRemoved()
    {
        // Ensemble rangé avec ses deux pièces, et une tête seule en plus.
        var game = Game(Slot(0, 1000), Slot(1, 10));

        var result = Run(game, PlanFor(game));

        Assert.Equal(1, result.Removed);
        Assert.Equal(["restore 1"], game.Calls);
        Assert.Equal(1, game.Slots.Count(s => s.ItemId != 0));
    }

    [Fact]
    public void FindsCopiesAgainWhenTheSlotsShift()
    {
        var game = Game(Slot(0, 10), Slot(1, 10), Slot(2, 10));
        game.CompactOnRemove = true;

        var result = Run(game, PlanFor(game));

        Assert.Equal(2, result.Removed);
        Assert.Single(game.Slots, s => s.ItemId == 10);
    }

    [Fact]
    public void FullInventoryStops()
    {
        var game = Game(Slot(0, 10), Slot(1, 10), Slot(2, 10));
        game.BagCapacity = 1;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.RestoreRefused, result.Problem);
        Assert.Equal(10u, result.ProblemItemId);
        Assert.Equal(1, result.Removed);
        Assert.Equal(2, game.Slots.Count(s => s.ItemId == 10));
    }

    [Fact]
    public void ClosedDresserStopsAtOnce()
    {
        var game = Game(Slot(0, 10), Slot(1, 10), Slot(2, 10));

        var result = Run(game, PlanFor(game), job =>
        {
            if (job.Removed == 1)
                game.DresserOpen = false;
        });

        Assert.Equal(PackProblem.DresserClosed, result.Problem);
        Assert.Equal(1, result.Removed);
        Assert.Single(game.Calls);
    }

    [Fact]
    public void GivesUpWhenTheGameNeverAnswers()
    {
        var game = Game(Slot(0, 10), Slot(1, 10));
        game.ServerSilent = true;

        var result = Run(game, PlanFor(game));

        Assert.Equal(PackProblem.Timeout, result.Problem);
        Assert.Equal(10u, result.ProblemItemId);
        Assert.Equal(0, result.Removed);
    }

    [Fact]
    public void StopEndsAfterTheCurrentCopy()
    {
        var game = Game(Slot(0, 10), Slot(1, 10), Slot(2, 10));

        var result = Run(game, PlanFor(game), job =>
        {
            if (job.Removed == 1)
                job.Stop();
        });

        Assert.Equal(PackProblem.Stopped, result.Problem);
        Assert.Equal(1, result.Removed);
        Assert.Single(game.Calls);
    }

    [Fact]
    public void CopiesGoneFromTheDresserLeaveNothingToRemove()
    {
        var game = Game(Slot(0, 10), Slot(1, 10));
        var plan = PlanFor(game);
        game.RemoveFromDresser(0);

        var result = Run(game, plan);

        // Il ne reste qu'une tête : c'est celle qui doit rester, le plugin ne la sort pas.
        Assert.Equal(PackProblem.NothingToRemove, result.Problem);
        Assert.Empty(game.Calls);
    }
}
