// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;

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
/// </remarks>
public sealed class PlannedMailRetrieval
{
    private readonly IEmailKnowledgeSearch knowledgeSearch;

    /// <summary>Creates the retrieval a plan is run through.</summary>
    /// <param name="knowledgeSearch">The deployment's own ranked retrieval over the caller's mail.</param>
    public PlannedMailRetrieval(IEmailKnowledgeSearch knowledgeSearch)
    {
        ArgumentNullException.ThrowIfNull(knowledgeSearch);

        this.knowledgeSearch = knowledgeSearch;
    }

    /// <summary>Runs the plan's lookups in order until enough distinct passages have been found or the plan runs out.</summary>
    /// <param name="question">The question, whose scope bounds every lookup.</param>
    /// <param name="plan">The plan to run.</param>
    /// <param name="cancellationToken">Cancels the retrieval between and during lookups.</param>
    /// <returns>What the run may answer from, with what it took to find it.</returns>
    /// <exception cref="MailboxQueryFilterInvalidException">Every lookup the plan holds carried a filter this deployment refuses.</exception>
    /// <remarks>
    /// One refused lookup is skipped rather than fatal: the filters are derived from a question by a model, and a
    /// question is not unanswerable because one of several wordings named an address that is not one. A plan whose every
    /// lookup was refused is a different thing — there is nothing left to answer from, and the caller is told why rather
    /// than handed an empty result that reads as a mailbox holding nothing.
    /// </remarks>
    public async Task<DiscoveryEvidence> RetrieveAsync(
        MailQuestion question,
        RetrievalPlan plan,
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
                continue;
            }

            // Taken from the first lookup that ran rather than from the last, because the mode describes how this
            // deployment ranks and every lookup of one run is therefore ranked the same way.
            if (lookupsRun is 0)
            {
                retrievalMode = retrieved.RetrievalMode;
            }

            lookupsRun++;
            foreach (var passage in retrieved.Passages)
            {
                if (alreadyFound.Add((passage.StoredEmailId.Value, passage.Text)))
                {
                    found.Add(passage);
                }
            }

            if (found.Count >= plan.SufficientPassages)
            {
                break;
            }
        }

        if (lookupsRun is 0 && lastRefusal is not null)
        {
            throw lastRefusal;
        }

        return new DiscoveryEvidence(
            [.. found.Take(plan.SufficientPassages)],
            retrievalMode,
            lookupsRun,
            lookupsRefused);
    }
}
