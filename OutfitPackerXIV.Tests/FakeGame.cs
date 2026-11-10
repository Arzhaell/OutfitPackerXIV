namespace OutfitPackerXIV.Tests;

internal static class TestOutfits
{
    public static OutfitDefinition Outfit(uint setItemId, params (int Column, uint ItemId)[] pieces)
    {
        var columns = new uint[OutfitCatalog.ColumnCount];
        foreach (var (column, itemId) in pieces)
            columns[column] = itemId;
        return new OutfitDefinition(setItemId, columns);
    }
}

/// <summary>
/// Un faux jeu pour tester le regroupement : il fait les mêmes vérifications que les fonctions du vrai jeu (lues dans
/// son code) et n'applique chaque action qu'après quelques images, comme le serveur.
/// </summary>
internal sealed class FakeGame : IDresserGame
{
    public const uint Inventory1 = 0;

    private readonly OutfitCatalog catalog;
    private readonly List<(int Due, Action Apply)> serverQueue = [];
    private DresserSlot[] slots;
    private int tick;

    public FakeGame(OutfitCatalog catalog, IEnumerable<DresserSlot> dresser, int size = 20)
    {
        this.catalog = catalog;
        slots = Enumerable.Range(0, size).Select(i => new DresserSlot(i, 0, 0, 0)).ToArray();
        foreach (var slot in dresser)
            slots[slot.Index] = slot;
    }

    public List<BagItem> Bags { get; } = [];

    public bool DresserOpen { get; set; } = true;

    /// <summary>Le contenu de la coiffeuse est chargé (le jeu l'oublie au changement de zone).</summary>
    public bool Loaded { get; set; } = true;

    public int Prisms { get; set; } = 99;

    /// <summary>Images avant que le serveur applique une action.</summary>
    public int ServerDelay { get; set; } = 3;

    /// <summary>Le serveur ne répond jamais.</summary>
    public bool ServerSilent { get; set; }

    /// <summary>Quand une case se vide, les suivantes se décalent (on ne sait pas si le jeu le fait).</summary>
    public bool CompactOnRemove { get; set; }

    public int BagCapacity { get; set; } = 140;

    /// <summary>Le conteneur où arrivent les pièces sorties (sacs ou arsenal).</summary>
    public uint RestoreContainer { get; set; } = Inventory1;

    public ushort RestoredCondition { get; set; } = BagItem.FullCondition;

    /// <summary>Les actions demandées, dans l'ordre (« restore 3 », « new 1000 », « existing 0 »).</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Les tableaux du dernier rangement.</summary>
    public (uint[] Containers, ushort[] Slots)? LastStore { get; private set; }

    /// <summary>Une action a été demandée alors que la précédente n'était pas encore appliquée.</summary>
    public bool OverlappingActions { get; private set; }

    public IReadOnlyList<DresserSlot> Slots => slots;

    public bool IsDresserOpen => DresserOpen;

    public int GlamourPrisms => Prisms;

    public IReadOnlyList<DresserSlot>? ReadSlots() => Loaded ? slots.ToArray() : null;

    public IReadOnlyList<BagItem> ReadBags() => Bags.ToArray();

    public bool IsOutfitColumnStored(int index, int column)
    {
        var slot = slots[index];
        return catalog.TryGetOutfit(ItemIds.Base(slot.ItemId), out var outfit)
            && outfit.Columns[column] != 0
            && (Mask(slot) & (1 << column)) == 0;
    }

    /// <summary>À appeler à chaque image : le serveur applique les actions arrivées à échéance.</summary>
    public void Tick()
    {
        tick++;
        foreach (var action in serverQueue.Where(a => a.Due <= tick).ToArray())
        {
            serverQueue.Remove(action);
            action.Apply();
        }
    }

    public void RemoveFromDresser(int index) => Empty(index);

    public bool Restore(int index)
    {
        Calls.Add($"restore {index}");
        var item = slots[index];
        if (item.ItemId == 0 || Bags.Count(b => b.Container == RestoreContainer) >= BagCapacity)
            return false;

        Send(() =>
        {
            var free = Enumerable.Range(0, BagCapacity).First(i => !Bags.Any(b => b.Container == RestoreContainer && b.Slot == i));
            Bags.Add(new BagItem(RestoreContainer, free, ItemIds.Base(item.ItemId), ItemIds.IsHighQuality(item.ItemId), RestoredCondition));
            Empty(slots[index] == item ? index : slots.First(s => s with { Index = 0 } == item with { Index = 0 }).Index);
        });
        return true;
    }

