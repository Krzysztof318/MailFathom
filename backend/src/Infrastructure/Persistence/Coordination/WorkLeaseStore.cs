// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Coordination;

/// <summary>Holds units of work exclusively in PostgreSQL, one holder per scope at a time.</summary>
/// <remarks>
/// <para>
/// All three operations are written statements rather than composed queries, and each for the same reason: the
/// guarantee is the statement's atomicity. The claim inserts on the primary key and resolves the conflict against the
/// recorded expiry in one statement, and the renewal and the release are each a single conditional update that writes
/// nothing when the hold has moved on. Reading a row and then writing it would leave a window between the two in
/// every one of them.
/// </para>
/// <para>
/// Nothing here reads a clock. Every instant a lease carries is PostgreSQL's, so the deployment's exclusion is decided
/// by one clock rather than by the difference between the replicas' — and the expiry a caller is handed comes back out
/// of the row the statement wrote.
/// </para>
/// <para>
/// The scoped context is used throughout and no method takes a persistence session, because a hold outlives the
/// transaction that took it. There is therefore nothing for a caller to enlist this in, which is what makes a lease
/// lost to somebody else's rollback structurally impossible rather than merely unlikely.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class WorkLeaseStore(MailFathomDbContext dbContext, WorkLeaseTelemetry telemetry) : IWorkLeaseStore
{
    /// <inheritdoc />
    public async Task<WorkLease?> ClaimAsync(
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        var takenExpiries = await dbContext.Database
            .SqlQuery<DateTimeOffset>(WorkLeaseStatements.ComposeClaim(scope, holder, leaseDuration))
            .ToArrayAsync(cancellationToken);

        var lease = LeaseOf(scope, holder, takenExpiries);
        telemetry.RecordClaim(scope, lease is not null);

        return lease;
    }

    /// <inheritdoc />
    public async Task<WorkLease?> RenewAsync(
        WorkScope scope,
        WorkLeaseHolder holder,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);

        var renewedExpiries = await dbContext.Database
            .SqlQuery<DateTimeOffset>(WorkLeaseStatements.ComposeRenewal(scope, holder, leaseDuration))
            .ToArrayAsync(cancellationToken);

        var lease = LeaseOf(scope, holder, renewedExpiries);
        telemetry.RecordRenewal(scope, lease is not null);

        return lease;
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(
        WorkScope scope,
        WorkLeaseHolder holder,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(holder);

        var releasedRows = await dbContext.Database.ExecuteSqlAsync(
            WorkLeaseStatements.ComposeRelease(scope, holder),
            cancellationToken);

        telemetry.RecordRelease(scope);

        return releasedRows == 1;
    }

    /// <summary>Reads the lease a statement wrote, or reports that it wrote none.</summary>
    /// <remarks>
    /// A claim and a renewal answer the same two ways — one row carrying the expiry PostgreSQL stamped, or no row at
    /// all — so the reading is one place rather than repeated at each of them.
    /// </remarks>
    private static WorkLease? LeaseOf(
        WorkScope scope,
        WorkLeaseHolder holder,
        IReadOnlyList<DateTimeOffset> writtenExpiries) => writtenExpiries is [var expiresAt]
        ? new WorkLease(scope, holder, expiresAt)
        : null;
}
