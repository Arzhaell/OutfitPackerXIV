using System;
using System.Collections.Generic;
using System.Linq;

namespace OutfitPackerXIV;

/// <summary>Une action en cours dans la coiffeuse (regrouper un ensemble, enlever ses doublons), une seule à la fois.</summary>
public interface IDresserJob
{
    bool StopRequested { get; }

    bool IsFinished { get; }

    /// <summary>À appeler à chaque image du jeu : une étape au plus, après confirmation de la précédente.</summary>
    void Update(TimeSpan now);

    /// <summary>Ne plus commencer d'action ; celle en cours se termine.</summary>
    void Stop();

    /// <summary>Arrête tout de suite, sans autre action (erreur du jeu).</summary>
    void Abort(PackProblem problem);
}

/// <summary>Un exemplaire en trop à sortir de la coiffeuse, reconnu à sa qualité et à ses deux teintures.</summary>
/// <param name="Keep">Exemplaires identiques qui doivent rester dans la coiffeuse (recomptés avant chaque sortie).</param>
public readonly record struct RemovalCopy(uint ItemId, bool HighQuality, byte Stain0, byte Stain1, int Keep)
{
    /// <summary>Le numéro de la pièce dans la coiffeuse (avec sa qualité).</summary>
    public uint DresserItemId => ItemIds.WithQuality(ItemId, HighQuality);

    public bool Dyed => Stain0 != 0 || Stain1 != 0;

    public bool Matches(DresserSlot slot) => slot.ItemId == DresserItemId && slot.Stain0 == Stain0 && slot.Stain1 == Stain1;
}

/// <summary>Les exemplaires en trop des pièces d'un ensemble, à sortir de la coiffeuse vers l'inventaire.</summary>
public sealed class RemovePlan
{
    public RemovePlan(OutfitDefinition outfit, IReadOnlyList<RemovalCopy> copies)
    {
        Outfit = outfit;
        Copies = copies;
    }

    public OutfitDefinition Outfit { get; }

    public IReadOnlyList<RemovalCopy> Copies { get; }

    public bool HasWork => Copies.Count > 0;

    public static RemovePlan For(OutfitStatus status) => new(
        status.Outfit,
        status.Pieces.SelectMany(p => p.Extra.Select(c => new RemovalCopy(
            p.ItemId,
            c.HighQuality,
            c.Stain0,
            c.Stain1,
            p.Loose.Count(l => Same(l, c)) - p.Extra.Count(e => Same(e, c))))).ToArray());

    private static bool Same(LoosePiece a, LoosePiece b) => a.HighQuality == b.HighQuality && a.Stain0 == b.Stain0 && a.Stain1 == b.Stain1;
}

/// <param name="Problem">Ce qui a arrêté ou écourté l'opération, ou <see cref="PackProblem.None"/>.</param>
/// <param name="ProblemItemId">La pièce concernée par le problème, ou 0.</param>
/// <param name="Removed">Exemplaires sortis de la coiffeuse (ils sont dans l'inventaire).</param>
/// <param name="NoLongerInDresser">Exemplaires prévus qui n'étaient plus dans la coiffeuse.</param>
public sealed record RemoveResult(PackProblem Problem, uint ProblemItemId, int Removed, IReadOnlyList<uint> NoLongerInDresser);

/// <summary>
/// Sort de la coiffeuse les exemplaires en trop, un à la fois, en attendant que le jeu confirme chacun (case vidée, objet
/// arrivé dans les sacs ou l'arsenal). Ne jette rien : les objets restent dans l'inventaire.
/// </summary>
public sealed class RemoveJob : IDresserJob
{
    private enum Step
    {
        Start,
        Next,
        Wait,
    }

    private readonly IDresserGame game;
    private readonly List<uint> notFound = [];
    private Step step = Step.Start;
    private int next;
    private int removed;
    private int refusals;
    private TimeSpan readyAt;
    private TimeSpan waitStart;
    private TimeSpan? notLoadedSince;

    // Sortie en cours.
    private RemovalCopy pending;
    private int pendingCountBefore;
    private HashSet<(uint, int)> pendingBagBefore = [];

