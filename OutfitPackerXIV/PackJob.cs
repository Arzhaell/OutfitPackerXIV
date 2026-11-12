using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitPackerXIV;

/// <summary>Ce que le regroupement lit et fait dans le jeu (sur le fil principal du jeu).</summary>
public interface IDresserGame
{
    /// <summary>La fenêtre de la coiffeuse est ouverte.</summary>
    bool IsDresserOpen { get; }

    /// <summary>Les cases de la coiffeuse, ou null si le jeu ne les a pas chargées.</summary>
    IReadOnlyList<DresserSlot>? ReadSlots();

    /// <summary>L'ensemble rangé dans cette case contient-il la pièce de cette colonne ?</summary>
    bool IsOutfitColumnStored(int index, int column);

    /// <summary>Les objets des sacs et de l'arsenal (pas l'équipement porté).</summary>
    IReadOnlyList<BagItem> ReadBags();

    /// <summary>Prismes mirage possédés.</summary>
    int GlamourPrisms { get; }

    /// <summary>Cases vides dans les quatre sacs de l'inventaire.</summary>
    int EmptyBagSlots { get; }

    /// <summary>
    /// Demande au jeu de sortir l'objet de cette case. Faux si le jeu refuse : coiffeuse pas prête, pas de place, objet
    /// unique déjà possédé.
    /// </summary>
    bool Restore(int index);

    /// <summary>Demande au jeu de ranger ces pièces dans un nouvel ensemble. Faux si le jeu refuse.</summary>
    bool StoreNew(uint dresserSetItemId, uint[] containers, ushort[] slots);

    /// <summary>Demande au jeu d'ajouter ces pièces à l'ensemble rangé dans cette case. Faux si le jeu refuse.</summary>
    bool StoreExisting(int index, uint[] containers, ushort[] slots);
}

public enum PackProblem
{
    None,

    /// <summary>Aucune pièce à ranger (ou plus aucune dans la coiffeuse).</summary>
    NothingToStore,

    /// <summary>Aucun exemplaire en trop à enlever (ou plus aucun dans la coiffeuse).</summary>
    NothingToRemove,

    NotEnoughPrisms,

    /// <summary>L'ensemble est déjà dans la coiffeuse : le jeu n'en accepte pas un second.</summary>
    AlreadyStored,

    /// <summary>L'ensemble à compléter n'est plus dans la coiffeuse.</summary>
    OutfitNotFound,

    /// <summary>Aucune case vide dans la coiffeuse pour un nouvel ensemble (et aucune pièce à en sortir).</summary>
    DresserFull,

    /// <summary>La coiffeuse s'est fermée (ou le jeu a oublié son contenu, au changement de zone).</summary>
    DresserClosed,

    /// <summary>Le jeu a refusé plusieurs fois de sortir une pièce alors que l'inventaire a de la place (objet unique…).</summary>
    RestoreRefused,

    /// <summary>Le jeu a refusé plusieurs fois de sortir une pièce, et l'inventaire est plein.</summary>
    InventoryFull,

    /// <summary>Le jeu n'a pas confirmé une action à temps.</summary>
    Timeout,

    /// <summary>Une pièce sortie n'est pas à 100 % : le jeu refuse de la ranger.</summary>
    NotFullCondition,

    /// <summary>Une pièce sortie n'est plus là où le jeu l'avait mise (déplacée entre-temps).</summary>
    PieceMoved,

    /// <summary>Le jeu a refusé de ranger les pièces dans l'ensemble.</summary>
    StoreRefused,

    /// <summary>Arrêté à la demande : les pièces déjà sorties ont été rangées.</summary>
    Stopped,

    /// <summary>Une fonction du jeu a échoué (jeu mis à jour sans le plugin).</summary>
    GameError,
}

/// <param name="Problem">Ce qui a arrêté ou écourté le regroupement, ou <see cref="PackProblem.None"/>.</param>
/// <param name="ProblemItemId">La pièce concernée par le problème, ou 0.</param>
/// <param name="StoredPieces">Pièces rangées dans l'ensemble.</param>
/// <param name="LeftInInventory">Pièces sorties de la coiffeuse et restées dans l'inventaire.</param>
/// <param name="NoLongerInDresser">Pièces prévues qui n'étaient plus dans la coiffeuse.</param>
/// <param name="StoredFromBags">Parmi les pièces rangées, celles qui étaient déjà dans les sacs ou l'arsenal.</param>
public sealed record PackResult(
    PackProblem Problem,
    uint ProblemItemId,
    int StoredPieces,
    IReadOnlyList<uint> LeftInInventory,
    IReadOnlyList<uint> NoLongerInDresser,
    int StoredFromBags = 0);

