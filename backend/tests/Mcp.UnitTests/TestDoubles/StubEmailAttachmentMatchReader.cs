// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Attachments;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>A stand-in for the attachment match reader, answering with what a test arranged per message.</summary>
/// <remarks>
/// It matches nothing: which passages a query reaches and what a headline cuts belong to PostgreSQL. What it lets a
/// boundary test prove is that whatever the application found reaches the published result on the right message, in the
/// shape and under the bounds the contract states.
/// </remarks>
internal sealed class StubEmailAttachmentMatchReader : IEmailAttachmentMatchReader
{
    private readonly Dictionary<StoredEmailId, IReadOnlyList<EmailAttachmentMatch>> written = [];

    private readonly Dictionary<StoredEmailId, IReadOnlyList<EmailAttachmentMatch>> depicted = [];

    /// <summary>Arranges what a message's document attachments contributed.</summary>
    /// <param name="storedEmailId">The message the matches hang on.</param>
    /// <param name="matches">What its attachments contributed.</param>
    /// <returns>This stub, so arrangement reads as one statement.</returns>
    public StubEmailAttachmentMatchReader WithWritten(
        StoredEmailId storedEmailId,
        params EmailAttachmentMatch[] matches)
    {
        this.written[storedEmailId] = matches;

        return this;
    }

    /// <summary>Arranges what a message's described pictures contributed.</summary>
    /// <param name="storedEmailId">The message the matches hang on.</param>
    /// <param name="matches">What its pictures contributed.</param>
    /// <returns>This stub, so arrangement reads as one statement.</returns>
    public StubEmailAttachmentMatchReader WithDepicted(
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
        ArgumentNullException.ThrowIfNull(rankedEmailIds);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Found(this.written, rankedEmailIds));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadDepictedMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<StoredEmailId> depictedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(depictedEmailIds);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(Found(this.depicted, depictedEmailIds));
    }

    private static IReadOnlyList<StoredEmailAttachmentMatches> Found(
        Dictionary<StoredEmailId, IReadOnlyList<EmailAttachmentMatch>> arranged,
        IReadOnlyList<StoredEmailId> requested) =>
        [
            .. requested
                .Where(arranged.ContainsKey)
                .Select(storedEmailId => new StoredEmailAttachmentMatches(storedEmailId, arranged[storedEmailId])),
        ];
}
