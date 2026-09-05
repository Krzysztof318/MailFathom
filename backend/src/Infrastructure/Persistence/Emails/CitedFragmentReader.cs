// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Citations;
using MailFathom.Application.Emails.Chunking;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Reads the passages a citation names out of PostgreSQL.</summary>
/// <remarks>
/// <para>
/// The message is part of the predicate rather than a check made afterwards, so a passage belonging to a different
/// message is never in the result set and never in this process. The rows are found by their primary key within one
/// message, so the cost follows the citations rather than the mailbox.
/// </para>
/// <para>
/// A citation whose passage was replaced by a later cut simply matches no row. That is the whole of how re-chunking
/// reaches this read: the writer replaces a changed message's rows and leaves an unchanged message's alone, so an
/// identifier that still matches names the same text it always did.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class CitedFragmentReader(MailFathomDbContext dbContext) : ICitedFragmentReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<EmailChunkId, CitedFragment>> ReadFragmentsAsync(
        StoredEmailId storedEmailId,
        IReadOnlyCollection<EmailChunkId> fragments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        if (fragments.Count is 0)
        {
            return new Dictionary<EmailChunkId, CitedFragment>();
        }

        var named = fragments.Select(static fragment => fragment.Value).ToArray();

        var rows = await dbContext.EmailChunks
            .AsNoTracking()
            .Where(chunk => chunk.StoredEmailId == storedEmailId.Value && named.Contains(chunk.Id))
            .Select(chunk => new CitedFragmentRow(chunk.Id, chunk.Ordinal, chunk.StartOffset, chunk.Text))
            .ToArrayAsync(cancellationToken);

        return rows.ToDictionary(
            static row => EmailChunkId.Create(row.Id),
            static row => new CitedFragment(
                EmailChunkId.Create(row.Id),
                row.Ordinal,
                row.StartOffset,
                row.StartOffset + row.Text.Length,
                row.Text));
    }
}
