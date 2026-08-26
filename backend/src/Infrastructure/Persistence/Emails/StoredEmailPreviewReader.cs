// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using MailFathom.Application.Emails.Summaries;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads the opening of the derived text of named emails out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The cut is PostgreSQL's. The projection asks for <see cref="EmailPreview.MaximumCharacters" /> characters of the
/// text column and never for the column, so a body is never in a result set that crosses this boundary and never in
/// this process at all — which is the same arrangement the search reader uses for its snippets and for the same
/// reason.
/// </para>
/// <para>
/// It reads the trimmed reading of the text rather than the untrimmed one beside it. Quoted history and a signature
/// block are what the first two hundred characters of a reply would otherwise be, and the trimmed column is the one
/// extraction wrote for exactly that distinction.
/// </para>
/// <para>
/// The rows are found by primary key over the identities one page returned, so the cost follows the page rather than
/// the mailbox, and the table holding the text stays out of the timeline query that chose those rows.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailPreviewReader(MailFathomDbContext dbContext) : IStoredEmailPreviewReader
{
    /// <summary>Projects one search document into the identity and the bounded opening a row shows.</summary>
    /// <remarks>
    /// Named rather than written inline so the generated command can be read by a test: what this claims — that the
    /// bound is applied by PostgreSQL — is invisible in a result that would look the same either way.
    /// </remarks>
    internal static Expression<Func<EmailSearchDocumentEntity, StoredEmailPreviewRow>> Projection { get; } =
        document => new StoredEmailPreviewRow(
            document.StoredEmailId,
            document.BodyText!.Substring(0, EmailPreview.MaximumCharacters));

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<StoredEmailId, string>> ReadPreviewsAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        if (storedEmailIds.Count is 0)
        {
            return new Dictionary<StoredEmailId, string>();
        }

        var rows = await PreviewsOf(dbContext, storedEmailIds).ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            static row => StoredEmailId.Create(row.StoredEmailId),
            static row => row.Preview);
    }

    /// <summary>Composes the one query a page's previews are read from.</summary>
    /// <param name="context">The scoped context the read runs on.</param>
    /// <param name="storedEmailIds">The emails whose previews are wanted.</param>
    /// <returns>The query, which PostgreSQL evaluates in full.</returns>
    /// <remarks>
    /// A document whose text is absent is left out here rather than mapped to nothing afterwards, so a page of messages
    /// none of which has been extracted costs one query returning no rows.
    /// </remarks>
    internal static IQueryable<StoredEmailPreviewRow> PreviewsOf(
        MailFathomDbContext context,
        IReadOnlyList<StoredEmailId> storedEmailIds)
    {
        var identities = storedEmailIds.Select(static storedEmailId => storedEmailId.Value).ToArray();

        return context.EmailSearchDocuments
            .AsNoTracking()
            .Where(document => identities.Contains(document.StoredEmailId) && document.BodyText != null)
            .Select(Projection);
    }
}
