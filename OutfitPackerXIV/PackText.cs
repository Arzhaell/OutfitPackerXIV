using System.Collections.Generic;
using System.Linq;

namespace OutfitPackerXIV;

/// <summary>Les textes du regroupement, en anglais et en français.</summary>
internal static class PackText
{
    public static string Slots(int count) => count == 1
        ? Loc.T("1 slot", "1 case")
        : Loc.T($"{count} slots", $"{count} cases");

    public static string Pieces(int count) => count == 1
        ? Loc.T("1 piece", "1 pièce")
        : Loc.T($"{count} pieces", $"{count} pièces");

    private static string Freed(int count) => count switch
    {
        <= 0 => Loc.T("no slot freed", "aucune case libérée"),
        1 => Loc.T("1 slot freed", "1 case libérée"),
        _ => Loc.T($"{count} slots freed", $"{count} cases libérées"),
    };

    /// <summary>Pourquoi le bouton « Regrouper » (ou « Ranger ») est grisé, ou null s'il ne l'est pas.</summary>
    public static string? WhyDisabled(PackChoice choice, bool busy, bool dresserOpen, int prisms, int freeDresserSlots)
    {
        if (busy)
            return Loc.T("Another action is running in the dresser.", "Une autre action est en cours dans la coiffeuse.");
        if (!dresserOpen)
            return Loc.T("Open the Glamour Dresser first.", "Ouvre d'abord la coiffeuse.");
        return WhyNot(choice.Main, prisms, freeDresserSlots);
    }

    /// <summary>Pourquoi ce regroupement n'est pas possible, ou null s'il l'est.</summary>
    public static string? WhyNot(PackPlan plan, int prisms, int freeDresserSlots)
    {
        if (!plan.HasWork)
            return Loc.T("No piece to store.", "Aucune pièce à ranger.");
        if (prisms < plan.PrismCost)
        {
            return Loc.T(
                $"Not enough Glamour Prisms: {plan.PrismCost} needed, you have {prisms}.",
                $"Pas assez de prismes mirage : il en faut {plan.PrismCost}, tu en as {prisms}.");
        }

        if (plan.IsNew && plan.ToRestore.Count == 0 && freeDresserSlots <= 0)
            return DresserFull;

        return null;
    }

    private static string DresserFull => Loc.T(
        "The dresser is full: a new outfit takes one slot.",
        "La coiffeuse est pleine : un nouvel ensemble y prend une case.");

    /// <summary>Ce que l'ensemble libère, en court (liste de l'onglet Inventaire).</summary>
    public static string ShortBalance(PackPlan plan)
    {
        var parts = new List<string>();
        if (plan.BagSlotsFreed > 0)
            parts.Add($"−{plan.BagSlotsFreed} " + Loc.T("inventory", "inventaire"));
        if (plan.DresserSlotsFreed != 0)
            parts.Add((plan.DresserSlotsFreed > 0 ? $"−{plan.DresserSlotsFreed} " : $"+{-plan.DresserSlotsFreed} ") + Loc.T("dresser", "coiffeuse"));
        return parts.Count > 0 ? string.Join(", ", parts) : Freed(0);
    }

    /// <summary>Les cases libérées dans l'inventaire et dans la coiffeuse (ou prises dans la coiffeuse).</summary>
    private static string Balance(int dresserFreed, int bagsFreed)
    {
        var parts = new List<string>();
        if (bagsFreed > 0)
        {
            parts.Add(Loc.T(
                $"{Slots(bagsFreed)} freed in your inventory",
                $"{Slots(bagsFreed)} {(bagsFreed == 1 ? "libérée" : "libérées")} dans ton inventaire"));
        }

        if (dresserFreed > 0)
        {
            parts.Add(Loc.T(
                $"{Slots(dresserFreed)} freed in the dresser",
                $"{Slots(dresserFreed)} {(dresserFreed == 1 ? "libérée" : "libérées")} dans la coiffeuse"));
        }
        else if (dresserFreed < 0)
        {
            parts.Add(Loc.T(
                $"{Slots(-dresserFreed)} taken in the dresser",
                $"{Slots(-dresserFreed)} {(dresserFreed == -1 ? "prise" : "prises")} dans la coiffeuse"));
        }

        return parts.Count > 0 ? string.Join(", ", parts) : Freed(0);
    }

