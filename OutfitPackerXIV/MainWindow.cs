using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace OutfitPackerXIV;

/// <summary>
/// Fenêtre de la coiffeuse : les ensembles complets, incomplets, ceux qu'une pièce peut rejoindre et les doublons,
/// chacun avec ses pièces et les cases qu'il libère, et un bouton pour le regrouper.
/// </summary>
internal sealed class MainWindow : Window
{
    public const string Key = "###OutfitPackerXIVMain";

    private const string DyedQuestionId = "###dyedQuestion";

    private readonly Plugin plugin;
    private string search = string.Empty;

    // Listes triées par nom et pièces à ranger de chaque ensemble, recalculées quand la coiffeuse ou les réglages changent.
    private DresserReport? sortedFor;
    private bool sortedWithSinglePieces;
    private OutfitStatus[] complete = [];
    private OutfitStatus[] partial = [];
    private OutfitStatus[] addToStored = [];
    private OutfitStatus[] duplicates = [];
    private Dictionary<uint, PackChoice> choices = [];

    // L'ensemble dont des pièces ne sont là que teintes : la question s'ouvre à l'image suivante.
    private PackChoice? dyedQuestion;
    private bool openDyedQuestion;

    public MainWindow(Plugin plugin)
        : base("OutfitPackerXIV" + Key)
    {
        this.plugin = plugin;
        Size = new Vector2(460, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(340, 260), MaximumSize = new Vector2(float.MaxValue) };
    }

    private Configuration Config => plugin.Configuration;

    private ItemInfo Items => plugin.Items;

    // Fermer la fenêtre arrête le regroupement : les pièces déjà sorties sont rangées, rien d'autre ne sort.
    public override void OnClose()
    {
        plugin.ForgetDresserOpening();
        plugin.StopPack();
    }

    public override void Draw()
    {
        DrawDyedQuestion();
        var report = plugin.Report;
        if (report == null)
        {
            DrawNotLoaded();
            return;
        }

        SortLists(report);
        DrawSummary(report);
        DrawPacking();
        ImGui.Spacing();

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", Loc.T("Search an outfit or a piece", "Chercher un ensemble ou une pièce"), ref search, 100);
        ImGui.Spacing();

        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs.Success)
            return;

