// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Rebuilds the dead letter one stopped row describes.</summary>
/// <remarks>
/// Separate from the reader for the reason <see cref="JobRecordMapping" /> is separate from the store: what a row means
/// is decided here, where it can be exercised without a database, and the query beside it decides only which rows are
/// read.
/// </remarks>
internal static class DeadLetteredJobMapping
{
    /// <summary>Rebuilds the dead letter a stopped row describes, or reports that this build cannot name its work.</summary>
    /// <param name="row">The projected row, as the reading returned it.</param>
    /// <returns>The dead letter, or <see langword="null" /> when the row names a job type this build does not declare.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="row" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// An undeclared type is answered with nothing rather than refused, which is the opposite of what a claimed row
    /// does. A claim has already filtered to the types this process runs, so a name it cannot read there is a
    /// contradiction; here the reading is deployment-wide, and a row written by a build that declared more types is an
    /// ordinary thing to meet during a rolling deployment. Reporting it under a name nothing runs would offer an
    /// operator a retry that no worker could ever claim.
    /// </remarks>
    internal static DeadLetteredJob? ToDeadLetteredJob(DeadLetteredJobRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!JobType.TryParseName(row.JobType, out var jobType))
        {
            return null;
        }

        return new DeadLetteredJob(
            JobId.Create(row.Id),
            jobType,
            JobIdempotencyKey.Create(row.IdempotencyKey),
            row.MailboxAccountId is { } accountId ? MailAccountId.Create(accountId) : null,
            row.AttemptCount,
            row.EnqueuedAt,
            row.StateChangedAt)
        {
            LastFailure = row is { LastFailureClassification: { } classification, LastFailureReason: { } reason }
                ? JobFailureRecord.Create(classification, reason)
                : null,
        };
    }
}

/// <summary>One stopped job as the database returns it, before the application values are rebuilt from it.</summary>
/// <remarks>
/// The payload is deliberately absent. It names a message occurrence, nothing on this surface reports it, and a
/// projection that left it out of the answer but read it from the row would still have carried it through the process.
/// </remarks>
/// <param name="Id">The job's own identifier.</param>
/// <param name="JobType">The stored name of the kind of work.</param>
/// <param name="IdempotencyKey">The identity the enqueuer composed.</param>
/// <param name="MailboxAccountId">The account the work belongs to, and <see langword="null" /> when it belongs to none.</param>
/// <param name="AttemptCount">How many attempts were handed out.</param>
/// <param name="LastFailureClassification">What the last failed attempt was classified as.</param>
/// <param name="LastFailureReason">The operator-safe name of what the last attempt failed with.</param>
/// <param name="EnqueuedAt">When the work was first enqueued.</param>
/// <param name="StateChangedAt">When the row reached the state it is in now.</param>
internal sealed record DeadLetteredJobRow(
    Guid Id,
    string JobType,
    string IdempotencyKey,
    string? MailboxAccountId,
    int AttemptCount,
    JobFailureClassification? LastFailureClassification,
    string? LastFailureReason,
    DateTimeOffset EnqueuedAt,
    DateTimeOffset StateChangedAt);