    /// <summary>Ce que le bouton « Ranger » de l'onglet Inventaire va faire.</summary>
    public static IEnumerable<string> DescribeFromBags(PackChoice choice, int prisms, ItemInfo items)
    {
        var plan = choice.Main;
        if (plan.HasWork)
        {
            var count = Pieces(plan.FromBags.Count);
            yield return Loc.T($"Stores {count} from your inventory (bags or armory chest)", $"Range {count} de ton inventaire (sacs ou arsenal)")
                + (plan.ToRestore.Count > 0
                    ? Loc.T($" and {Pieces(plan.ToRestore.Count)} taken out of the dresser first", $" et {Pieces(plan.ToRestore.Count)} sortie{(plan.ToRestore.Count == 1 ? string.Empty : "s")} d'abord de la coiffeuse")
                    : string.Empty)
                + (plan.IsNew
                    ? Loc.T(" as a new outfit.", " en un nouvel ensemble.")
                    : Loc.T(" in the stored outfit.", " dans l'ensemble rangé."));
            yield return Balance(plan.DresserSlotsFreed, plan.BagSlotsFreed) + ".";
            yield return Loc.T(
                $"Uses up to {plan.PrismCost} Glamour Prisms (one per piece; you have {prisms}).",
                $"Utilise jusqu'à {plan.PrismCost} prismes mirage (un par pièce ; tu en as {prisms}).");
            if (plan.HighQuality)
                yield return Loc.T("High-quality pieces: stored in a high-quality outfit.", "Pièces de haute qualité : rangées dans un ensemble HQ.");

            var signed = plan.FromBags.Where(p => p.Bag!.Value.Signed).Select(p => items.Name(p.ItemId)).ToArray();
            if (signed.Length > 0)
                yield return Loc.T("The game removes the crafter's signature of: ", "Le jeu retire la signature d'artisan de : ") + string.Join(", ", signed) + ".";
        }

        if (choice.AsksAboutDyed)
        {
            var one = choice.DyedOnly.Count == 1;
            yield return Loc.T("Only dyed: ", one ? "Seulement teinte : " : "Seulement teintes : ")
                + Names(choice.DyedOnly, items) + ". "
                + (one
                    ? Loc.T("It will ask you whether to store it too (the game removes its dye).", "Il te demandera s'il faut la ranger aussi (le jeu retire sa teinture).")
                    : Loc.T("It will ask you whether to store them too (the game removes their dye).", "Il te demandera s'il faut les ranger aussi (le jeu retire leur teinture)."));
        }

        var left = plan.Skipped.Where(s => s.Reason != SkipReason.Dyed).Select(s => $"{items.Name(s.ItemId)} ({Reason(s.Reason)})").ToArray();
        if (left.Length > 0)
            yield return Loc.T("Left where they are: ", "Restent à leur place : ") + string.Join(", ", left) + ".";
    }

    /// <summary>Pourquoi une pièce reste à sa place, en quelques mots.</summary>
    public static string Reason(SkipReason reason) => reason switch
    {
        SkipReason.Dyed => Loc.T("dyed", "teinte"),
        SkipReason.OtherQuality => Loc.T("other quality", "autre qualité"),
        SkipReason.InGearset => Loc.T("used by a gear set", "dans un ensemble d'équipement"),
        SkipReason.HasMateria => Loc.T("materia", "matérias"),
        SkipReason.Glamoured => Loc.T("glamoured", "mirage projeté"),
        SkipReason.NotRepaired => Loc.T("needs repair", "à réparer"),
        _ => reason.ToString(),
    };

