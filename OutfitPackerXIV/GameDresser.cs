using FFXIVClientStructs.FFXIV.Client.Game;

namespace OutfitPackerXIV;

/// <summary>Lecture de la coiffeuse dans la mémoire du jeu (sur le fil principal du jeu). Ne modifie rien.</summary>
internal static unsafe class GameDresser
{
    /// <summary>La fenêtre de la coiffeuse.</summary>
    public const string AddonName = "MiragePrismPrismBox";

    /// <summary>
    /// Les cases de la coiffeuse, ou null si le jeu ne les a pas chargées : il les demande au serveur à l'ouverture de la
    /// coiffeuse et les oublie au changement de zone.
    /// </summary>
    public static DresserSlot[]? ReadSlots()
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

    /// <summary>L'ensemble rangé dans cette case contient-il la pièce de cette colonne (feuille MirageStoreSetItem) ?</summary>
    public static bool IsOutfitColumnStored(int index, int column)
    {
        var manager = MirageManager.Instance();
        return manager != null && manager->IsSetSlotUnlocked((uint)index, column);
    }
}