        DrawTab(Loc.T("Complete", "Complets"), complete, Loc.T(
            "All their pieces are in the dresser, each in its own slot: “Pack” stores them as an outfit.",
            "Toutes leurs pièces sont dans la coiffeuse, chacune dans sa case : « Regrouper » les range en un ensemble."));
        DrawTab(Loc.T("Incomplete", "Incomplets"), partial, Loc.T(
            "Some pieces are missing. The game can store an incomplete outfit: the other pieces can join it later.",
            "Il manque des pièces. Le jeu peut ranger un ensemble incomplet : les autres pièces pourront le rejoindre plus tard."));
        DrawTab(Loc.T("Add to stored", "À compléter"), addToStored, Loc.T(
            "These outfits are already in the dresser, and some of their pieces are still in their own slot.",
            "Ces ensembles sont déjà dans la coiffeuse, et certaines de leurs pièces sont encore dans leur propre case."));
        DrawTab(Loc.T("Duplicates", "En double"), duplicates, Loc.T(
            "These pieces are in the dresser more than once, or already inside their stored outfit.",
            "Ces pièces sont plusieurs fois dans la coiffeuse, ou déjà dans leur ensemble rangé."), duplicatesOnly: true);
        DrawSettingsTab();
    }

    private void DrawNotLoaded()
    {
        if (plugin.ReadError)
        {
            ImGui.TextColored(ImGuiColors.DalamudRed, Loc.T(
                "The Glamour Dresser could not be read: the game has probably been updated. Wait for a new version of the plugin.",
                "Impossible de lire la coiffeuse : le jeu a sans doute été mis à jour. Attends une nouvelle version du plugin."));
        }
        else
        {
            ImGui.TextWrapped(Loc.T(
                "Open the Glamour Dresser: the game only loads its contents at that moment.",
                "Ouvre la coiffeuse : le jeu ne charge son contenu qu'à ce moment-là."));
        }

        DrawPacking();
        ImGui.Spacing();
        DrawSettings();
    }

    private void DrawSummary(DresserReport report)
    {
        var freed = report.MaxFreedSlots(Config.ShowSinglePieces);
        ImGui.TextUnformatted(Loc.T($"{report.UsedSlots} slots used in the dresser.", $"{report.UsedSlots} cases occupées dans la coiffeuse."));
        if (freed > 0)
        {
            ImGui.TextColored(ImGuiColors.HealerGreen, Loc.T(
                $"Storing these outfits frees up to {PackText.Slots(freed)}.",
                $"Ranger ces ensembles libère jusqu'à {PackText.Slots(freed)}."));
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T("No outfit to store.", "Aucun ensemble à ranger."));
        }

        ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T($"Glamour Prisms: {plugin.GlamourPrisms}", $"Prismes mirage : {plugin.GlamourPrisms}"));
        Tooltip(Loc.T(
            "The game uses up to one Glamour Prism per piece stored in an outfit.",
            "Le jeu utilise jusqu'à un prisme mirage par pièce rangée dans un ensemble."));
    }

    // Le regroupement en cours (avec Stop), ou le bilan du dernier.
    private void DrawPacking()
    {
        if (plugin.Job is { } job)
        {
            ImGui.Spacing();
            using (ImRaii.Disabled(job.StopRequested))
            {
                if (ImGui.SmallButton(Loc.T("Stop", "Stop") + "###stop"))
                    plugin.StopPack();
            }

            Tooltip(Loc.T(
                "Takes no more pieces out: those already out are stored in the outfit, then it stops.",
                "Ne sort plus de pièce : celles déjà sorties sont rangées dans l'ensemble, puis il s'arrête."));
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudYellow, PackText.Progress(job, Items));
        }
        else if (plugin.LastResult is { } last)
        {
            ImGui.Spacing();
            ImGui.PushTextWrapPos(0);
            ImGui.TextColored(last.Ok ? ImGuiColors.HealerGreen : ImGuiColors.DalamudOrange, last.Text);
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawTab(string title, OutfitStatus[] outfits, string explanation, bool duplicatesOnly = false)
    {
        var shown = outfits.Where(Matches).ToArray();
        using var tab = ImRaii.TabItem($"{title} ({outfits.Length})###{title}");
        if (!tab.Success)
            return;

        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudGrey))
            ImGui.TextWrapped(explanation);
        ImGui.Separator();

        using var child = ImRaii.Child("##list", new Vector2(-1, -1));
        if (!child.Success)
            return;

        if (shown.Length == 0)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, outfits.Length == 0 ? Loc.T("Nothing here.", "Rien ici.") : Loc.T("No match.", "Aucun résultat."));
            return;
        }

        foreach (var outfit in shown)
            DrawOutfit(outfit, duplicatesOnly);
    }

    private void DrawOutfit(OutfitStatus outfit, bool duplicatesOnly)
    {
        var setId = outfit.Outfit.SetItemId;
        var have = outfit.Pieces.Count(p => !p.Missing);
        var gain = duplicatesOnly
            ? Loc.T($"{outfit.ExtraCopies} extra", $"{outfit.ExtraCopies} en trop")
            : outfit.FreedSlots > 0 ? $"−{PackText.Slots(outfit.FreedSlots)}" : Loc.T("no slot freed", "aucune case libérée");

        if (!duplicatesOnly && choices.TryGetValue(setId, out var choice))
        {
            DrawPackButton(choice);
            ImGui.SameLine();
        }

        Icon(Items.Icon(setId));
        ImGui.SameLine();
        using var node = ImRaii.TreeNode($"{Items.Name(setId)}  ({have}/{outfit.Pieces.Count}) · {gain}###outfit{setId}");
        if (outfit.HasDyedPieces && !duplicatesOnly)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudYellow, Loc.T("dyed", "teinte"));
            Tooltip(Loc.T("At least one of the pieces to store is dyed.", "Au moins une des pièces à ranger est teinte."));
        }

        if (!node.Success)
            return;

        using var table = ImRaii.Table($"##pieces{setId}", 2, ImGuiTableFlags.SizingFixedFit);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("piece", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("state", ImGuiTableColumnFlags.WidthFixed);
        foreach (var piece in outfit.Pieces)
        {
            if (duplicatesOnly && piece.ExtraCopies == 0)
                continue;
            DrawPiece(piece, duplicatesOnly);
        }
    }

    private void DrawPackButton(PackChoice choice)
    {
        var whyDisabled = PackText.WhyDisabled(choice, plugin.Job != null, plugin.DresserOpen, plugin.GlamourPrisms);
        using (ImRaii.Disabled(whyDisabled != null))
        {
            if (ImGui.SmallButton(Loc.T("Pack", "Regrouper") + $"###pack{choice.Main.Outfit.SetItemId}"))
            {
                // Des pièces ne sont là que teintes : on demande avant (le jeu retirerait leur teinture).
                if (choice.AsksAboutDyed)
                {
                    dyedQuestion = choice;
                    openDyedQuestion = true;
                }
                else
                {
                    plugin.StartPack(choice.Undyed);
                }
            }
        }

        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
        if (whyDisabled != null)
            ImGui.TextColored(ImGuiColors.DalamudOrange, whyDisabled);
        foreach (var line in PackText.Describe(choice, plugin.GlamourPrisms, Items))
            ImGui.TextUnformatted(line);
        ImGui.PopTextWrapPos();
    }

    // Ouverte au niveau de la fenêtre (le bouton est dans une liste) : ranger aussi les pièces teintes, ou les laisser.
    private void DrawDyedQuestion()
    {
        var id = PackText.DyedQuestionTitle + DyedQuestionId;
        if (openDyedQuestion)
        {
            ImGui.OpenPopup(id, ImGuiPopupFlags.None);
            openDyedQuestion = false;
        }

        if (dyedQuestion is not { } choice)
            return;

        ImGui.SetNextWindowPos(ImGui.GetWindowPos() + (ImGui.GetWindowSize() / 2), ImGuiCond.Appearing, new Vector2(0.5f));
        using var popup = ImRaii.PopupModal(id, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        if (!popup.Success)
        {
            // Fermée sans réponse (fenêtre refermée entre-temps) : on oublie la question.
            if (!ImGui.IsPopupOpen(id))
                dyedQuestion = null;
            return;
        }

        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24);
        ImGui.TextUnformatted(PackText.DyedQuestion(choice, Items));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        var blocked = plugin.Job != null
            ? Loc.T("Another outfit is being packed.", "Un autre ensemble est en cours de regroupement.")
            : plugin.DresserOpen ? null : Loc.T("Open the Glamour Dresser first.", "Ouvre d'abord la coiffeuse.");
        var answered = DrawAnswer(PackText.StoreDyedToo(choice), choice.WithDyed, blocked)
            | DrawAnswer(PackText.LeaveDyed(choice), choice.Undyed, blocked);
        if (ImGui.Button(PackText.Cancel + "###cancel") || answered)
        {
            dyedQuestion = null;
            ImGui.CloseCurrentPopup();
        }
    }

    private bool DrawAnswer(string label, PackPlan plan, string? blocked)
    {
        var whyNot = blocked ?? PackText.WhyNot(plan, plugin.GlamourPrisms);
        var clicked = false;
        using (ImRaii.Disabled(whyNot != null))
        {
            if (ImGui.Button(label))
            {
                plugin.StartPack(plan);
                clicked = true;
            }
        }

        if (whyNot != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            using var tooltip = ImRaii.Tooltip();
            ImGui.TextUnformatted(whyNot);
        }

        return clicked;
    }

    private void DrawPiece(PieceStatus piece, bool duplicatesOnly)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        Icon(Items.Icon(piece.ItemId));
        ImGui.SameLine();
        ImGui.TextUnformatted(Items.Name(piece.ItemId));
        if (piece.SharedWith.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T("(shared)", "(partagée)"));
            Tooltip(Loc.T("Also part of:", "Fait aussi partie de :") + "\n" + string.Join("\n", piece.SharedWith.Select(Items.Name)));
        }

        ImGui.TableNextColumn();
        if (duplicatesOnly)
        {
            ImGui.TextColored(ImGuiColors.DalamudOrange, piece.InOutfit
                ? Loc.T($"in the outfit + {piece.ExtraCopies} in the dresser", $"dans l'ensemble + {piece.ExtraCopies} dans la coiffeuse")
                : Loc.T($"{piece.Loose.Count} copies", $"{piece.Loose.Count} exemplaires"));
            return;
        }

        if (piece.InOutfit)
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, Loc.T("in the outfit", "dans l'ensemble"));
        }
        else if (piece.Missing)
        {
            ImGui.TextColored(ImGuiColors.DalamudOrange, Loc.T("missing", "manquante"));
        }
        else
        {
            var copies = piece.Loose.Count > 1 ? $" ×{piece.Loose.Count}" : string.Empty;
            ImGui.TextColored(ImGuiColors.HealerGreen, Loc.T("in the dresser", "dans la coiffeuse") + copies);
            if (piece.Loose.Any(l => l.Dyed))
            {
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudYellow, Loc.T("dyed", "teinte"));
            }
        }
    }

    private void DrawSettingsTab()
    {
        using var tab = ImRaii.TabItem(Loc.T("Settings", "Réglages") + "###settings");
        if (tab.Success)
            DrawSettings();
    }

    private void DrawSettings()
    {
        var openWithDresser = Config.OpenWithDresser;
        if (ImGui.Checkbox(Loc.T("Open with the Glamour Dresser", "Ouvrir avec la coiffeuse"), ref openWithDresser))
        {
            Config.OpenWithDresser = openWithDresser;
            Config.Save();
        }

        var singlePieces = Config.ShowSinglePieces;
        if (ImGui.Checkbox(Loc.T("Show outfits with a single piece in the dresser", "Montrer les ensembles dont une seule pièce est dans la coiffeuse"), ref singlePieces))
        {
            Config.ShowSinglePieces = singlePieces;
            Config.Save();
        }

        Tooltip(Loc.T(
            "Storing them frees no slot (the outfit takes the piece's place), but the next pieces you get can join it.",
            "Les ranger ne libère aucune case (l'ensemble prend la place de la pièce), mais les prochaines pièces pourront le rejoindre."));

        ImGui.Spacing();
        ImGui.SetNextItemWidth(160 * ImGuiHelpers.GlobalScale);
        var languages = new[] { (PluginLanguage.Auto, Loc.T("Automatic", "Automatique")), (PluginLanguage.English, "English"), (PluginLanguage.French, "Français") };
        var current = languages.First(l => l.Item1 == Config.Language).Item2;
        using var combo = ImRaii.Combo(Loc.T("Language", "Langue") + "###language", current);
        if (!combo.Success)
            return;

        foreach (var (language, name) in languages)
        {
            if (ImGui.Selectable(name, language == Config.Language))
                plugin.SetLanguage(language);
        }
    }

    private bool Matches(OutfitStatus outfit)
    {
        if (search.Length == 0)
            return true;

        return Items.Name(outfit.Outfit.SetItemId).Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || outfit.Pieces.Any(p => Items.Name(p.ItemId).Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }

    private void SortLists(DresserReport report)
    {
        if (ReferenceEquals(sortedFor, report) && sortedWithSinglePieces == Config.ShowSinglePieces)
            return;

        sortedFor = report;
        sortedWithSinglePieces = Config.ShowSinglePieces;
        complete = Sorted(report.Complete);
        partial = Sorted(report.Partial(Config.ShowSinglePieces));
        addToStored = Sorted(report.AddToStored);
        duplicates = Sorted(report.Duplicates);
        choices = complete.Concat(partial).Concat(addToStored).ToDictionary(o => o.Outfit.SetItemId, PackPlanner.Choose);
    }

    // Plus grand gain d'abord, puis par nom.
    private OutfitStatus[] Sorted(IEnumerable<OutfitStatus> outfits) => outfits
        .OrderByDescending(o => o.FreedSlots)
        .ThenBy(o => Items.Name(o.Outfit.SetItemId), StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    private static void Icon(uint iconId)
    {
        var size = new Vector2(ImGui.GetTextLineHeight() * 1.4f);
        if (iconId == 0)
        {
            ImGui.Dummy(size);
            return;
        }

        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
        ImGui.Image(texture.Handle, size);
    }

    private static void Tooltip(string text)
    {
        if (!ImGui.IsItemHovered())
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}
