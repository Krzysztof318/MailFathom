// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core ledger of what each budget period has consumed reading attachments, per step and per owner.</summary>
[RequiresIntegrationCoverage]
internal sealed class AttachmentDerivationSpendLedger(MailFathomDbContext dbContext) : IAttachmentDerivationSpendLedger
{
    /// <summary>Adds a period's consumption, inserting the row the first time anything is charged to it.</summary>
    /// <remarks>
    /// One statement rather than a read and a write, because the account runs that charge do so in separate
    /// transactions and a read-modify-write would let each of them overwrite the other's increment with a total that
    /// was already stale when it was read. PostgreSQL's upsert makes the whole thing one atomic addition, and the column
    /// and table names come from the entity so the statement and the mapping cannot drift apart. The conflict target is
    /// the whole key: two steps inside one period are two rows rather than one they would take turns replacing.
    /// </remarks>
    private const string RecordSpendStatement = $$"""
        INSERT INTO {{AttachmentDerivationSpendPeriodEntity.TableName}}
            ("{{AttachmentDerivationSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.OwnerIdColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.StepColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.ConsumedUnitCountColumnName}}")
        VALUES ({0}, {1}, {2}, {3})
        ON CONFLICT ("{{AttachmentDerivationSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.OwnerIdColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.StepColumnName}}") DO UPDATE
        SET "{{AttachmentDerivationSpendPeriodEntity.ConsumedUnitCountColumnName}}" =
            {{AttachmentDerivationSpendPeriodEntity.TableName}}."{{AttachmentDerivationSpendPeriodEntity.ConsumedUnitCountColumnName}}"
            + EXCLUDED."{{AttachmentDerivationSpendPeriodEntity.ConsumedUnitCountColumnName}}"
        """;

    /// <inheritdoc />
    /// <remarks>
    /// Both totals are aggregated from the same set of rows in one round trip, which is what makes them describe one
    /// moment. A period nothing has been charged to has no row at all, and the aggregation over nothing reads as zero
    /// rather than as an absence — which is what makes the first charge of a new period ordinary.
    /// </remarks>
    public async Task<AttachmentDerivationTotals> ReadConsumedAsync(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var ownerId = owner.Value;

        var totals = await dbContext.AttachmentDerivationSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart && period.Step == derivationStep)
            .GroupBy(_ => 1)
            .Select(rows => new AttachmentDerivationTotals(
                rows.Sum(period => period.OwnerId == ownerId ? period.ConsumedUnitCount : 0L),
                rows.Sum(period => period.ConsumedUnitCount)))
            .SingleOrDefaultAsync(cancellationToken);

        return totals ?? AttachmentDerivationTotals.Unspent;
    }

    /// <inheritdoc />
    public async Task<long> ReadDeploymentConsumedAsync(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        CancellationToken cancellationToken) =>
        await dbContext.AttachmentDerivationSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart && period.Step == derivationStep)
            .SumAsync(period => period.ConsumedUnitCount, cancellationToken);

    /// <inheritdoc />
    public async Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        MailOwnerId owner,
        long unitCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCount);

        if (unitCount == 0)
        {
            return;
        }

        var sessionDbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // Every value is a parameter rather than composed text; only the identifiers, which come from the entity's own
        // constants, are part of the statement. The step travels as its name because that is how the column stores it.
        await sessionDbContext.Database.ExecuteSqlRawAsync(
            RecordSpendStatement,
            [periodStart, owner.Value, derivationStep.ToString(), unitCount],
            cancellationToken);
    }
}
