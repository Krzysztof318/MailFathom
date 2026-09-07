// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Attachments;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Connections;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads which attachment of a ranked message the query reached, and where inside the file that was.</summary>
/// <remarks>
/// <para>
/// The passage is the unit rather than the attachment, because the passage is what carries a coordinate: an offset into
/// the attachment's own text, which the boundaries stored beside that text turn into the page, slide, or sheet a
/// citation opens. Reporting the attachment alone would name a two-hundred-page report and leave a reader to find the
/// clause in it.
/// </para>
/// <para>
/// The lexical read narrows twice and scans once. The indexed vector on the attachment row decides which files the
/// query reached at all, which is an index lookup; only the passages of those files are then matched, which is what
/// keeps a per-passage match from being a scan of every passage a window's messages hold. The passages of one attachment
/// are bounded by the ceiling its text was read under, and the window is bounded by what the caller is about to
/// publish, so both ends of the cost are already closed.
/// </para>
/// <para>
/// A description is read by identity rather than by matching: it never entered the lexical index, so there is nothing to
/// cut an extract around, and
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// says what a depicted result shows instead — the description itself, whole, being the only readable account of why the
/// picture matched.
/// </para>
/// <para>
/// Every extract this returns is mail content, and a description is a machine's account of mail content. Nothing here
/// writes one to a log, a metric, a trace, or an error message, and the file name travels on the same terms: it is text
/// a sender chose.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailAttachmentMatchReader(
    MailFathomDbContext dbContext,
    PostgresTextSearchConfiguration textSearchConfiguration) : IEmailAttachmentMatchReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadWrittenMatchesAsync(
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

        if (rankedEmailIds.Count is 0)
        {
            return [];
        }

        var rows = await this.MatchedPassagesQuery(selection, queryText, snippetBounds, rankedEmailIds)
            .ToArrayAsync(cancellationToken);

        return Composed(rows, snippetBounds, extractsFromHeadline: true);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadDepictedMatchesAsync(
        MailboxEmailSelection selection,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<StoredEmailId> depictedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(snippetBounds);
        ArgumentNullException.ThrowIfNull(depictedEmailIds);

        if (depictedEmailIds.Count is 0)
        {
            return [];
        }

        var rows = await this.DescribedPassagesQuery(selection, snippetBounds, depictedEmailIds)
            .ToArrayAsync(cancellationToken);

        return Composed(rows, snippetBounds, extractsFromHeadline: false);
    }

    /// <summary>Composes the query that cuts an extract out of every passage of a matching document attachment.</summary>
    /// <param name="selection">The validated structural filters the window was ranked under.</param>
    /// <param name="queryText">The validated free text the extracts are cut around.</param>
    /// <param name="snippetBounds">How many extracts one result may carry, and how long each may be.</param>
    /// <param name="rankedEmailIds">The window's identities.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// Exposed for the reason the two ranking queries are: that the query text reaches both <c>websearch_to_tsquery</c>
    /// and <c>ts_headline</c> as a parameter, and that the file-level index decides which passages are matched at all,
    /// are claims about the generated command rather than anything observable from the application side.
    /// </remarks>
    internal IQueryable<AttachmentPassageMatchRow> MatchedPassagesQuery(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<StoredEmailId> rankedEmailIds)
    {
        var configuration = textSearchConfiguration.Value;
        var text = queryText.Value;
        var headlineOptions = SearchHeadlineText.Options(snippetBounds);
        var identities = Identities(rankedEmailIds);

        // The message rather than the passage carries the filter, because the selection is a predicate over mail: a
        // passage of a message that has left the request's scope since the ranking is absent rather than published.
        var eligible = StoredEmailSelectionPredicate
            .Matching(dbContext.StoredEmails.AsNoTracking(), selection)
            .Where(email => identities.Contains(email.Id));

        var matched = from email in eligible
                      from attachment in email.AttachmentTexts
                      where attachment.SearchVector != null
                          && attachment.SearchVector!.Matches(
                              EF.Functions.WebSearchToTsQuery(configuration, text))
                      from chunk in email.Chunks
                      where chunk.AttachmentPosition == attachment.AttachmentPosition
                          && EF.Functions.ToTsVector(configuration, chunk.Text)
                              .Matches(EF.Functions.WebSearchToTsQuery(configuration, text))
                      orderby email.Id, attachment.AttachmentPosition, chunk.Ordinal
                      select new AttachmentPassageMatchRow(
                          email.Id,
                          attachment.AttachmentPosition,
                          chunk.Ordinal,
                          chunk.StartOffset,
                          attachment.FileName,
                          attachment.DeclaredMediaType,
                          attachment.Kind,
                          attachment.Segments,
                          EF.Functions.WebSearchToTsQuery(configuration, text)
                              .GetResultHeadline(configuration, chunk.Text, headlineOptions));

        return matched.Take(PassageBound(snippetBounds, rankedEmailIds.Count));
    }

    /// <summary>Composes the query that reads the described passages of the pictures attached to a set of messages.</summary>
    /// <param name="selection">The validated structural filters the window was ranked under.</param>
    /// <param name="snippetBounds">How many passages one result may carry, which bounds what the statement returns.</param>
    /// <param name="depictedEmailIds">The messages a description alone placed.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// Exposed for the reason the matched-passage query is: that a description is selected by the kind recorded on its
    /// attachment row and never by matching a caller's words against it is a claim about the generated command, and it
    /// is the claim ADR 0030's floor rests on.
    /// </remarks>
    internal IQueryable<AttachmentPassageMatchRow> DescribedPassagesQuery(
        MailboxEmailSelection selection,
        EmailSearchSnippetBounds snippetBounds,
        IReadOnlyList<StoredEmailId> depictedEmailIds)
    {
        var identities = Identities(depictedEmailIds);

        var eligible = StoredEmailSelectionPredicate
            .Matching(dbContext.StoredEmails.AsNoTracking(), selection)
            .Where(email => identities.Contains(email.Id));

        var described = from email in eligible
                        from attachment in email.AttachmentTexts
                        where attachment.Kind == AttachmentTextKind.ImageDescription
                        from chunk in email.Chunks
                        where chunk.AttachmentPosition == attachment.AttachmentPosition
                        orderby email.Id, attachment.AttachmentPosition, chunk.Ordinal
                        select new AttachmentPassageMatchRow(
                            email.Id,
                            attachment.AttachmentPosition,
                            chunk.Ordinal,
                            chunk.StartOffset,
                            attachment.FileName,
                            attachment.DeclaredMediaType,
                            attachment.Kind,
                            attachment.Segments,
                            chunk.Text);

        return described.Take(PassageBound(snippetBounds, depictedEmailIds.Count));
    }

    private static Guid[] Identities(IReadOnlyList<StoredEmailId> emailIds) =>
        [.. emailIds.Select(static emailId => emailId.Value)];

    /// <summary>The greatest number of passage rows one read may return, across the whole window.</summary>
    /// <remarks>
    /// A statement bound rather than a per-message one, because PostgreSQL has no per-group limit an ordinary query can
    /// express and a correlated one would be a lateral join per attachment of every result. What it buys is the whole of
    /// the cost control: the window is already closed and each message publishes at most its own share, so the only
    /// thing the shape gives up is fairness in a mailbox where one message carries more matching passages than the whole
    /// window's share — that message keeps its own extracts and a later one in the same window loses its attachment
    /// extracts while keeping its place, its rank, and its own snippets. Ordering by the message first is what confines
    /// that to the tail rather than spreading it.
    /// </remarks>
    private static int PassageBound(EmailSearchSnippetBounds snippetBounds, int emailCount) =>
        snippetBounds.SnippetsPerEmail * emailCount;

    /// <summary>Groups the rows by message and turns each one's offset into the place it was read from.</summary>
    /// <remarks>
    /// The bound is applied per message rather than to the whole read, so one message carrying a thousand matching
    /// passages cannot take the places of the others in the window. It is applied here as well as in the query for the
    /// reason every extract bound is applied twice: it is the control on how much of a mailbox one call draws out, and a
    /// result must not depend on the server having honored an option list.
    /// </remarks>
    private static IReadOnlyList<StoredEmailAttachmentMatches> Composed(
        IReadOnlyList<AttachmentPassageMatchRow> rows,
        EmailSearchSnippetBounds snippetBounds,
        bool extractsFromHeadline) =>
        [
            .. rows
                .GroupBy(static row => row.StoredEmailId)
                .Select(group => new StoredEmailAttachmentMatches(
                    StoredEmailId.Create(group.Key),
                    [
                        .. group
                            .Select(row => Match(row, snippetBounds, extractsFromHeadline))
                            .Where(static match => match.Extracts.Count is not 0)
                            .Take(snippetBounds.SnippetsPerEmail),
                    ]))
                .Where(static matches => matches.Matches.Count is not 0),
        ];

    /// <summary>Reads one row into the match a result publishes.</summary>
    /// <remarks>
    /// A described passage is published whole and a document's is published as the fragments the query matched inside
    /// it. The difference is not a formatting choice: a description is the entire derived text rather than a body being
    /// sampled, and it carries none of the query's words by construction, so cutting it around them would leave nothing.
    /// </remarks>
    private static EmailAttachmentMatch Match(
        AttachmentPassageMatchRow row,
        EmailSearchSnippetBounds snippetBounds,
        bool extractsFromHeadline) => new(
        row.AttachmentPosition,
        row.FileName,
        row.DeclaredMediaType,
        row.Kind,
        AttachmentTextSegment.At(DeserializeSegments(row.Segments), row.StartOffset),
        extractsFromHeadline
            ? SearchHeadlineText.Extracts(row.Text, snippetBounds)
            : Described(row.Text));

    /// <summary>Publishes a description as the one extract it is, or as nothing where the row carried no text.</summary>
    private static IReadOnlyList<string> Described(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : [text];

    /// <summary>Reads back the boundary document one attachment's row stores.</summary>
    /// <remarks>
    /// A row written before this feature stored boundaries carries none, which is not an error: the passage is still a
    /// passage, and a match without a page is what an honest reading of it supports.
    /// </remarks>
    private static IReadOnlyList<AttachmentTextSegment> DeserializeSegments(string? document) => document is null
        ? []
        : JsonSerializer.Deserialize(
            document,
            AttachmentTextSegmentJsonContext.Default.IReadOnlyListAttachmentTextSegment) ?? [];

    /// <summary>One matching passage of one attachment, as the projection returns it.</summary>
    /// <param name="StoredEmailId">The message the attachment belongs to.</param>
    /// <param name="AttachmentPosition">The attachment's walk position within that message.</param>
    /// <param name="Ordinal">The passage's place in the attachment's own text.</param>
    /// <param name="StartOffset">Where the passage begins in that text, which is what the boundaries are read against.</param>
    /// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
    /// <param name="DeclaredMediaType">What the part declared itself to be.</param>
    /// <param name="Kind">Whether the words are the file's own or a model's account of a picture.</param>
    /// <param name="Segments">The stored boundary document, or <see langword="null" /> where the reading recorded none.</param>
    /// <param name="Text">The extract, which is a headline for a document and the description itself for a picture.</param>
    internal sealed record AttachmentPassageMatchRow(
        Guid StoredEmailId,
        int AttachmentPosition,
        int Ordinal,
        int StartOffset,
        string? FileName,
        string DeclaredMediaType,
        AttachmentTextKind Kind,
        string? Segments,
        string? Text);
}
