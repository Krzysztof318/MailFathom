// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>EF Core ledger of what each budget period has spent against an embedding provider, and for whom.</summary>
[RequiresIntegrationCoverage]
internal sealed class EmbeddingSpendLedger(MailFathomDbContext dbContext) : IEmbeddingSpendLedger
{
    /// <summary>Adds a period's spend, inserting the owner's row the first time anything is charged to it.</summary>
    /// <remarks>
    /// One statement rather than a read and a write, because the two workers that spend do so in separate transactions
    /// and a read-modify-write would let each of them overwrite the other's increment with a total that was already
    /// stale when it was read. PostgreSQL's upsert makes the whole thing one atomic addition, and the column and table
    /// names come from the entity so the statement and the mapping cannot drift apart. The conflict target is the whole
    /// key: two owners spending inside one period are two rows rather than one they would take turns replacing.
    /// </remarks>
    private const string RecordSpendStatement = $$"""
        INSERT INTO {{EmbeddingSpendPeriodEntity.TableName}}
            ("{{EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{EmbeddingSpendPeriodEntity.OwnerIdColumnName}}", "{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}")
        VALUES ({0}, {1}, {2})
        ON CONFLICT ("{{EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{EmbeddingSpendPeriodEntity.OwnerIdColumnName}}") DO UPDATE
        SET "{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}" =
            {{EmbeddingSpendPeriodEntity.TableName}}."{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}"
            + EXCLUDED."{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}"
        """;

    /// <inheritdoc />
    /// <remarks>
    /// Both totals are aggregated from the same set of rows in one round trip, which is what makes them describe one
    /// moment. A period nobody has spent in has no row at all, and the aggregation over nothing reads as zero rather
    /// than as an absence — which is what makes the first call of a new period ordinary.
    /// </remarks>
    public async Task<EmbeddingSpendTotals> ReadConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var ownerId = owner.Value;

        var totals = await dbContext.EmbeddingSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart)
            .GroupBy(_ => 1)
            .Select(rows => new EmbeddingSpendTotals(
                rows.Sum(period => period.OwnerId == ownerId ? period.ConsumedInputCharacterCount : 0L),
                rows.Sum(period => period.ConsumedInputCharacterCount)))
            .SingleOrDefaultAsync(cancellationToken);

        return totals ?? EmbeddingSpendTotals.Unspent;
    }

    /// <inheritdoc />
    public async Task<long> ReadDeploymentConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        CancellationToken cancellationToken) =>
        await dbContext.EmbeddingSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart)
            .SumAsync(period => period.ConsumedInputCharacterCount, cancellationToken);

    /// <inheritdoc />
    public async Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        MailOwnerId owner,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacterCount);

        if (inputCharacterCount == 0)
        {
            return;
        }

        var sessionDbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // All three values are parameters rather than composed text; only the identifiers, which come from the entity's
        // own constants, are part of the statement.
        await sessionDbContext.Database.ExecuteSqlRawAsync(
            RecordSpendStatement,
            [periodStart, owner.Value, inputCharacterCount],
            cancellationToken);
    }
}
