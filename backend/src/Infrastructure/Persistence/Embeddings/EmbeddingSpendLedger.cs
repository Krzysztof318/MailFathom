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
    /// <summary>The user column's value on the row carrying the deployment's own total, which names nobody.</summary>
    private static readonly Guid DeploymentRow = Guid.Empty;

    /// <summary>Adds a period's spend, inserting the row the first time anything is charged to it.</summary>
    /// <remarks>
    /// One statement rather than a read and a write, because the two workers that spend do so in separate transactions
    /// and a read-modify-write would let each of them overwrite the other's increment with a total that was already
    /// stale when it was read. PostgreSQL's upsert makes the whole thing one atomic addition, and the column and table
    /// names come from the entity so the statement and the mapping cannot drift apart. The conflict target is the whole
    /// key: two users spending inside one period are two rows rather than one they would take turns replacing.
    /// </remarks>
    private const string RecordSpendStatement = $$"""
        INSERT INTO {{EmbeddingSpendPeriodEntity.TableName}}
            ("{{EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{EmbeddingSpendPeriodEntity.UserIdColumnName}}", "{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}")
        VALUES ({0}, {1}, {2})
        ON CONFLICT ("{{EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{EmbeddingSpendPeriodEntity.UserIdColumnName}}") DO UPDATE
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
        UserId user,
        CancellationToken cancellationToken)
    {
        var userId = user.Value;
        var deploymentRow = DeploymentRow;

        var totals = await dbContext.EmbeddingSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart)
            .GroupBy(_ => 1)
            .Select(rows => new EmbeddingSpendTotals(
                rows.Sum(period => period.UserId == userId ? period.ConsumedInputCharacterCount : 0L),
                rows.Sum(period => period.UserId == deploymentRow ? period.ConsumedInputCharacterCount : 0L)))
            .SingleOrDefaultAsync(cancellationToken);

        return totals ?? EmbeddingSpendTotals.Unspent;
    }

    /// <inheritdoc />
    public async Task<long> ReadDeploymentConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        CancellationToken cancellationToken)
    {
        var deploymentRow = DeploymentRow;

        return await dbContext.EmbeddingSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart && period.UserId == deploymentRow)
            .SumAsync(period => period.ConsumedInputCharacterCount, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        IReadOnlyCollection<UserId> users,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacterCount);

        if (inputCharacterCount == 0)
        {
            return;
        }

        var sessionDbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // The deployment's own row first, so what was sent is counted once whether the mailbox has one assigned user,
        // several, or none. A loop over the users is what charges each of them in full, which is the per-user ceiling
        // ADR 0014 asks for and is deliberately more than was sent.
        await IncrementAsync(sessionDbContext, periodStart, DeploymentRow, inputCharacterCount, cancellationToken);

        foreach (var user in users)
        {
            await IncrementAsync(sessionDbContext, periodStart, user.Value, inputCharacterCount, cancellationToken);
        }
    }

    /// <summary>Issues one upsert, which is the whole of what a charge is.</summary>
    private static Task<int> IncrementAsync(
        MailFathomDbContext sessionDbContext,
        DateTimeOffset periodStart,
        Guid userId,
        long inputCharacterCount,
        CancellationToken cancellationToken) =>

        // All three values are parameters rather than composed text; only the identifiers, which come from the entity's
        // own constants, are part of the statement.
        sessionDbContext.Database.ExecuteSqlRawAsync(
            RecordSpendStatement,
            [periodStart, userId, inputCharacterCount],
            cancellationToken);
}
