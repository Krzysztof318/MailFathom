// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Answering;

/// <summary>EF Core ledger of what each answering period of this deployment admitted and what it cost.</summary>
/// <remarks>
/// Both writes are one upsert against the period's row. PostgreSQL takes that row's lock on the conflicting branch and
/// re-reads it after waiting for whoever held it, so an admission decides against what every replica has already
/// admitted and a spend adds to what every replica has already spent — neither of which a read followed by a write
/// could promise. The column and table names come from the entity, so the statements and the mapping cannot drift
/// apart.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailAnsweringSpendPeriodStore(MailFathomDbContext dbContext) : IMailAnsweringSpendPeriodStore
{
    /// <inheritdoc />
    public async Task<int> TryAdmitRunAsync(
        DateTimeOffset periodStart,
        int maximumRuns,
        long maximumTokens,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRuns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumTokens, 1);

        // A refused admission updates nothing and therefore returns no row, which is read as zero — a figure a granted
        // admission can never produce, since the run it just counted is in it. Materialized rather than composed: a
        // terminal that narrows the source makes EF Core wrap the statement in a subquery, and PostgreSQL accepts a
        // data-modifying statement only at the top level.
        var admittedRuns = await dbContext.Database
            .SqlQueryRaw<int>(AdmitStatement(dbContext.Model), periodStart, maximumRuns, maximumTokens)
            .ToArrayAsync(cancellationToken);

        return admittedRuns.SingleOrDefault();
    }

    /// <inheritdoc />
    public async Task<long> RecordSpendAsync(
        DateTimeOffset periodStart,
        long tokenCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokenCount);

        // Materialized rather than composed, for the reason the admission above is. The upsert answers exactly one row,
        // inserting or conflicting, because a spend is never refused.
        var consumedTokens = await dbContext.Database
            .SqlQueryRaw<long>(RecordSpendStatement(dbContext.Model), periodStart, tokenCount)
            .ToArrayAsync(cancellationToken);

        return consumedTokens.Single();
    }

    /// <summary>The statement that admits one run against both of the period's ceilings, or refuses it.</summary>
    /// <remarks>
    /// The inserting branch carries no condition because a period nothing has spent in admits by definition, and the
    /// bounds refuse a ceiling below one before they are ever built. The conflicting branch is what both ceilings are
    /// asked of, and it is asked of the row as PostgreSQL re-read it rather than of anything this process held.
    /// </remarks>
    private static string AdmitStatement(IModel model)
    {
        var names = MailAnsweringSpendPeriodNames.Of(model);

        return $$"""
            INSERT INTO {{names.Periods}} ({{names.PeriodStartsAtColumn}}, {{names.AdmittedRunCountColumn}}, {{names.ConsumedTokenCountColumn}})
            VALUES ({0}, 1, 0)
            ON CONFLICT ({{names.PeriodStartsAtColumn}}) DO UPDATE
            SET {{names.AdmittedRunCountColumn}} = {{names.Periods}}.{{names.AdmittedRunCountColumn}} + 1
            WHERE {{names.Periods}}.{{names.AdmittedRunCountColumn}} < {1}
              AND {{names.Periods}}.{{names.ConsumedTokenCountColumn}} < {2}
            RETURNING {{names.AdmittedRunCountColumn}} AS "Value"
            """;
    }

    /// <summary>The statement that adds one run's tokens to the period they were spent in.</summary>
    /// <remarks>
    /// Unconditional, because what has already been spent cannot be refused: the ceiling is applied when a run is
    /// admitted, and a run the deployment has already paid for is recorded whether or not it took the period past its
    /// ceiling. A period whose first write is a spend rather than an admission is the shape a run admitted just before
    /// a roll-over leaves, and it inserts its row like any other.
    /// </remarks>
    private static string RecordSpendStatement(IModel model)
    {
        var names = MailAnsweringSpendPeriodNames.Of(model);

        return $$"""
            INSERT INTO {{names.Periods}} ({{names.PeriodStartsAtColumn}}, {{names.AdmittedRunCountColumn}}, {{names.ConsumedTokenCountColumn}})
            VALUES ({0}, 0, {1})
            ON CONFLICT ({{names.PeriodStartsAtColumn}}) DO UPDATE
            SET {{names.ConsumedTokenCountColumn}} = {{names.Periods}}.{{names.ConsumedTokenCountColumn}} + EXCLUDED.{{names.ConsumedTokenCountColumn}}
            RETURNING {{names.ConsumedTokenCountColumn}} AS "Value"
            """;
    }

    /// <summary>The identifiers both statements are composed from, taken from the model once.</summary>
    private sealed record MailAnsweringSpendPeriodNames(
        string Periods,
        string PeriodStartsAtColumn,
        string AdmittedRunCountColumn,
        string ConsumedTokenCountColumn)
    {
        public static MailAnsweringSpendPeriodNames Of(IModel model)
        {
            var periods = PersistedSchemaNames.EntityTypeOf<MailAnsweringSpendPeriodEntity>(model);

            return new MailAnsweringSpendPeriodNames(
                PersistedSchemaNames.QuotedTable(periods),
                PersistedSchemaNames.QuotedColumn(periods, nameof(MailAnsweringSpendPeriodEntity.PeriodStartsAt)),
                PersistedSchemaNames.QuotedColumn(periods, nameof(MailAnsweringSpendPeriodEntity.AdmittedRunCount)),
                PersistedSchemaNames.QuotedColumn(periods, nameof(MailAnsweringSpendPeriodEntity.ConsumedTokenCount)));
        }
    }
}
