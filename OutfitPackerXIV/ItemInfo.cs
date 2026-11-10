using System.Collections.Generic;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace OutfitPackerXIV;

/// <summary>Nom (dans la langue du jeu) et icône des objets, gardés une fois lus.</summary>
internal sealed class ItemInfo
{
    private readonly ExcelSheet<Item> sheet;
    private readonly Dictionary<uint, (string Name, uint Icon)> cache = [];

    public ItemInfo(ExcelSheet<Item> sheet) => this.sheet = sheet;

    public string Name(uint itemId) => Get(itemId).Name;

    public uint Icon(uint itemId) => Get(itemId).Icon;

    private (string Name, uint Icon) Get(uint itemId)
    {
        if (cache.TryGetValue(itemId, out var info))
            return info;

        info = sheet.TryGetRow(itemId, out var row) ? (row.Name.ExtractText(), (uint)row.Icon) : ($"#{itemId}", 0u);
        if (info.Name.Length == 0)
            info.Name = $"#{itemId}";
        cache[itemId] = info;
        return info;
    }
}