    public RemoveJob(IDresserGame game, RemovePlan plan)
    {
        this.game = game;
        Plan = plan;
    }

    public RemovePlan Plan { get; }

    public RemoveResult? Result { get; private set; }

    public bool IsFinished => Result != null;

    public bool StopRequested { get; private set; }

    /// <summary>Exemplaires déjà sortis.</summary>
    public int Removed => removed;

    public void Stop() => StopRequested = true;

    public void Abort(PackProblem problem)
    {
        if (Result == null)
            Finish(problem, step == Step.Wait ? pending.ItemId : 0);
    }

    public void Update(TimeSpan now)
    {
        if (Result != null)
            return;

        if (!game.IsDresserOpen)
        {
            Finish(PackProblem.DresserClosed, step == Step.Wait ? pending.ItemId : 0);
            return;
        }

        // Fenêtre ouverte mais contenu pas (encore) chargé : le jeu le redemande peut-être au serveur, on l'attend.
        var slots = game.ReadSlots();
        if (slots == null)
        {
            notLoadedSince ??= now;
            if (now - notLoadedSince > PackJob.StepTimeout)
                Finish(PackProblem.DresserClosed, step == Step.Wait ? pending.ItemId : 0);
            return;
        }

        notLoadedSince = null;
        switch (step)
        {
            case Step.Start:
                if (!Plan.HasWork)
                {
                    Finish(PackProblem.NothingToRemove);
                    return;
                }

                step = Step.Next;
                readyAt = now;
                break;
            case Step.Next:
                if (now >= readyAt)
                    RemoveNext(slots, now);
                break;
            case Step.Wait:
                Wait(slots, now);
                break;
        }
    }

    private void RemoveNext(IReadOnlyList<DresserSlot> slots, TimeSpan now)
    {
        if (next < Plan.Copies.Count && StopRequested)
        {
            Finish(PackProblem.Stopped);
            return;
        }

        if (next >= Plan.Copies.Count)
        {
            Finish(removed == 0 && notFound.Count > 0 ? PackProblem.NothingToRemove : PackProblem.None);
            return;
        }

        // Relu à chaque fois : les numéros de case peuvent avoir bougé, et des exemplaires avoir disparu entre-temps.
        // Ceux qui doivent rester ne sortent jamais.
        var copy = Plan.Copies[next++];
        var count = slots.Count(copy.Matches);
        if (count <= copy.Keep)
        {
            notFound.Add(copy.ItemId);
            return;
        }

        var index = slots.First(copy.Matches).Index;
        pendingCountBefore = count;
        pendingBagBefore = game.ReadBags().Where(b => IsCopy(b, copy)).Select(b => b.Position).ToHashSet();
        if (!game.Restore(index))
        {
            // Le jeu refuse parfois une sortie juste après la précédente, puis l'accepte : on réessaie avant d'abandonner.
            if (++refusals < PackJob.RestoreAttempts)
            {
                next--;
                readyAt = now + PackJob.RetryDelay;
                return;
            }

            // Toujours refusé : les suivants ne sortiraient sans doute pas non plus.
            Finish(PackJob.RefusalProblem(game), copy.ItemId);
            return;
        }

        refusals = 0;
        pending = copy;
        step = Step.Wait;
        waitStart = now;
    }

    private void Wait(IReadOnlyList<DresserSlot> slots, TimeSpan now)
    {
        if (slots.Count(pending.Matches) < pendingCountBefore
            && game.ReadBags().Any(b => IsCopy(b, pending) && !pendingBagBefore.Contains(b.Position)))
        {
            removed++;
            step = Step.Next;
            readyAt = now + PackJob.PauseBetweenActions;
            return;
        }

        if (now - waitStart > PackJob.StepTimeout)
            Finish(PackProblem.Timeout, pending.ItemId);
    }

    private void Finish(PackProblem problem, uint itemId = 0) =>
        Result = new RemoveResult(problem, itemId, removed, notFound.ToArray());

    private static bool IsCopy(BagItem item, RemovalCopy copy) => item.ItemId == copy.ItemId && item.HighQuality == copy.HighQuality;
}
