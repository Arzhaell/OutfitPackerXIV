using System;
using Dalamud.Configuration;

namespace OutfitPackerXIV;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public PluginLanguage Language { get; set; } = PluginLanguage.Auto;

    /// <summary>Ouvrir la fenêtre en même temps que la coiffeuse (et la fermer avec elle).</summary>
    public bool OpenWithDresser { get; set; } = true;

    /// <summary>
    /// Montrer aussi les ensembles dont une seule pièce est dans la coiffeuse : les ranger ne libère aucune case
    /// (la pièce prend la place de l'ensemble), mais les pièces suivantes pourront s'y ajouter.
    /// </summary>
    public bool ShowSinglePieces { get; set; }

    /// <summary>Regrouper aussi les pièces teintes : le jeu retire leur teinture en les rangeant dans l'ensemble.</summary>
    public bool IncludeDyedPieces { get; set; }

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
