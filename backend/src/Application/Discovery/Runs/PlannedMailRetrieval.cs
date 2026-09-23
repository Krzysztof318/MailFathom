// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Application.Discovery.Runs;

/// <summary>Runs a retrieval plan's lookups against the mail the question's scope admits, and stops when it has enough.</summary>
/// <remarks>
/// <para>
/// It implements no retrieval of its own. Every lookup goes through <see cref="IEmailKnowledgeSearch" />, which is the
/// deployment's own lexical, semantic, or hybrid path, so whatever those paths have been taught to reach — an
/// attachment's own text, a description of a picture — is what a plan reaches, ranked as they rank it. Nothing here
/// re-derives or re-ranks around what they returned.
/// </para>
/// <para>
/// The scope is the question's and it bounds the reading rather than the result: it travels into each lookup and is
/// applied by the query itself, so a question about four selected messages reads four messages instead of reading the
/// mailbox and discarding the rest.
/// </para>
/// <para>
/// What a plan may draw out in total is the run's, not a lookup's. Each lookup's passages pass the run's own ledger,
/// which admits what still fits inside the characters one question may retrieve — the privacy ceiling, being the total
/// amount of somebody's mail that may leave the process to answer one question — and says so when a lookup found more
/// than that. Reaching it stops the plan rather than cutting the answer, and the run states it as a limitation instead
/// of quietly answering from less.
/// </para>
/// </remarks>
public sealed class PlannedMailRetrieval
{
    private readonly IEmailKnowledgeSearch knowledgeSearch;
    private readonly MailAnsweringRunLedger ledger;

    /// <summary>Creates the retrieval a plan is run through.</summary>
    /// <param name="knowledgeSearch">The deployment's own ranked retrieval over the caller's mail.</param>
    /// <param name="ledger">Counts what this run has drawn out of the mailbox, and admits only what still fits inside its ceiling.</param>
    public PlannedMailRetrieval(IEmailKnowledgeSearch knowledgeSearch, MailAnsweringRunLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(knowledgeSearch);
        ArgumentNullException.ThrowIfNull(ledger);

        this.knowledgeSearch = knowledgeSearch;
        this.ledger = ledger;
    }