/// <summary>
/// Regroupe un ensemble, une action à la fois : sort chaque pièce de la coiffeuse en attendant que le jeu confirme
/// (case vidée, objet arrivé dans les sacs ou l'arsenal), puis range les pièces sorties dans l'ensemble. Des objets
/// qui étaient déjà dans l'inventaire, il ne range que ceux que le plan désigne (onglet Inventaire), à la place qu'ils
/// avaient. Appeler <see cref="Update"/> à chaque image du jeu.
/// </summary>
public sealed class PackJob : IDresserJob
{
    /// <summary>Délai laissé au jeu pour confirmer une action.</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Pause entre deux actions, pour ne pas presser le serveur.</summary>
    public static readonly TimeSpan PauseBetweenActions = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Essais avant d'abandonner une sortie refusée. Le jeu en refuse parfois une juste après l'action précédente, puis
    /// l'accepte (vu en jeu le 2026-11-11 : à chaque fois, un nouveau clic quelques secondes après a réussi).
    /// </summary>
    public const int RestoreAttempts = 3;

    /// <summary>Attente avant de réessayer une sortie refusée.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Pourquoi le jeu refuse encore une sortie après tous les essais.</summary>
    internal static PackProblem RefusalProblem(IDresserGame game) =>
        game.EmptyBagSlots == 0 ? PackProblem.InventoryFull : PackProblem.RestoreRefused;

    private enum Step
    {
        Start,
        NextRestore,
        WaitRestore,
        Store,
        WaitStore,
    }

    private readonly IDresserGame game;
    private readonly List<(PlannedPiece Piece, BagItem Item)> taken = [];
    private readonly List<uint> notFound = [];
    private Step step = Step.Start;
    private int nextPiece;
    private int refusals;
    private TimeSpan readyAt;
    private TimeSpan waitStart;
    private TimeSpan? notLoadedSince;

    // Premier problème qui n'arrête pas tout (pièce refusée, arrêt demandé…) : il figure dans le résultat.
    private PackProblem softProblem;
    private uint softProblemItemId;

    // Sortie en cours.
    private PlannedPiece pending;
    private int pendingCountBefore;
    private HashSet<(uint, int)> pendingBagBefore = [];

    // Rangement en cours.
    private List<(PlannedPiece Piece, BagItem Item)> storing = [];
    private int setCountBefore;

    public PackJob(IDresserGame game, PackPlan plan)
    {
        this.game = game;
        Plan = plan;
    }

    public PackPlan Plan { get; }

    /// <summary>Le résultat, une fois le regroupement fini.</summary>
    public PackResult? Result { get; private set; }

    public bool IsFinished => Result != null;

    public bool StopRequested { get; private set; }

    /// <summary>Les pièces sont rangées dans l'ensemble (dernière étape).</summary>
    public bool IsStoring => step is Step.Store or Step.WaitStore;

    /// <summary>Pièces déjà sorties de la coiffeuse.</summary>
    public int TakenOut => taken.Count;

    /// <summary>Ne plus sortir de pièce : celles déjà sorties sont rangées dans l'ensemble, puis le regroupement s'arrête.</summary>
    public void Stop() => StopRequested = true;

    /// <summary>Arrête tout de suite, sans autre action (erreur du jeu).</summary>
    public void Abort(PackProblem problem)
    {
        if (Result == null)
            Finish(problem, step == Step.WaitRestore ? pending.ItemId : 0);
    }

    public void Update(TimeSpan now)
    {
        if (Result != null)
            return;

        if (!game.IsDresserOpen)
        {
            Finish(PackProblem.DresserClosed, step == Step.WaitRestore ? pending.ItemId : 0);
            return;
        }

        // Fenêtre ouverte mais contenu pas (encore) chargé : le jeu le redemande peut-être au serveur, on l'attend.
        var slots = game.ReadSlots();
        if (slots == null)
        {
            notLoadedSince ??= now;
            if (now - notLoadedSince > StepTimeout)
                Finish(PackProblem.DresserClosed, step == Step.WaitRestore ? pending.ItemId : 0);
            return;
        }

        notLoadedSince = null;

        switch (step)
        {
            case Step.Start:
                Start(slots, now);
                break;
            case Step.NextRestore:
                if (now >= readyAt)
                    RestoreNext(slots, now);
                break;
            case Step.WaitRestore:
                WaitRestore(slots, now);
                break;
            case Step.Store:
                if (now >= readyAt)
                    Store(slots, now);
                break;
            case Step.WaitStore:
                WaitStore(slots, now);
                break;
        }
    }

