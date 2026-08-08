// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>EF Core ledger of what each budget period has spent against an embedding provider.</summary>
[RequiresIntegrationCoverage]
internal sealed class EmbeddingSpendLedger(MailFathomDbContext dbContext) : IEmbeddingSpendLedger
{
    /// <summary>Adds a period's spend, inserting the period's row the first time anything is charged to it.</summary>
    /// <remarks>
    /// One statement rather than a read and a write, because the two workers that spend do so in separate transactions
    /// and a read-modify-write would let each of them overwrite the other's increment with a total that was already
    /// stale when it was read. PostgreSQL's upsert makes the whole thing one atomic addition, and the column and table
    /// names come from the entity so the statement and the mapping cannot drift apart.
    /// </remarks>
    private const string RecordSpendStatement = $$"""
        INSERT INTO {{EmbeddingSpendPeriodEntity.TableName}}
            ("{{EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}")
        VALUES ({0}, {1})
        ON CONFLICT ("{{EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName}}") DO UPDATE
        SET "{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}" =
            {{EmbeddingSpendPeriodEntity.TableName}}."{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}"
            + EXCLUDED."{{EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName}}"
        """;

    /// <inheritdoc />
    /// <remarks>
    /// A period nobody has spent in has no row, and reads as zero rather than as an absence. That is what makes the
    /// first call of a new period ordinary: nothing has to create a period before work may begin in it.
    /// </remarks>
    public async Task<long> ReadConsumedInputCharactersAsync(
        DateTimeOffset periodStart,
        CancellationToken cancellationToken) =>
        await dbContext.EmbeddingSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart)
            .Select(period => period.ConsumedInputCharacterCount)
            .SingleOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        long inputCharacterCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(inputCharacterCount);

        if (inputCharacterCount == 0)
        {
            return Task.CompletedTask;
        }

        var sessionDbContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // Both values are parameters rather than composed text; only the identifiers, which come from the entity's own
        // constants, are part of the statement.
        return sessionDbContext.Database.ExecuteSqlRawAsync(
            RecordSpendStatement,
            [periodStart, inputCharacterCount],
            cancellationToken);
    }
}
