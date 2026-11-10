using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace OutfitPackerXIV;

/// <summary>
/// La coiffeuse et l'inventaire dans la mémoire du jeu, sur le fil principal du jeu. Les actions passent par les
/// fonctions que la fenêtre du jeu appelle elle-même ; si l'une manque après un patch, FFXIVClientStructs lève une
/// exception au lieu de l'appeler.
/// </summary>
internal sealed unsafe class GameDresser : IDresserGame
{
    /// <summary>La fenêtre de la coiffeuse.</summary>
    public const string AddonName = "MiragePrismPrismBox";

    /// <summary>Prisme mirage : le jeu en demande un par pièce rangée dans un ensemble.</summary>
    private const uint GlamourPrismItemId = 21800;

    // Là où une pièce sortie de la coiffeuse peut arriver. Jamais l'équipement porté.
    private static readonly InventoryType[] BagTypes =
    [
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
        InventoryType.ArmoryMainHand, InventoryType.ArmoryOffHand, InventoryType.ArmoryHead, InventoryType.ArmoryBody,
        InventoryType.ArmoryHands, InventoryType.ArmoryLegs, InventoryType.ArmoryFeets, InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck, InventoryType.ArmoryWrist, InventoryType.ArmoryRings,
    ];

    public bool IsDresserOpen => Plugin.GameGui.GetAddonByName(AddonName) is { IsNull: false, IsVisible: true };

    /// <summary>
    /// Les cases de la coiffeuse, ou null si le jeu ne les a pas chargées : il les demande au serveur à l'ouverture de la
    /// coiffeuse et les oublie au changement de zone.
    /// </summary>
    public DresserSlot[]? ReadSlots()
    {
        var manager = MirageManager.Instance();
        if (manager == null || !manager->PrismBoxLoaded)
            return null;

        var itemIds = manager->PrismBoxItemIds;
        var stain0 = manager->PrismBoxStain0Ids;
        var stain1 = manager->PrismBoxStain1Ids;
        var slots = new DresserSlot[itemIds.Length];
        for (var i = 0; i < itemIds.Length; i++)
            slots[i] = new DresserSlot(i, itemIds[i], stain0[i], stain1[i]);
        return slots;
    }

    IReadOnlyList<DresserSlot>? IDresserGame.ReadSlots() => ReadSlots();

    /// <summary>
    /// L'ensemble rangé dans cette case contient-il la pièce de cette colonne (feuille MirageStoreSetItem) ? Le jeu note
    /// les pièces manquantes dans les « teintures » de la case ; cette fonction répond vrai quand la pièce y est.
    /// </summary>
    public bool IsOutfitColumnStored(int index, int column)
    {
        var manager = MirageManager.Instance();
        return manager != null && manager->IsSetSlotUnlocked((uint)index, column);
    }

    public IReadOnlyList<BagItem> ReadBags()
    {
        var items = new List<BagItem>();
        var inventory = InventoryManager.Instance();
        if (inventory == null)
            return items;

        foreach (var type in BagTypes)
        {
            var container = inventory->GetInventoryContainer(type);
            if (container == null || !container->IsLoaded)
                continue;

            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item == null || item->ItemId == 0)
                    continue;

                items.Add(new BagItem(
                    (uint)type,
                    i,
                    item->ItemId,
                    item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality),
                    item->Condition));
            }
        }

        return items;
    }

    // Compté comme le jeu le fait avant de ranger un ensemble : sacs, équipement porté et arsenal.
    public int GlamourPrisms
    {
        get
        {
            var inventory = InventoryManager.Instance();
            return inventory == null ? 0 : inventory->GetInventoryItemCount(GlamourPrismItemId);
        }
    }

    public bool Restore(int index)
    {
        var manager = MirageManager.Instance();
        return manager != null && manager->RestorePrismBoxItem((uint)index);
    }

    public bool StoreNew(uint dresserSetItemId, uint[] containers, ushort[] slots)
    {
        var manager = MirageManager.Instance();
        if (manager == null)
            return false;

        fixed (uint* c = containers)
        fixed (ushort* s = slots)
            return manager->StoreNewOutfit(dresserSetItemId, (InventoryType*)c, s);
    }

    public bool StoreExisting(int index, uint[] containers, ushort[] slots)
    {
        var manager = MirageManager.Instance();
        if (manager == null)
            return false;

        fixed (uint* c = containers)
        fixed (ushort* s = slots)
            return manager->StoreExistingOutfit((uint)index, (InventoryType*)c, s);
    }
}
