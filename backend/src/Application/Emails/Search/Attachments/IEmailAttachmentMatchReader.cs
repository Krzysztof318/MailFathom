// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search.Attachments;

/// <summary>Reads what the attachments of an already ranked window contributed to it.</summary>
/// <remarks>
/// <para>
/// A port of its own beside <see cref="IEmailSearchIndexReader" /> and
/// <see cref="IEmailVectorSearchIndexReader" /> because it reads neither of the two things they rank: those decide
/// where a message sits, and this says which file inside it the query reached and where in that file. It runs over a
/// closed window rather than over everything eligible, so what it costs is bounded by what a caller is about to publish
/// rather than by what the mailbox holds.
/// </para>
/// <para>
/// It is read-only and joins no transaction, per
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>.
/// Its filters are re-applied for the reason the window read re-applies them: the statements read different snapshots,
/// and an attachment of a message that has left the request's scope must be absent rather than published.
/// </para>
/// <para>
/// The two reads are separate because the two kinds of match are found by different means and mean different things. A
/// document's own words are matched by the query's words, exactly as a body is; a description was never matched
/// lexically at all — <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// keeps it out of the lexical index — so what explains a depicted result is the description itself rather than an
/// extract cut around words it need not contain.
/// </para>
/// </remarks>
public interface IEmailAttachmentMatchReader
{
    /// <summary>Reads the passages of document attachments the query's own words reached, inside a ranked window.</summary>
    /// <param name="selection">The same filters the window was ranked under.</param>
    /// <param name="queryText">The validated free text the extracts are cut around.</param>
    /// <param name="snippetBounds">How many extracts one message may show, and how long each may be.</param>
    /// <param name="rankedEmailIds">The window to read, in any order.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per message whose attachments matched, each carrying at most <see cref="EmailSearchSnippetBounds.SnippetsPerEmail" /> passages.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <remarks>
    /// A message whose attachments matched nothing is absent rather than present and empty, and so is an attachment
    /// MailFathom could not read — encrypted, corrupt, unsupported, carrying no text layer, or excluded by format. There
    /// is no searched-and-empty state here: what the reason was lives on the attachment's own row, which is where an
    /// user asking why is answered.
    /// </remarks>
    Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadWrittenMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<StoredEmailId> rankedEmailIds,
        CancellationToken cancellationToken);

    /// <summary>Reads the descriptions of the pictures attached to messages a description alone put in a result.</summary>
    /// <param name="selection">The same filters the window was ranked under.</param>
    /// <param name="snippetBounds">How many passages one message may show.</param>
    /// <param name="depictedEmailIds">The messages a description alone placed, in any order.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per message whose pictures were described, each carrying at most <see cref="EmailSearchSnippetBounds.SnippetsPerEmail" /> passages.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The description is served whole rather than cut around anything, because it is not a body being sampled: it is
    /// the entire derived text and the only readable account of why the picture matched. It is bounded where it was
    /// composed, by the ceiling a description is written under, rather than here.
    /// </remarks>
    Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadDepictedMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<StoredEmailId> depictedEmailIds,
        CancellationToken cancellationToken);
}