    private void Start(IReadOnlyList<DresserSlot> slots, TimeSpan now)
    {
        if (!Plan.HasWork)
        {
            Finish(PackProblem.NothingToStore);
        }
        else if (game.GlamourPrisms < Plan.PrismCost)
        {
            Finish(PackProblem.NotEnoughPrisms);
        }
        else if (Plan.IsNew && slots.Any(s => s.ItemId != 0 && ItemIds.Base(s.ItemId) == Plan.Outfit.SetItemId))
        {
            Finish(PackProblem.AlreadyStored);
        }
        else if (!Plan.IsNew && IndexOf(slots, Plan.DresserSetItemId) < 0)
        {
            Finish(PackProblem.OutfitNotFound);
        }
        else if (Plan.IsNew && Plan.ToRestore.Count == 0 && slots.All(s => s.ItemId != 0))
        {
            // Aucune pièce ne sortira de la coiffeuse pour faire de la place au nouvel ensemble.
            Finish(PackProblem.DresserFull);
        }
        else
        {
            step = Step.NextRestore;
            readyAt = now;
        }
    }

    private void RestoreNext(IReadOnlyList<DresserSlot> slots, TimeSpan now)
    {
        if (nextPiece < Plan.ToRestore.Count && StopRequested)
        {
            Soft(PackProblem.Stopped, 0);
            nextPiece = Plan.ToRestore.Count;
        }

        if (nextPiece >= Plan.ToRestore.Count)
        {
            step = Step.Store;
            return;
        }

        var piece = Plan.ToRestore[nextPiece++];

        // Relu à chaque fois : les numéros de case de la coiffeuse peuvent avoir bougé depuis le calcul.
        var index = FindLoose(slots, piece);
        if (index < 0)
        {
            notFound.Add(piece.ItemId);
            return;
        }

        pendingCountBefore = Count(slots, piece.DresserItemId);
        pendingBagBefore = game.ReadBags().Where(b => IsCopyOf(b, piece)).Select(b => b.Position).ToHashSet();
        if (!game.Restore(index))
        {
            if (++refusals < RestoreAttempts)
            {
                nextPiece--;
                readyAt = now + RetryDelay;
                return;
            }

            // Toujours refusée : les pièces suivantes ne sortiraient sans doute pas non plus.
            Soft(RefusalProblem(game), piece.ItemId);
            nextPiece = Plan.ToRestore.Count;
            step = Step.Store;
            return;
        }

        refusals = 0;
        pending = piece;
        step = Step.WaitRestore;
        waitStart = now;
    }

    private void WaitRestore(IReadOnlyList<DresserSlot> slots, TimeSpan now)
    {
        if (Count(slots, pending.DresserItemId) < pendingCountBefore)
        {
            foreach (var item in game.ReadBags())
            {
                if (!IsCopyOf(item, pending) || pendingBagBefore.Contains(item.Position))
                    continue;

                taken.Add((pending, item));
                step = Step.NextRestore;
                readyAt = now + PauseBetweenActions;
                return;
            }
        }

        if (now - waitStart > StepTimeout)
            Finish(PackProblem.Timeout, pending.ItemId);
    }