    /// <summary>Runs the plan's lookups in order, giving each its share of the passages the plan calls enough.</summary>
    /// <param name="question">The question, whose scope bounds every lookup.</param>
    /// <param name="plan">The plan to run.</param>
    /// <param name="progress">Told how far the plan has got as each lookup settles, or <see langword="null" /> where nobody is watching.</param>
    /// <param name="cancellationToken">Cancels the retrieval between and during lookups.</param>
    /// <returns>What the run may answer from, with what it took to find it.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Every lookup the plan holds carried a filter this deployment refuses.</exception>
    /// <remarks>
    /// <para>
    /// Every lookup is admitted up to an equal share of <see cref="RetrievalPlan.SufficientPassages" /> first, and only
    /// what that leaves unspent goes to the passages a lookup found beyond its share, taken rank by rank across the
    /// lookups. So a later lookup's best passage is never crowded out by an earlier lookup's lower-ranked ones: a plan
    /// lists several short wordings because it cannot know which one reaches the evidence, and the one that does is as
    /// often the last as the first. Where the plan calls for fewer passages than it holds lookups, each lookup's share is
    /// one and the lookups past that many are not run, since nothing they found could be admitted.
    /// </para>
    /// <para>
    /// One refused lookup is skipped rather than fatal: the filters are derived from a question by a model, and a
    /// question is not unanswerable because one of several wordings named an address that is not one. A plan whose every
    /// lookup was refused is a different thing — there is nothing left to answer from, and the caller is told why rather
    /// than handed an empty result that reads as a mailbox holding nothing.
    /// </para>
    /// <para>
    /// The progress callback is a plain delegate rather than an <see cref="IProgress{T}" />, and each report is awaited
    /// before the next lookup is issued. That is the whole reason: the standard implementation posts each report to a
    /// synchronization context or to the thread pool, so two reports can be observed in the order they were scheduled
    /// rather than the order they were made — and a caller turning them into a numbered sequence would write a plan
    /// that went backwards. Awaiting is what extends that property to a caller whose report is a write: two writes
    /// never overlap, so the run stays the one writer of its own record. A caller wanting them elsewhere hands over a
    /// delegate that puts them there.
    /// </para>
    /// </remarks>
    public async Task<DiscoveryEvidence> RetrieveAsync(
        MailQuestion question,
        RetrievalPlan plan,
        Func<DiscoveryRetrievalProgress, Task>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(plan);

        var found = new List<EmailKnowledgePassage>(plan.SufficientPassages);
        var alreadyFound = new HashSet<(Guid StoredEmailId, string Text)>();
        var beyondShare = new List<IReadOnlyList<EmailKnowledgePassage>>(plan.Lookups.Count);
        var share = Math.Max(1, plan.SufficientPassages / plan.Lookups.Count);
        var retrievalMode = EmailSearchRetrievalMode.Lexical;
        var lookupsRun = 0;
        var lookupsRefused = 0;
        MailboxQueryFilterInvalidException? lastRefusal = null;

        foreach (var lookup in plan.Lookups)
        {
            EmailKnowledgeLookup? retrieved = null;
            try
            {
                retrieved = await this.knowledgeSearch.FindPassagesAsync(question.Scope, lookup, cancellationToken);
            }
            catch (MailboxQueryFilterInvalidException refusal)
            {
                lastRefusal = refusal;
                lookupsRefused++;
            }

            if (retrieved is not null)
            {
                // Taken from the first lookup that ran rather than from the last, because the mode describes how this
                // deployment ranks and every lookup of one run is therefore ranked the same way.
                if (lookupsRun is 0)
                {
                    retrievalMode = retrieved.RetrievalMode;
                }

                lookupsRun++;

                // Cut to the lookup's share before the ledger is asked, so a lookup returning twenty passages charges
                // the run for its share alone: what it found beyond that is held back uncharged, and reaches the ledger
                // only if the rest of the plan leaves room for it.
                var newlyFound = NotYetFound(retrieved.Passages, alreadyFound);
                var admissible = Math.Min(newlyFound.Count, Math.Min(share, plan.SufficientPassages - found.Count));

                beyondShare.Add(newlyFound[admissible..]);
                found.AddRange(Admit(newlyFound[..admissible]));
            }

            if (lookupsRun + lookupsRefused == plan.Lookups.Count && !this.ledger.RetrievalWasTruncated)
            {
                found.AddRange(Admit(RankByRank(beyondShare, alreadyFound, plan.SufficientPassages - found.Count)));
            }

            await ReportAsync();

            if (found.Count >= plan.SufficientPassages || this.ledger.RetrievalWasTruncated)
            {
                // A ceiling that is reached stops the plan rather than cutting one lookup: nothing a later lookup found
                // would fit either, so issuing it would read more of somebody's mail out of the database to discard it.
                break;
            }
        }

        if (lookupsRun is 0 && lastRefusal is not null)
        {
            throw lastRefusal;
        }

        return new DiscoveryEvidence(
            found,
            retrievalMode,
            lookupsRun,
            lookupsRefused,
            this.ledger.RetrievalWasTruncated);

        Task ReportAsync() => progress?.Invoke(new DiscoveryRetrievalProgress(
            lookupsRun,
            lookupsRefused,
            plan.Lookups.Count,
            found.Count)) ?? Task.CompletedTask;

        IReadOnlyList<EmailKnowledgePassage> Admit(IReadOnlyList<EmailKnowledgePassage> passages)
        {
            var admitted = this.ledger.AdmitPassages(passages);
            alreadyFound.UnionWith(admitted.Select(IdentityOf));

            return admitted;
        }
    }

    private static List<EmailKnowledgePassage> NotYetFound(
        IReadOnlyList<EmailKnowledgePassage> passages,
        HashSet<(Guid StoredEmailId, string Text)> alreadyFound) =>
        [.. passages.Where(passage => !alreadyFound.Contains(IdentityOf(passage))).DistinctBy(IdentityOf)];

    /// <summary>Takes what the lookups found beyond their shares, every lookup's first before any lookup's second.</summary>
    /// <remarks>The sort is stable, so within one rank the plan's own order decides.</remarks>
    private static List<EmailKnowledgePassage> RankByRank(
        List<IReadOnlyList<EmailKnowledgePassage>> beyondShare,
        HashSet<(Guid StoredEmailId, string Text)> alreadyFound,
        int wanted) =>
    [
        .. beyondShare
            .SelectMany(passages => passages.Select((passage, rank) => (Passage: passage, Rank: rank)))
            .OrderBy(ranked => ranked.Rank)
            .Select(ranked => ranked.Passage)
            .Where(passage => !alreadyFound.Contains(IdentityOf(passage)))
            .DistinctBy(IdentityOf)
            .Take(wanted),
    ];

    /// <summary>What makes two passages one: the same extract of the same message, however many lookups reached it.</summary>
    private static (Guid StoredEmailId, string Text) IdentityOf(EmailKnowledgePassage passage) =>
        (passage.StoredEmailId.Value, passage.Text);
}
