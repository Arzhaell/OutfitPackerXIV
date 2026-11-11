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

    /// <summary>Pourquoi le bouton « Regrouper » est grisé, ou null s'il ne l'est pas.</summary>
    public static string? WhyDisabled(PackChoice choice, bool busy, bool dresserOpen, int prisms)
    {
        if (busy)
            return Loc.T("Another outfit is being packed.", "Un autre ensemble est en cours de regroupement.");
        if (!dresserOpen)
            return Loc.T("Open the Glamour Dresser first.", "Ouvre d'abord la coiffeuse.");
        return WhyNot(choice.Main, prisms);
    }

    /// <summary>Pourquoi ce regroupement n'est pas possible, ou null s'il l'est.</summary>
    public static string? WhyNot(PackPlan plan, int prisms)
    {
        if (!plan.HasWork)
            return Loc.T("No piece to store.", "Aucune pièce à ranger.");
        if (prisms < plan.PrismCost)
        {
            return Loc.T(
                $"Not enough Glamour Prisms: {plan.PrismCost} needed, you have {prisms}.",
                $"Pas assez de prismes mirage : il en faut {plan.PrismCost}, tu en as {prisms}.");
        }

        return null;
    }

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
        return Loc.T($"Outfit “{name}”: ", $"Ensemble « {name} » : ")
            + (one
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

    /// <summary>Où en est le regroupement.</summary>
    public static string Progress(PackJob job, ItemInfo items)
    {
        var name = items.Name(job.Plan.Outfit.SetItemId);
        var text = job.IsStoring
            ? Loc.T($"Packing “{name}”: storing the outfit…", $"Regroupement de « {name} » : rangement dans l'ensemble…")
            : Loc.T(
                $"Packing “{name}”: taking pieces out ({job.TakenOut}/{job.Plan.Pieces.Count})…",
                $"Regroupement de « {name} » : sortie des pièces ({job.TakenOut}/{job.Plan.Pieces.Count})…");
        return job.StopRequested ? text + Loc.T(" (stopping)", " (arrêt demandé)") : text;
    }

    /// <summary>Le bilan d'un regroupement, et s'il s'est bien passé.</summary>
    public static (string Text, bool Ok) Result(PackPlan plan, PackResult result, ItemInfo items)
    {
        var name = items.Name(plan.Outfit.SetItemId);
        var lines = new List<string>();
        if (result.StoredPieces > 0)
        {
            var freed = Freed(plan.IsNew ? result.StoredPieces - 1 : result.StoredPieces);
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

        var piece = result.ProblemItemId != 0 ? items.Name(result.ProblemItemId) : Loc.T("a piece", "une pièce");
        var problem = result.Problem switch
        {
            PackProblem.NothingToStore => Loc.T("Nothing to store: its pieces are no longer in the dresser.", "Rien à ranger : ses pièces ne sont plus dans la coiffeuse."),
            PackProblem.NotEnoughPrisms => Loc.T("Not enough Glamour Prisms (one per piece).", "Pas assez de prismes mirage (un par pièce)."),
            PackProblem.AlreadyStored => Loc.T("This outfit is already in the dresser.", "Cet ensemble est déjà dans la coiffeuse."),
            PackProblem.OutfitNotFound => Loc.T("The outfit is no longer in the dresser.", "L'ensemble n'est plus dans la coiffeuse."),
            PackProblem.DresserClosed => Loc.T("The Glamour Dresser was closed.", "La coiffeuse a été fermée."),
            PackProblem.RestoreRefused => Loc.T(
                $"The game refused to take {piece} out of the dresser (inventory full, or a unique item you already have).",
                $"Le jeu a refusé de sortir {piece} de la coiffeuse (inventaire plein, ou objet unique déjà possédé)."),
            PackProblem.Timeout => Loc.T("The game did not confirm in time.", "Le jeu n'a pas confirmé à temps.")
                + (result.ProblemItemId != 0 ? Loc.T($" Check where {piece} is.", $" Vérifie où est {piece}.") : string.Empty),
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
        if (problem != null)
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
}