    public bool StoreNew(uint dresserSetItemId, uint[] containers, ushort[] inventorySlots)
    {
        Calls.Add($"new {dresserSetItemId}");
        LastStore = (containers, inventorySlots);
        if (!catalog.TryGetOutfit(ItemIds.Base(dresserSetItemId), out var outfit)
            || ReadPieces(outfit, ItemIds.IsHighQuality(dresserSetItemId), containers, inventorySlots) is not { } pieces
            || Prisms < pieces.Count
            || slots.All(s => s.ItemId != 0))
        {
            return false;
        }

        Send(() =>
        {
            var missing = Enumerable.Range(0, OutfitCatalog.ColumnCount)
                .Where(c => outfit.Columns[c] != 0 && pieces.All(p => p.Column != c))
                .Aggregate(0, (mask, c) => mask | (1 << c));
            var free = slots.First(s => s.ItemId == 0).Index;
            slots[free] = new DresserSlot(free, dresserSetItemId, (byte)(missing & 0xFF), (byte)(missing >> 8));
            Consume(pieces);
        });
        return true;
    }

    public bool StoreExisting(int index, uint[] containers, ushort[] inventorySlots)
    {
        Calls.Add($"existing {index}");
        LastStore = (containers, inventorySlots);
        var slot = slots[index];
        if (!catalog.TryGetOutfit(ItemIds.Base(slot.ItemId), out var outfit)
            || ReadPieces(outfit, ItemIds.IsHighQuality(slot.ItemId), containers, inventorySlots) is not { } pieces
            || pieces.Any(p => (Mask(slot) & (1 << p.Column)) == 0)
            || Prisms < pieces.Count)
        {
            return false;
        }

        Send(() =>
        {
            var current = slots.Single(s => s.ItemId == slot.ItemId);
            var missing = pieces.Aggregate(Mask(current), (mask, p) => mask & ~(1 << p.Column));
            slots[current.Index] = current with { Stain0 = (byte)(missing & 0xFF), Stain1 = (byte)(missing >> 8) };
            Consume(pieces);
        });
        return true;
    }

    private static int Mask(DresserSlot slot) => slot.Stain0 | (slot.Stain1 << 8);

    private void Send(Action apply)
    {
        if (serverQueue.Count > 0)
            OverlappingActions = true;
        if (!ServerSilent)
            serverQueue.Add((tick + ServerDelay, apply));
    }

    private void Empty(int index)
    {
        if (!CompactOnRemove)
        {
            slots[index] = new DresserSlot(index, 0, 0, 0);
            return;
        }

        var kept = slots.Where(s => s.Index != index).Append(new DresserSlot(0, 0, 0, 0)).ToArray();
        slots = kept.Select((s, i) => s with { Index = i }).ToArray();
    }

    private void Consume(List<(int Column, BagItem Item)> pieces)
    {
        foreach (var (_, item) in pieces)
            Bags.Remove(item);
        Prisms -= pieces.Count;
    }

    // Comme le jeu : la liste s'arrête au premier conteneur invalide, chaque pièce doit être là, de la qualité de
    // l'ensemble, à 100 %, et correspondre à la colonne suivante de l'ensemble.
    private List<(int Column, BagItem Item)>? ReadPieces(OutfitDefinition outfit, bool highQuality, uint[] containers, ushort[] inventorySlots)
    {
        if (containers.Length < StoreArguments.MaxPieces || inventorySlots.Length < StoreArguments.MaxPieces)
            return null;

        var items = new List<BagItem>();
        for (var i = 0; i < StoreArguments.MaxPieces && containers[i] != StoreArguments.InvalidContainer; i++)
        {
            var found = Bags.Where(b => b.Container == containers[i] && b.Slot == inventorySlots[i]).ToArray();
            if (found.Length == 0 || found[0].HighQuality != highQuality || found[0].Condition < BagItem.FullCondition)
                return null;
            items.Add(found[0]);
        }

        var pieces = new List<(int Column, BagItem Item)>();
        foreach (var (column, itemId) in outfit.Pieces)
        {
            if (pieces.Count < items.Count && items[pieces.Count].ItemId == itemId)
                pieces.Add((column, items[pieces.Count]));
        }

        return pieces.Count == items.Count && items.Count > 0 ? pieces : null;
    }
}
