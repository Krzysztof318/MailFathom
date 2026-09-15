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

/// <summary>EF Core ledger of what each budget period has consumed reading attachments, per step and per user.</summary>
[RequiresIntegrationCoverage]
internal sealed class AttachmentDerivationSpendLedger(MailFathomDbContext dbContext) : IAttachmentDerivationSpendLedger
{
    /// <summary>The user column's value on the row carrying the deployment's own total, which names nobody.</summary>
    private static readonly Guid DeploymentRow = Guid.Empty;

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
            ("{{AttachmentDerivationSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.UserIdColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.StepColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.ConsumedUnitCountColumnName}}")
        VALUES ({0}, {1}, {2}, {3})
        ON CONFLICT ("{{AttachmentDerivationSpendPeriodEntity.PeriodStartsAtColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.UserIdColumnName}}", "{{AttachmentDerivationSpendPeriodEntity.StepColumnName}}") DO UPDATE
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
        MailUserId user,
        CancellationToken cancellationToken)
    {
        var userId = user.Value;
        var deploymentRow = DeploymentRow;

        var totals = await dbContext.AttachmentDerivationSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart && period.Step == derivationStep)
            .GroupBy(_ => 1)
            .Select(rows => new AttachmentDerivationTotals(
                rows.Sum(period => period.UserId == userId ? period.ConsumedUnitCount : 0L),
                rows.Sum(period => period.UserId == deploymentRow ? period.ConsumedUnitCount : 0L)))
            .SingleOrDefaultAsync(cancellationToken);

        return totals ?? AttachmentDerivationTotals.Unspent;
    }

    /// <inheritdoc />
    public async Task<long> ReadDeploymentConsumedAsync(
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        CancellationToken cancellationToken)
    {
        var deploymentRow = DeploymentRow;

        return await dbContext.AttachmentDerivationSpendPeriods
            .AsNoTracking()
            .Where(period => period.PeriodStartsAt == periodStart
                && period.Step == derivationStep
                && period.UserId == deploymentRow)
            .SumAsync(period => period.ConsumedUnitCount, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RecordSpendAsync(
        IPersistenceSession session,
        DateTimeOffset periodStart,
        AttachmentDerivationStep derivationStep,
        IReadOnlyCollection<MailUserId> users,
        long unitCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentOutOfRangeException.ThrowIfNegative(unitCount);

        if (unitCount == 0)
        {
            return;
        }

        var sessionDbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // The deployment's own row first, so what was read is counted once whether the mailbox has one assigned user,
        // several, or none. The loop is what charges each of them in full, which is the per-user ceiling ADR 0014 asks
        // for and is deliberately more than was read.
        await IncrementAsync(sessionDbContext, periodStart, DeploymentRow, derivationStep, unitCount, cancellationToken);

        foreach (var user in users)
        {
            await IncrementAsync(sessionDbContext, periodStart, user.Value, derivationStep, unitCount, cancellationToken);
        }
    }

    /// <summary>Issues one upsert, which is the whole of what a charge is.</summary>
    private static Task<int> IncrementAsync(
        MailFathomDbContext sessionDbContext,
        DateTimeOffset periodStart,
        Guid userId,
        AttachmentDerivationStep derivationStep,
        long unitCount,
        CancellationToken cancellationToken) =>

        // Every value is a parameter rather than composed text; only the identifiers, which come from the entity's own
        // constants, are part of the statement. The step travels as its name because that is how the column stores it.
        sessionDbContext.Database.ExecuteSqlRawAsync(
            RecordSpendStatement,
            [periodStart, userId, derivationStep.ToString(), unitCount],
            cancellationToken);
}
