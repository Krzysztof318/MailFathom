// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Search.Attachments;

/// <summary>The attachment half of a ranked window: what the files of its results contributed, and what a scanner reads before any of it is published.</summary>
/// <remarks>
/// <para>
/// One step both searches call rather than a procedure each of them keeps. The tool's window and the client's page are
/// the same question over the same mail and reach the same port; a copy of the written-and-depicted split in each would
/// be two places for
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// to hold, which is one place too many for it to hold in after the next change to either.
/// </para>
/// <para>
/// The egress point stays the caller's. What is scanned is the same in both — the extracts, and the name a sender chose
/// for the file — but a tool window and a client page cross the deployment at different points, and a shared step that
/// picked one of them would count a browser's page against a series no MCP caller ever made.
/// </para>
/// </remarks>
internal static class EmailAttachmentMatchWindow
{
    /// <summary>Reads what the window's attachments contributed and hangs it on the matches they belong to.</summary>
    /// <param name="reader">The port that reads which attachment of a result the query reached.</param>
    /// <param name="selection">The same filters the window was ranked under.</param>
    /// <param name="queryText">The validated free text the document extracts are cut around.</param>
    /// <param name="snippetBounds">How many extracts one message may show, and how long each may be.</param>
    /// <param name="matches">The window, as the ranking read it.</param>
    /// <param name="depictedOnly">The members of the window a description of a picture alone placed.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The window, each match carrying what its attachments contributed and whether a picture is its whole claim on the query.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Two reads because the two kinds of contribution are found by different means. A document's own words were reached
    /// by the query's words, so every result the written rankings placed is asked about; a description was never matched
    /// lexically at all, so only the results a description alone placed are, and what they show is the description
    /// itself.
    /// </para>
    /// <para>
    /// Sequential rather than concurrent, and after the window rather than beside it: both reads reach the same scoped
    /// EF Core context, which serves one operation at a time, so starting them together would fault instead of
    /// overlapping.
    /// </para>
    /// <para>
    /// A message the depicted read answered for keeps the extracts that read found and is marked as depicted, which is
    /// what says a picture is the whole of its claim on the query. A message the fused ranking carried is never marked,
    /// however many pictures are attached to it: its place was earned by something somebody wrote.
    /// </para>
    /// </remarks>
    public static async Task<IReadOnlyList<EmailSearchMatch>> ReadWindowMatchesAsync(
        this IEmailAttachmentMatchReader reader,
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<EmailSearchMatch> matches,
        IReadOnlySet<StoredEmailId> depictedOnly,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(depictedOnly);

        if (matches.Count is 0)
        {
            return matches;
        }

        StoredEmailId[] writtenIds =
        [
            .. matches
                .Select(static match => match.Summary.StoredEmailId)
                .Where(storedEmailId => !depictedOnly.Contains(storedEmailId)),
        ];

        StoredEmailId[] depictedIds =
        [
            .. matches
                .Select(static match => match.Summary.StoredEmailId)
                .Where(depictedOnly.Contains),
        ];

        var written = writtenIds.Length is 0
            ? []
            : await reader.ReadWrittenMatchesAsync(
                selection,
                queryText,
                snippetBounds,
                writtenIds,
                cancellationToken);

        var depicted = depictedIds.Length is 0
            ? []
            : await reader.ReadDepictedMatchesAsync(
                selection,
                snippetBounds,
                depictedIds,
                cancellationToken);

        var byEmail = written
            .Concat(depicted)
            .ToDictionary(
                static found => found.StoredEmailId,
                static found => found.Matches);

        return
        [
            .. matches.Select(match => match with
            {
                AttachmentMatches = byEmail.GetValueOrDefault(match.Summary.StoredEmailId, []),
                IsDepictedMatch = depictedOnly.Contains(match.Summary.StoredEmailId),
            }),
        ];
    }

    /// <summary>Scans what a result's attachments publish, which is their extracts and the names their senders chose.</summary>
    /// <param name="egressGuard">The scanner this deployment publishes through.</param>
    /// <param name="egressPoint">The point the caller's answer crosses the deployment at.</param>
    /// <param name="attachmentMatches">What the message's attachments contributed.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The matches, with everything a sender or a model composed scanned.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="egressGuard" /> or <paramref name="attachmentMatches" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The file name is scanned for the reason a sender's display name is: it is free text somebody wrote, and a
    /// document called after the person it concerns discloses as much as a sentence about them. The walk position and
    /// the declared media type are not scanned, being a coordinate a caller acts on and a parser's input rather than
    /// text to read, and a page number is neither.
    /// </remarks>
    public static async Task<IReadOnlyList<EmailAttachmentMatch>> GuardAttachmentMatchesAsync(
        this SensitiveContentEgressGuard egressGuard,
        SensitiveContentEgressPoint egressPoint,
        IReadOnlyList<EmailAttachmentMatch> attachmentMatches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(egressGuard);
        ArgumentNullException.ThrowIfNull(attachmentMatches);

        if (attachmentMatches.Count is 0)
        {
            return attachmentMatches;
        }

        var guarded = new List<EmailAttachmentMatch>(attachmentMatches.Count);

        foreach (var attachmentMatch in attachmentMatches)
        {
            guarded.Add(attachmentMatch with
            {
                FileName = await egressGuard.GuardOptionalAsync(
                    egressPoint,
                    attachmentMatch.FileName,
                    cancellationToken),
                Extracts = await egressGuard.GuardAllAsync(
                    egressPoint,
                    attachmentMatch.Extracts,
                    cancellationToken),
            });
        }

        return guarded;
    }
}
