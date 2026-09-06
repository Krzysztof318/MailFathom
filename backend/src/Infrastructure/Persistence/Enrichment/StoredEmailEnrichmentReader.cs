// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Enrichment;

/// <summary>Reads what was derived about the emails one page names, out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// One query over the marks rather than one per row, and it reads no message body, no search document, and no raw MIME:
/// a mark and the identifiers of the passages behind it are what the derivation already wrote down.
/// </para>
/// <para>
/// A message whose derivation found nothing to say has a record and no marks, and it comes back as an enrichment with
/// an empty mark list — which is what lets a client tell it apart from a message still awaiting one, whose identity is
/// simply absent from the answer.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailEnrichmentReader(MailFathomDbContext dbContext) : IStoredEmailEnrichmentReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<StoredEmailId, EmailEnrichment>> ReadEnrichmentsAsync(
        IReadOnlyList<StoredEmailId> storedEmailIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(storedEmailIds);

        if (storedEmailIds.Count is 0)
        {
            return new Dictionary<StoredEmailId, EmailEnrichment>();
        }

        var named = storedEmailIds.Select(static storedEmailId => storedEmailId.Value).ToArray();

        var rows = await dbContext.EmailEnrichments
            .AsNoTracking()
            .Where(record => named.Contains(record.StoredEmailId))
            .Select(record => new EnrichmentRow(
                record.StoredEmailId,
                record.DerivedAt,
                record.Marks
                    .OrderBy(mark => mark.Aspect)
                    .Select(mark => new MarkRow(
                        mark.Aspect,
                        mark.Text,
                        mark.Reason,
                        mark.DueAt,
                        mark.Source,
                        mark.Origin,
                        mark.Evidence))
                    .ToList()))
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            static row => StoredEmailId.Create(row.StoredEmailId),
            static row => new EmailEnrichment(
                StoredEmailId.Create(row.StoredEmailId),
                [.. row.Marks.Select(ToMark).OfType<EmailEnrichmentMark>()],
                row.DerivedAt));
    }

    /// <summary>Reads one stored row back into the mark it was written from, or into nothing where it is no longer one.</summary>
    /// <remarks>
    /// The value object's own rules are re-applied rather than trusted, because a row is read back long after it was
    /// written and nothing between the two is under this code's control. A row that would break one of them is dropped
    /// rather than raised: a mark whose evidence has gone is exactly the claim nobody can check, and refusing it takes
    /// one row off a screen instead of failing the page every other row was going to be drawn on.
    /// </remarks>
    private static EmailEnrichmentMark? ToMark(MarkRow row)
    {
        IReadOnlyList<EmailChunkId> evidence =
        [
            .. row.Evidence
                .Where(static passage => passage != Guid.Empty)
                .Select(EmailChunkId.Create),
        ];

        if (evidence.Count is 0 || string.IsNullOrWhiteSpace(row.Text) || string.IsNullOrWhiteSpace(row.Reason))
        {
            return null;
        }

        return EmailEnrichmentMark.Create(
            row.Aspect,
            row.Text,
            row.Reason,
            evidence,
            EmailEnrichmentProvenance.Restore(row.Source, row.Origin),
            row.Aspect is EmailEnrichmentAspect.Commitment ? row.DueAt : null);
    }

    private sealed record EnrichmentRow(Guid StoredEmailId, DateTimeOffset DerivedAt, IReadOnlyList<MarkRow> Marks);

    private sealed record MarkRow(
        EmailEnrichmentAspect Aspect,
        string Text,
        string Reason,
        DateTimeOffset? DueAt,
        EmailEnrichmentSource Source,
        string Origin,
        IReadOnlyList<Guid> Evidence);
}
