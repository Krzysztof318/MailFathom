// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Stands in for this deployment's retrieval, answering per query and recording the scope each lookup arrived with.</summary>
/// <remarks>
/// A recorder rather than a substitute, because what the tests using it assert is which lookups ran and what scope
/// bounded them — an argument matcher would state that expectation in the assertion instead of reading what reached the
/// port.
/// </remarks>
internal sealed class ScriptedEmailKnowledgeSearch : IEmailKnowledgeSearch
{
    private readonly List<EmailKnowledgeQuery> lookups = [];
    private readonly Dictionary<string, IReadOnlyList<EmailKnowledgePassage>> passagesByQuery =
        new(StringComparer.Ordinal);

    private readonly HashSet<string> refusedQueries = new(StringComparer.Ordinal);

    private readonly Dictionary<string, CancellationTokenSource> stoppedQueries = new(StringComparer.Ordinal);

    private EmailSearchRetrievalMode retrievalMode = EmailSearchRetrievalMode.Hybrid;

    /// <summary>Gets the lookups that ran, in order.</summary>
    public IReadOnlyList<EmailKnowledgeQuery> Lookups => this.lookups;

    /// <summary>Gets the scope the last lookup was bounded by, or <see langword="null" /> when none ran.</summary>
    public MailboxScope? LastScope { get; private set; }

    /// <summary>Arranges what one query finds.</summary>
    /// <param name="queryText">The query to answer.</param>
    /// <param name="passages">What it finds.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    public ScriptedEmailKnowledgeSearch Returning(string queryText, params EmailKnowledgePassage[] passages)
    {
        this.passagesByQuery[queryText] = passages;

        return this;
    }

    /// <summary>Arranges a query the deployment refuses, as it does for a filter it cannot accept.</summary>
    /// <param name="queryText">The query to refuse.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    public ScriptedEmailKnowledgeSearch Refusing(string queryText)
    {
        this.refusedQueries.Add(queryText);

        return this;
    }

    /// <summary>Arranges a query during which somebody stops the run, which is where a cancellation actually reaches one.</summary>
    /// <param name="queryText">The query the stop arrives during.</param>
    /// <param name="stopping">The source the stop is signalled through.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    /// <remarks>
    /// Stopping from inside the lookup rather than before the run is what makes the claim a cancellation claim: the run
    /// is already executing, the retrieval is what it is waiting on, and the token it observes is the linked one the run
    /// composed rather than the one a test holds.
    /// </remarks>
    public ScriptedEmailKnowledgeSearch Stopping(string queryText, CancellationTokenSource stopping)
    {
        this.stoppedQueries[queryText] = stopping;

        return this;
    }

    /// <summary>Arranges how this retrieval reports it ranked.</summary>
    /// <param name="mode">The mode every lookup reports.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    public ScriptedEmailKnowledgeSearch RankingBy(EmailSearchRetrievalMode mode)
    {
        this.retrievalMode = mode;

        return this;
    }

    /// <summary>Builds one passage of its own message, so two extracts are two messages unless a test says otherwise.</summary>
    /// <param name="text">The extract itself.</param>
    /// <param name="storedEmailId">The stable identity, or <see langword="null" /> to generate one.</param>
    /// <returns>The passage.</returns>
    public static EmailKnowledgePassage Passage(string text, Guid? storedEmailId = null) => new()
    {
        StoredEmailId = StoredEmailId.Create(storedEmailId ?? Guid.CreateVersion7()),
        AccountId = MailAccountId.Create("primary"),
        FolderAlias = MailFolderAlias.Create("INBOX"),
        Subject = null,
        ReceivedAt = null,
        SenderVerification = SenderVerification.NotEstablished,
        MachineAuthorship = MachineAuthorshipAssessment.NotAssessed,
        Text = text,
    };

    /// <inheritdoc />
    public Task<EmailKnowledgeLookup> FindPassagesAsync(
        MailboxScope scope,
        EmailKnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        this.lookups.Add(query);
        this.LastScope = scope;

        if (this.stoppedQueries.TryGetValue(query.QueryText, out var stopping))
        {
            stopping.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (this.refusedQueries.Contains(query.QueryText))
        {
            throw MailboxQueryFilterInvalidException.NotAnAddress("sender address");
        }

        return Task.FromResult(EmailKnowledgeLookup.Unfiltered(
            this.passagesByQuery.GetValueOrDefault(query.QueryText, []),
            this.retrievalMode));
    }
}