    private void Store(IReadOnlyList<DresserSlot> slots, TimeSpan now)
    {
        if (taken.Count == 0 && Plan.FromBags.Count == 0)
        {
            Finish(softProblem == PackProblem.None ? PackProblem.NothingToStore : PackProblem.None);
            return;
        }

        // Les pièces doivent être encore là où le jeu les a mises (ou là où elles étaient dans l'inventaire), et à 100 % :
        // sinon il refuse de les ranger. Une pièce de l'inventaire remplacée entre-temps par un exemplaire serti ou
        // couvert d'un mirage n'est pas prise.
        var bags = game.ReadBags();
        var ready = new List<(PlannedPiece Piece, BagItem Item)>();
        foreach (var (piece, item) in taken.Concat(Plan.FromBags.Select(p => (p, p.Bag!.Value))))
        {
            var current = bags.Where(b => b.Position == item.Position && IsCopyOf(b, piece)
                && (piece.Bag == null || (!b.HasMateria && !b.Glamoured))).ToArray();
            if (current.Length == 0)
                Soft(PackProblem.PieceMoved, piece.ItemId);
            else if (current[0].Condition < BagItem.FullCondition)
                Soft(PackProblem.NotFullCondition, piece.ItemId);
            else
                ready.Add((piece, current[0]));
        }

        if (ready.Count == 0)
        {
            Finish(PackProblem.None);
            return;
        }

        if (game.GlamourPrisms < ready.Count)
        {
            Finish(PackProblem.NotEnoughPrisms);
            return;
        }

        ready.Sort((a, b) => a.Piece.Column.CompareTo(b.Piece.Column));
        var (containers, inventorySlots) = StoreArguments.Build(ready.Select(r => r.Item).ToArray());

        bool sent;
        if (Plan.IsNew)
        {
            setCountBefore = Count(slots, Plan.DresserSetItemId);
            sent = game.StoreNew(Plan.DresserSetItemId, containers, inventorySlots);
        }
        else
        {
            var index = IndexOf(slots, Plan.DresserSetItemId);
            if (index < 0)
            {
                Finish(PackProblem.OutfitNotFound);
                return;
            }

            sent = game.StoreExisting(index, containers, inventorySlots);
        }

        if (!sent)
        {
            Finish(PackProblem.StoreRefused);
            return;
        }

        storing = ready;
        step = Step.WaitStore;
        waitStart = now;
    }

    private void WaitStore(IReadOnlyList<DresserSlot> slots, TimeSpan now)
    {
        var bags = game.ReadBags();
        var leftBags = storing.All(s => !bags.Any(b => b.Position == s.Item.Position && IsCopyOf(b, s.Piece)));

        bool inOutfit;
        if (Plan.IsNew)
        {
            inOutfit = Count(slots, Plan.DresserSetItemId) > setCountBefore;
        }
        else
        {
            var index = IndexOf(slots, Plan.DresserSetItemId);
            inOutfit = index >= 0 && storing.All(s => game.IsOutfitColumnStored(index, s.Piece.Column));
        }

        if (leftBags && inOutfit)
            Finish(PackProblem.None, stored: storing.Count);
        else if (now - waitStart > StepTimeout)
            Finish(PackProblem.Timeout);
    }

    private void Soft(PackProblem problem, uint itemId)
    {
        if (softProblem != PackProblem.None)
            return;
        softProblem = problem;
        softProblemItemId = itemId;
    }

    // Un problème qui arrête tout passe avant un problème qui n'a fait qu'écourter le regroupement.
    private void Finish(PackProblem problem, uint itemId = 0, int stored = 0)
    {
        var storedIds = stored > 0 ? storing.Select(s => s.Piece.ItemId).ToHashSet() : [];
        var left = taken.Select(t => t.Piece.ItemId).Where(id => !storedIds.Contains(id)).ToArray();
        var fromBags = stored > 0 ? storing.Count(s => s.Piece.Bag != null) : 0;
        Result = problem != PackProblem.None
            ? new PackResult(problem, itemId, stored, left, notFound.ToArray(), fromBags)
            : new PackResult(softProblem, softProblemItemId, stored, left, notFound.ToArray(), fromBags);
    }

    private static bool IsCopyOf(BagItem item, PlannedPiece piece) => item.ItemId == piece.ItemId && item.HighQuality == piece.HighQuality;

    private static int Count(IReadOnlyList<DresserSlot> slots, uint dresserItemId) => slots.Count(s => s.ItemId == dresserItemId);

    private static int IndexOf(IReadOnlyList<DresserSlot> slots, uint dresserItemId)
    {
        foreach (var slot in slots)
        {
            if (slot.ItemId == dresserItemId)
                return slot.Index;
        }

        return -1;
    }

    // Un exemplaire non teint d'abord ; un exemplaire teint seulement si l'option le permet.
    private static int FindLoose(IReadOnlyList<DresserSlot> slots, PlannedPiece piece)
    {
        var dyed = -1;
        foreach (var slot in slots)
        {
            if (slot.ItemId != piece.DresserItemId)
                continue;
            if (slot.Stain0 == 0 && slot.Stain1 == 0)
                return slot.Index;
            if (dyed < 0 && piece.AllowDyed)
                dyed = slot.Index;
        }

        return dyed;
    }
}
