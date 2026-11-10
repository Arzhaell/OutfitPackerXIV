using System;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace OutfitPackerXIV;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/outfitpacker";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("OutfitPackerXIV");
    private readonly MainWindow mainWindow;

    // Dernières cases lues : on ne refait le calcul que si la coiffeuse a changé.
    private DresserSlot[]? lastSlots;

    // La fenêtre a été ouverte par la coiffeuse : elle se ferme avec elle.
    private bool openedWithDresser;

    public Plugin()
    {
        Configuration = LoadConfiguration();
        Loc.Update(Configuration.Language);

        Catalog = OutfitCatalog.FromSheet(DataManager.GetExcelSheet<MirageStoreSetItem>());
        Items = new ItemInfo(DataManager.GetExcelSheet<Item>());

        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi += mainWindow.Toggle;
        PluginInterface.LanguageChanged += OnDalamudLanguageChanged;
        AddonLifecycle.RegisterListener(AddonEvent.PostSetup, GameDresser.AddonName, OnDresserOpened);
        AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, GameDresser.AddonName, OnDresserClosed);

        RegisterCommand();
        Log.Information($"{Catalog.Count} outfits in the game data.");
    }

    public Configuration Configuration { get; }

    internal OutfitCatalog Catalog { get; }

    internal ItemInfo Items { get; }

    /// <summary>Ce que la coiffeuse permet de regrouper, ou null tant que le jeu ne l'a pas chargée.</summary>
    internal DresserReport? Report { get; private set; }

    public void SetLanguage(PluginLanguage language)
    {
        Configuration.Language = language;
        Configuration.Save();
        RefreshLanguage();
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, GameDresser.AddonName, OnDresserOpened);
        AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, GameDresser.AddonName, OnDresserClosed);
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.LanguageChanged -= OnDalamudLanguageChanged;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= mainWindow.Toggle;
        PluginInterface.UiBuilder.OpenConfigUi -= mainWindow.Toggle;
        windowSystem.RemoveAllWindows();
    }

    // Un fichier de configuration illisible ne doit pas empêcher le plugin de se charger : on repart des réglages par défaut.
    private static Configuration LoadConfiguration()
    {
        try
        {
            return PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        }
        catch (Exception e)
        {
            Log.Error(e, "Could not read the configuration, falling back to default settings.");
            return new Configuration();
        }
    }

    // Sur le fil principal du jeu : lecture de la coiffeuse, seulement quand la fenêtre est ouverte.
    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!mainWindow.IsOpen)
            return;

        var slots = GameDresser.ReadSlots();
        if (slots == null)
        {
            Report = null;
            lastSlots = null;
            return;
        }

        if (lastSlots != null && slots.AsSpan().SequenceEqual(lastSlots))
            return;

        lastSlots = slots;
        try
        {
            Report = DresserScan.Run(Catalog, slots, GameDresser.IsOutfitColumnStored);
            ReadError = false;
        }
        catch (Exception e)
        {
            // Une fonction du jeu introuvable après un patch : on le note une fois, la fenêtre l'affiche.
            Log.Error(e, "Could not read the Glamour Dresser.");
            Report = null;
            ReadError = true;
        }
    }

    /// <summary>La lecture de la coiffeuse a échoué (jeu mis à jour sans que le plugin le soit).</summary>
    internal bool ReadError { get; private set; }

    private void OnDresserOpened(AddonEvent type, AddonArgs args)
    {
        if (!Configuration.OpenWithDresser || mainWindow.IsOpen)
            return;

        mainWindow.IsOpen = true;
        openedWithDresser = true;
    }

    private void OnDresserClosed(AddonEvent type, AddonArgs args)
    {
        if (openedWithDresser)
            mainWindow.IsOpen = false;
        openedWithDresser = false;
    }

    /// <summary>La fenêtre a été fermée ou rouverte à la main : elle ne suit plus la coiffeuse.</summary>
    internal void ForgetDresserOpening() => openedWithDresser = false;

    private void OnCommand(string command, string arguments)
    {
        ForgetDresserOpening();
        mainWindow.Toggle();
    }

    private static string CommandHelp => Loc.T(
        "/outfitpacker: show the outfits you can store in the Glamour Dresser.",
        "/outfitpacker : voir les ensembles à ranger dans la coiffeuse.");

    private void OnDalamudLanguageChanged(string languageCode)
    {
        if (Configuration.Language == PluginLanguage.Auto)
            RefreshLanguage();
    }

    private void RefreshLanguage()
    {
        Loc.Update(Configuration.Language);
        CommandManager.RemoveHandler(CommandName);
        RegisterCommand();
    }

    private void RegisterCommand()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand) { HelpMessage = CommandHelp });
    }
}
