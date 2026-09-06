// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Attachments;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>An in-memory stand-in for the attachment match reader, holding what a test arranged per message.</summary>
/// <remarks>
/// It answers by identity rather than by matching anything. What a headline cuts and which passages a query reaches
/// belong to PostgreSQL, and a fake that reproduced either would test a reimplementation of it; what this proves is
/// everything around the read — that only the window is asked about, that a depicted result is asked about on the other
/// method, and that whatever comes back reaches the result on the message it belongs to.
/// </remarks>
internal sealed class InMemoryEmailAttachmentMatchIndex : IEmailAttachmentMatchReader
{
    private readonly Dictionary<StoredEmailId, IReadOnlyList<EmailAttachmentMatch>> written = [];

    private readonly Dictionary<StoredEmailId, IReadOnlyList<EmailAttachmentMatch>> depicted = [];

    private readonly List<IReadOnlyList<StoredEmailId>> writtenRequests = [];

    private readonly List<IReadOnlyList<StoredEmailId>> depictedRequests = [];

    /// <summary>Gets the windows the written read was asked about, in order.</summary>
    public IReadOnlyList<IReadOnlyList<StoredEmailId>> WrittenRequests => this.writtenRequests;

    /// <summary>Gets the windows the depicted read was asked about, in order.</summary>
    public IReadOnlyList<IReadOnlyList<StoredEmailId>> DepictedRequests => this.depictedRequests;

    /// <summary>Arranges what a message's document attachments contributed.</summary>
    /// <param name="storedEmailId">The message the matches hang on.</param>
    /// <param name="matches">What its attachments contributed.</param>
    /// <returns>This index, so arrangement reads as one statement.</returns>
    public InMemoryEmailAttachmentMatchIndex WithWritten(
        StoredEmailId storedEmailId,
        params EmailAttachmentMatch[] matches)
    {
        this.written[storedEmailId] = matches;

        return this;
    }

    /// <summary>Arranges what a message's described pictures contributed.</summary>
    /// <param name="storedEmailId">The message the matches hang on.</param>
    /// <param name="matches">What its pictures contributed.</param>
    /// <returns>This index, so arrangement reads as one statement.</returns>
    public InMemoryEmailAttachmentMatchIndex WithDepicted(
        StoredEmailId storedEmailId,
        params EmailAttachmentMatch[] matches)
    {
        this.depicted[storedEmailId] = matches;

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadWrittenMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<StoredEmailId> rankedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentNullException.ThrowIfNull(rankedEmailIds);
        cancellationToken.ThrowIfCancellationRequested();

        this.writtenRequests.Add(rankedEmailIds);

        return Task.FromResult(Found(this.written, rankedEmailIds));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadDepictedMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<StoredEmailId> depictedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentNullException.ThrowIfNull(depictedEmailIds);
        cancellationToken.ThrowIfCancellationRequested();

        this.depictedRequests.Add(depictedEmailIds);

        return Task.FromResult(Found(this.depicted, depictedEmailIds));
    }

    /// <summary>Answers for the messages the caller asked about and for no others.</summary>
    private static IReadOnlyList<StoredEmailAttachmentMatches> Found(
        Dictionary<StoredEmailId, IReadOnlyList<EmailAttachmentMatch>> arranged,
        IReadOnlyList<StoredEmailId> requested) =>
        [
            .. requested
                .Where(arranged.ContainsKey)
                .Select(storedEmailId => new StoredEmailAttachmentMatches(storedEmailId, arranged[storedEmailId])),
        ];
}
