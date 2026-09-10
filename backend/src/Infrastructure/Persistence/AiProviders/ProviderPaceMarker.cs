// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.AiProviders;

/// <summary>EF Core marker of when each paced workload of this deployment may send its next provider request.</summary>
[RequiresIntegrationCoverage]
internal sealed class ProviderPaceMarker(MailFathomDbContext dbContext) : IProviderPaceMarker
{
    /// <inheritdoc />
    public async Task<TimeSpan> ReserveNextSlotAsync(
        string workload,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(workload);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);

        // Materialized rather than composed: a terminal that narrows the source makes EF Core wrap the statement in a
        // subquery, and PostgreSQL accepts a data-modifying statement only at the top level. The upsert answers exactly
        // one row, granted or conflicting, so reading the array is the whole of it.
        var waits = await dbContext.Database
            .SqlQueryRaw<TimeSpan>(ReserveStatement(dbContext.Model), workload, interval)
            .ToArrayAsync(cancellationToken);

        return waits.Single();
    }

    /// <summary>The statement that moves one workload's marker forward and reports the slot it handed out.</summary>
    /// <remarks>
    /// <para>
    /// One statement rather than a read and a write, because every replica reserves against the same row and a
    /// read-modify-write would let each of them hand out a slot the others had already taken. The upsert's conflicting
    /// branch takes the row's lock and re-reads it after waiting for whoever held it, so concurrent reservations queue
    /// behind each other and come out one interval apart — which is what pacing is.
    /// </para>
    /// <para>
    /// The marker moves to one interval past whichever is later, its own value or now. Past its own value is what
    /// spaces a burst out; past now is what stops a workload that has been idle for an hour from being owed that hour's
    /// worth of slots at once.
    /// </para>
    /// <para>
    /// What is returned is the wait rather than the instant, and it is subtracted here where both readings are the
    /// database's: a replica whose clock sits a minute ahead would otherwise read its own reserved slot as already past
    /// and send immediately, which turns the rate into a burst without anything reporting a fault. It is floored at
    /// zero because a slot in the past is a slot that has arrived.
    /// </para>
    /// </remarks>
    private static string ReserveStatement(IModel model)
    {
        var marker = PersistedSchemaNames.EntityTypeOf<ProviderPaceMarkerEntity>(model);
        var table = PersistedSchemaNames.QuotedTable(marker);
        var workload = PersistedSchemaNames.QuotedColumn(marker, nameof(ProviderPaceMarkerEntity.Workload));
        var nextSlotAt = PersistedSchemaNames.QuotedColumn(marker, nameof(ProviderPaceMarkerEntity.NextSlotAt));

        return $$"""
            INSERT INTO {{table}} ({{workload}}, {{nextSlotAt}})
            VALUES ({0}, now() + {1})
            ON CONFLICT ({{workload}}) DO UPDATE
            SET {{nextSlotAt}} = GREATEST({{table}}.{{nextSlotAt}}, now()) + {1}
            RETURNING GREATEST({{nextSlotAt}} - {1} - now(), INTERVAL '0') AS "Value"
            """;
    }
}