    /// <summary>Ce que le bouton « Regrouper » va faire.</summary>
    public static IEnumerable<string> Describe(PackChoice choice, int prisms, ItemInfo items)
    {
        var plan = choice.Main;
        if (plan.HasWork)
        {
            var count = Pieces(plan.Pieces.Count);
            var one = plan.Pieces.Count == 1;
            yield return plan.IsNew
                ? Loc.T(
                    $"Takes {count} out of the dresser and stores {(one ? "it" : "them")} as an outfit.",
                    $"Sort {count} de la coiffeuse et {(one ? "la" : "les")} range en un ensemble.")
                : Loc.T(
                    $"Takes {count} out of the dresser and adds {(one ? "it" : "them")} to the stored outfit.",
                    $"Sort {count} de la coiffeuse et {(one ? "l'" : "les ")}ajoute à l'ensemble rangé.");
            yield return Loc.T(
                $"Uses up to {plan.PrismCost} Glamour Prisms (one per piece; you have {prisms}).",
                $"Utilise jusqu'à {plan.PrismCost} prismes mirage (un par pièce ; tu en as {prisms}).");
            if (plan.HighQuality)
                yield return Loc.T("High-quality pieces: stored in a high-quality outfit.", "Pièces de haute qualité : rangées dans un ensemble HQ.");
        }

        if (choice.AsksAboutDyed)
        {
            var one = choice.DyedOnly.Count == 1;
            yield return Loc.T("Only dyed in the dresser: ", one ? "Seulement teinte dans la coiffeuse : " : "Seulement teintes dans la coiffeuse : ")
                + Names(choice.DyedOnly, items) + ". "
                + (one
                    ? Loc.T("It will ask you whether to store it too (the game removes its dye).", "Il te demandera s'il faut la ranger aussi (le jeu retire sa teinture).")
                    : Loc.T("It will ask you whether to store them too (the game removes their dye).", "Il te demandera s'il faut les ranger aussi (le jeu retire leur teinture)."));
        }

        var otherQuality = plan.Skipped.Where(s => s.Reason == SkipReason.OtherQuality).Select(s => items.Name(s.ItemId)).ToArray();
        if (otherQuality.Length > 0)
            yield return Loc.T("Left in their slot (other quality): ", "Restent dans leur case (autre qualité) : ") + string.Join(", ", otherQuality);
    }

    public static string DyedQuestionTitle => Loc.T("Dyed pieces", "Pièces teintes");

    /// <summary>La question posée quand des pièces ne sont là que teintes.</summary>
    public static string DyedQuestion(PackChoice choice, ItemInfo items)
    {
        var name = items.Name(choice.WithDyed.Outfit.SetItemId);
        var one = choice.DyedOnly.Count == 1;
        var pieces = Names(choice.DyedOnly, items);
        var inBags = choice.DyedOnly.Any(p => p.Bag != null);
        return Loc.T($"Outfit “{name}”: ", $"Ensemble « {name} » : ")
            + (inBags
                ? one
                    ? Loc.T($"{pieces} is only there dyed.", $"{pieces} n'est là qu'en version teinte.")
                    : Loc.T($"{pieces} are only there dyed.", $"{pieces} ne sont là qu'en version teinte.")
                : one
                    ? Loc.T($"{pieces} is only in the dresser dyed.", $"{pieces} n'est dans la coiffeuse qu'en version teinte.")
                    : Loc.T($"{pieces} are only in the dresser dyed.", $"{pieces} ne sont dans la coiffeuse qu'en version teinte."))
            + " " + Loc.T(
                "The game removes the dye of the pieces it stores in an outfit.",
                "Le jeu retire la teinture des pièces qu'il range dans un ensemble.");
    }

    public static string StoreDyedToo(PackChoice choice) => (choice.DyedOnly.Count == 1
            ? Loc.T("Store it too", "La ranger aussi")
            : Loc.T("Store them too", "Les ranger aussi"))
        + $" ({Pieces(choice.WithDyed.Pieces.Count)})";

    public static string LeaveDyed(PackChoice choice) => (choice.DyedOnly.Count == 1
            ? Loc.T("Leave it in its slot", "La laisser dans sa case")
            : Loc.T("Leave them in their slot", "Les laisser dans leur case"))
        + $" ({Pieces(choice.Undyed.Pieces.Count)})";

    public static string Cancel => Loc.T("Cancel", "Annuler");

    private static string Names(IEnumerable<PlannedPiece> pieces, ItemInfo items) => string.Join(", ", pieces.Select(p => items.Name(p.ItemId)));

    /// <summary>Où en est l'action en cours.</summary>
    public static string Progress(IDresserJob job, ItemInfo items)
    {
        var text = job switch
        {
            PackJob pack => pack.IsStoring
                ? Loc.T(
                    $"Packing “{items.Name(pack.Plan.Outfit.SetItemId)}”: storing the outfit…",
                    $"Regroupement de « {items.Name(pack.Plan.Outfit.SetItemId)} » : rangement dans l'ensemble…")
                : Loc.T(
                    $"Packing “{items.Name(pack.Plan.Outfit.SetItemId)}”: taking pieces out ({pack.TakenOut}/{pack.Plan.ToRestore.Count})…",
                    $"Regroupement de « {items.Name(pack.Plan.Outfit.SetItemId)} » : sortie des pièces ({pack.TakenOut}/{pack.Plan.ToRestore.Count})…"),
            RemoveJob remove => Loc.T(
                $"Duplicates of “{items.Name(remove.Plan.Outfit.SetItemId)}”: taking them out ({remove.Removed}/{remove.Plan.Copies.Count})…",
                $"Doublons de « {items.Name(remove.Plan.Outfit.SetItemId)} » : sortie ({remove.Removed}/{remove.Plan.Copies.Count})…"),
            _ => string.Empty,
        };
        return job.StopRequested ? text + Loc.T(" (stopping)", " (arrêt demandé)") : text;
    }

    /// <summary>Pourquoi le bouton « Enlever » est grisé, ou null s'il ne l'est pas.</summary>
    public static string? RemoveWhyDisabled(RemovePlan plan, bool busy, bool dresserOpen)
    {
        if (busy)
            return Loc.T("Another action is running in the dresser.", "Une autre action est en cours dans la coiffeuse.");
        if (!dresserOpen)
            return Loc.T("Open the Glamour Dresser first.", "Ouvre d'abord la coiffeuse.");
        return plan.HasWork ? null : Loc.T("No extra copy to remove.", "Aucun exemplaire en trop à enlever.");
    }

    /// <summary>Ce que le bouton « Enlever » va faire.</summary>
    public static IEnumerable<string> DescribeRemoval(RemovePlan plan, ItemInfo items)
    {
        if (!plan.HasWork)
            yield break;

        var copies = plan.Copies.Select(c => items.Name(c.ItemId)
            + (c.HighQuality ? " HQ" : string.Empty)
            + (c.Dyed ? Loc.T(" (dyed)", " (teinte)") : string.Empty));
        yield return Loc.T(
            $"Takes {Copies(plan.Copies.Count)} out of the dresser: ",
            $"Sort {Copies(plan.Copies.Count)} en trop de la coiffeuse : ") + string.Join(", ", copies) + ".";
        yield return Loc.T(
            "One copy stays for each outfit that does not have the piece yet.",
            "Un exemplaire reste pour chaque ensemble qui n'a pas encore la pièce.");
        yield return Loc.T(
            "They go to your inventory: the plugin never throws anything away. The game binds them to your character (no more market board).",
            "Ils arrivent dans ton inventaire : le plugin ne jette rien. Le jeu les lie à ton personnage (plus de vente au marché).");
    }

    /// <summary>Le bilan des doublons enlevés, et si tout s'est bien passé.</summary>
    public static (string Text, bool Ok) RemoveResult(RemovePlan plan, RemoveResult result, ItemInfo items)
    {
        var name = items.Name(plan.Outfit.SetItemId);
        var lines = new List<string>
        {
            result.Removed > 0
                ? Loc.T(
                    $"Duplicates of “{name}”: {Copies(result.Removed)} taken out to your inventory, {Freed(result.Removed)}.",
                    $"Doublons de « {name} » : {Copies(result.Removed)} {(result.Removed == 1 ? "sorti" : "sortis")} dans ton inventaire, {Freed(result.Removed)}.")
                : Loc.T($"Duplicates of “{name}”: nothing taken out.", $"Doublons de « {name} » : rien n'est sorti."),
        };

        if (Problem(result.Problem, result.ProblemItemId, items) is { } problem)
            lines.Add(problem);

        if (result.NoLongerInDresser.Count > 0)
        {
            lines.Add(Loc.T("No longer in the dresser: ", "Plus dans la coiffeuse : ")
                + string.Join(", ", result.NoLongerInDresser.Distinct().Select(items.Name)) + ".");
        }

        return (string.Join(" ", lines), result.Problem is PackProblem.None or PackProblem.Stopped);
    }

    private static string Copies(int count) => count == 1
        ? Loc.T("1 copy", "1 exemplaire")
        : Loc.T($"{count} copies", $"{count} exemplaires");

    /// <summary>Le bilan d'un regroupement, et s'il s'est bien passé.</summary>
    public static (string Text, bool Ok) Result(PackPlan plan, PackResult result, ItemInfo items)
    {
        var name = items.Name(plan.Outfit.SetItemId);
        var lines = new List<string>();
        if (result.StoredPieces > 0)
        {
            var fromDresser = result.StoredPieces - result.StoredFromBags;
            var freed = result.StoredFromBags > 0
                ? Balance(plan.IsNew ? fromDresser - 1 : fromDresser, result.StoredFromBags)
                : Freed(plan.IsNew ? result.StoredPieces - 1 : result.StoredPieces);
            var count = Pieces(result.StoredPieces);
            var added = result.StoredPieces == 1 ? "ajoutée" : "ajoutées";
            lines.Add(plan.IsNew
                ? Loc.T($"Outfit “{name}” stored: {count}, {freed}.", $"Ensemble « {name} » rangé : {count}, {freed}.")
                : Loc.T($"{count} added to “{name}”, {freed}.", $"{count} {added} à « {name} », {freed}."));
        }
        else
        {
            lines.Add(Loc.T($"Outfit “{name}” not stored.", $"L'ensemble « {name} » n'a pas été rangé."));
        }

        if (Problem(result.Problem, result.ProblemItemId, items) is { } problem)
            lines.Add(problem);

        if (result.LeftInInventory.Count > 0)
        {
            lines.Add(Loc.T("Still in your inventory: ", "Encore dans ton inventaire : ")
                + string.Join(", ", result.LeftInInventory.Select(items.Name)) + ".");
        }

        if (result.NoLongerInDresser.Count > 0)
        {
            lines.Add(Loc.T("No longer in the dresser: ", "Plus dans la coiffeuse : ")
                + string.Join(", ", result.NoLongerInDresser.Select(items.Name)) + ".");
        }

        var ok = (result.Problem is PackProblem.None or PackProblem.Stopped) && result.LeftInInventory.Count == 0;
        return (string.Join(" ", lines), ok);
    }

    // Ce qui a arrêté ou écourté l'action, en une phrase, ou null.
    private static string? Problem(PackProblem problem, uint problemItemId, ItemInfo items)
    {
        var piece = problemItemId != 0 ? items.Name(problemItemId) : Loc.T("a piece", "une pièce");
        return problem switch
        {
            PackProblem.NothingToStore => Loc.T("Nothing to store: its pieces are no longer in the dresser.", "Rien à ranger : ses pièces ne sont plus dans la coiffeuse."),
            PackProblem.NothingToRemove => Loc.T("Nothing to remove: the extra copies are no longer in the dresser.", "Rien à enlever : les exemplaires en trop ne sont plus dans la coiffeuse."),
            PackProblem.NotEnoughPrisms => Loc.T("Not enough Glamour Prisms (one per piece).", "Pas assez de prismes mirage (un par pièce)."),
            PackProblem.AlreadyStored => Loc.T("This outfit is already in the dresser.", "Cet ensemble est déjà dans la coiffeuse."),
            PackProblem.OutfitNotFound => Loc.T("The outfit is no longer in the dresser.", "L'ensemble n'est plus dans la coiffeuse."),
            PackProblem.DresserFull => DresserFull,
            PackProblem.DresserClosed => Loc.T("The Glamour Dresser was closed.", "La coiffeuse a été fermée."),
            PackProblem.RestoreRefused => Loc.T(
                $"The game refused three times to take {piece} out of the dresser (a unique item you already have?). Try again in a moment.",
                $"Le jeu a refusé trois fois de sortir {piece} de la coiffeuse (objet unique que tu as déjà ?). Réessaie dans un instant."),
            PackProblem.InventoryFull => Loc.T(
                $"Your inventory is full: {piece} could not leave the dresser.",
                $"Ton inventaire est plein : {piece} n'a pas pu sortir de la coiffeuse."),
            PackProblem.Timeout => Loc.T("The game did not confirm in time.", "Le jeu n'a pas confirmé à temps.")
                + (problemItemId != 0 ? Loc.T($" Check where {piece} is.", $" Vérifie où est {piece}.") : string.Empty),
            PackProblem.NotFullCondition => Loc.T(
                $"{piece} is not at 100% condition: the game refuses to store it. Repair it first.",
                $"La pièce {piece} n'est pas à 100 % de condition : le jeu refuse de la ranger. Répare-la d'abord."),
            PackProblem.PieceMoved => Loc.T($"{piece} was moved in the meantime.", $"La pièce {piece} a été déplacée entre-temps."),
            PackProblem.StoreRefused => Loc.T("The game refused to store the pieces in the outfit.", "Le jeu a refusé de ranger les pièces dans l'ensemble."),
            PackProblem.Stopped => Loc.T("Stopped on request.", "Arrêté à ta demande."),
            PackProblem.GameError => Loc.T(
                "A game function failed (the game has probably been updated).",
                "Une fonction du jeu a échoué (le jeu a sans doute été mis à jour)."),
            _ => null,
        };
    }
}
