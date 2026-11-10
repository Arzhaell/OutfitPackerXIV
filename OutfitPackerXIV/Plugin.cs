using System;
using System.Linq;
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
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("OutfitPackerXIV");
    private readonly MainWindow mainWindow;
    private readonly GameDresser dresser = new();

    // Dernières cases lues : on ne refait le calcul que si la coiffeuse a changé.
    private DresserSlot[]? lastSlots;

    // La fonction du jeu qui compte les prismes a échoué (jeu mis à jour sans le plugin) : on ne la rappelle plus.
    private bool prismsUnreadable;

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

    /// <summary>La fenêtre de la coiffeuse est ouverte (relu quand la fenêtre du plugin l'est).</summary>
    internal bool DresserOpen { get; private set; }

    /// <summary>Prismes mirage possédés (relu quand la fenêtre du plugin l'est).</summary>
    internal int GlamourPrisms { get; private set; }

    /// <summary>Le regroupement en cours, un seul à la fois.</summary>
    internal PackJob? Job { get; private set; }

    /// <summary>Le bilan du dernier regroupement, affiché jusqu'au suivant.</summary>
    internal (string Text, bool Ok)? LastResult { get; private set; }

    internal void StartPack(PackPlan plan)
    {
        if (Job != null)
            return;

        LastResult = null;
        Job = new PackJob(dresser, plan);
        Log.Information($"Packing outfit {plan.DresserSetItemId}: {plan.Pieces.Count} pieces ({string.Join(", ", plan.Pieces.Select(p => p.DresserItemId))}), new: {plan.IsNew}.");
    }

    /// <summary>Ne plus sortir de pièce : celles déjà sorties sont rangées, puis le regroupement s'arrête.</summary>
    internal void StopPack() => Job?.Stop();

    public void SetLanguage(PluginLanguage language)
    {
        Configuration.Language = language;
        Configuration.Save();
        RefreshLanguage();
    }

    public void Dispose()
    {
        if (Job != null)
            Log.Warning($"Unloaded while packing outfit {Job.Plan.DresserSetItemId}: {Job.TakenOut} pieces taken out of the dresser.");
        Job = null;
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

    // Sur le fil principal du jeu : le regroupement en cours, puis la lecture de la coiffeuse quand la fenêtre est ouverte.
    private void OnFrameworkUpdate(IFramework framework)
    {
        UpdateJob();
        if (!mainWindow.IsOpen)
            return;

        var slots = dresser.ReadSlots();
        if (slots == null)
        {
            Report = null;
            lastSlots = null;
            DresserOpen = false;
            return;
        }

        DresserOpen = dresser.IsDresserOpen;
        if (!prismsUnreadable)
        {
            try
            {
                GlamourPrisms = dresser.GlamourPrisms;
            }
            catch (Exception e)
            {
                // Noté une fois : sans prismes comptés, les boutons « Regrouper » restent grisés.
                Log.Error(e, "Could not count the Glamour Prisms.");
                prismsUnreadable = true;
            }
        }

        if (lastSlots != null && slots.AsSpan().SequenceEqual(lastSlots))
            return;

        lastSlots = slots;
        try
        {
            Report = DresserScan.Run(Catalog, slots, dresser.IsOutfitColumnStored);
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

    // Une étape au plus par image : le regroupement attend que le jeu confirme chaque action avant la suivante.
    private void UpdateJob()
    {
        if (Job == null)
            return;

        try
        {
            Job.Update(TimeSpan.FromMilliseconds(Environment.TickCount64));
        }
        catch (Exception e)
        {
            Log.Error(e, "Packing failed.");
            Job.Abort(PackProblem.GameError);
        }

        if (Job.Result is not { } result)
            return;

        var plan = Job.Plan;
        Job = null;
        Log.Information($"Packing outfit {plan.DresserSetItemId} ended: {result.Problem} (piece {result.ProblemItemId}), "
            + $"{result.StoredPieces} stored, left in inventory: [{string.Join(", ", result.LeftInInventory)}], "
            + $"no longer in the dresser: [{string.Join(", ", result.NoLongerInDresser)}].");
        var (text, ok) = PackText.Result(plan, result, Items);
        LastResult = (text, ok);
        if (ok)
            ChatGui.Print(text, "OutfitPackerXIV");
        else
            ChatGui.PrintError(text, "OutfitPackerXIV");
    }

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
