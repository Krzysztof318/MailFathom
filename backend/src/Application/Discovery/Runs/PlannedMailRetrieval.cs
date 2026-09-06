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

    /// <summary>Runs the plan's lookups in order until enough distinct passages have been found or the plan runs out.</summary>
    /// <param name="question">The question, whose scope bounds every lookup.</param>
    /// <param name="plan">The plan to run.</param>
    /// <param name="progress">Told how far the plan has got as each lookup settles, or <see langword="null" /> where nobody is watching.</param>
    /// <param name="cancellationToken">Cancels the retrieval between and during lookups.</param>
    /// <returns>What the run may answer from, with what it took to find it.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Every lookup the plan holds carried a filter this deployment refuses.</exception>
    /// <remarks>
    /// <para>
    /// One refused lookup is skipped rather than fatal: the filters are derived from a question by a model, and a
    /// question is not unanswerable because one of several wordings named an address that is not one. A plan whose every
    /// lookup was refused is a different thing — there is nothing left to answer from, and the caller is told why rather
    /// than handed an empty result that reads as a mailbox holding nothing.
    /// </para>
    /// <para>
    /// The progress callback is a plain delegate rather than an <see cref="IProgress{T}" />, and it is called on the
    /// thread the lookup finished on. That is the whole reason: the standard implementation posts each report to a
    /// synchronization context or to the thread pool, so two reports can be observed in the order they were scheduled
    /// rather than the order they were made — and a caller turning them into a numbered event stream would publish a
    /// plan that went backwards. A caller wanting them elsewhere hands over a delegate that puts them there.
    /// </para>
    /// </remarks>
    public async Task<DiscoveryEvidence> RetrieveAsync(
        MailQuestion question,
        RetrievalPlan plan,
        Action<DiscoveryRetrievalProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(plan);

        var found = new List<EmailKnowledgePassage>(plan.SufficientPassages);
        var alreadyFound = new HashSet<(Guid StoredEmailId, string Text)>();
        var retrievalMode = EmailSearchRetrievalMode.Lexical;
        var lookupsRun = 0;
        var lookupsRefused = 0;
        MailboxQueryFilterInvalidException? lastRefusal = null;

        foreach (var lookup in plan.Lookups)
        {
            EmailKnowledgeLookup retrieved;
            try
            {
                retrieved = await this.knowledgeSearch.FindPassagesAsync(question.Scope, lookup, cancellationToken);
            }
            catch (MailboxQueryFilterInvalidException refusal)
            {
                lastRefusal = refusal;
                lookupsRefused++;
                Report();

                continue;
            }

            // Taken from the first lookup that ran rather than from the last, because the mode describes how this
            // deployment ranks and every lookup of one run is therefore ranked the same way.
            if (lookupsRun is 0)
            {
                retrievalMode = retrieved.RetrievalMode;
            }

            lookupsRun++;

            // Cut to what the plan still calls for before the ledger is asked, so a lookup returning twenty passages
            // when two are wanted charges the run for two: the surplus was discarded a few lines below anyway, and
            // charging for it would spend the mailbox's character ceiling on mail nothing was ever going to read.
            List<EmailKnowledgePassage> candidates = [];
            foreach (var passage in retrieved.Passages)
            {
                if (found.Count + candidates.Count >= plan.SufficientPassages)
                {
                    break;
                }

                if (alreadyFound.Add((passage.StoredEmailId.Value, passage.Text)))
                {
                    candidates.Add(passage);
                }
            }

            found.AddRange(this.ledger.AdmitPassages(candidates));

            Report();

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

        void Report() => progress?.Invoke(new DiscoveryRetrievalProgress(
            lookupsRun,
            lookupsRefused,
            plan.Lookups.Count,
            Math.Min(found.Count, plan.SufficientPassages)));
    }
}
