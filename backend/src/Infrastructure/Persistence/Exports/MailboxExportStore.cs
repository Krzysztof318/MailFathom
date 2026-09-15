// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Export;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Exports;

/// <summary>Keeps the record of every export in PostgreSQL, one row per export.</summary>
/// <remarks>
/// <para>
/// Every state write is one conditional <c>UPDATE</c> naming the state it expects to find, rather than a read followed
/// by a write: an operator cancelling and the writing job completing reach one row at the same moment, and what decides
/// between them has to be the statement rather than which of them read first. The row count the statement reports is
/// the answer the port returns.
/// </para>
/// <para>
/// The statements are issued directly instead of through the change tracker for the reason the repair-request store
/// gives: these run outside any persistence session, and calling <c>SaveChanges</c> on the scoped context would commit
/// whatever else that scope happened to have pending.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxExportStore(MailFathomDbContext dbContext) : IMailboxExportStore
{
    /// <inheritdoc />
    public async Task RecordAsync(MailboxExport queuedExport, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queuedExport);

        dbContext.MailboxExports.Add(new MailboxExportEntity
        {
            Id = queuedExport.Id.Value,
            MailboxAccountId = queuedExport.Account.Value,
            FolderPath = queuedExport.FolderPath,
            State = queuedExport.State,
            RequestedAt = queuedExport.RequestedAt,
            MessageCount = queuedExport.MessageCount,
            ByteCount = queuedExport.ByteCount,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MailboxExport?> FindAsync(
        MailAccountId account,
        MailboxExportId exportId,
        CancellationToken cancellationToken)
    {
        var row = await dbContext.MailboxExports
            .AsNoTracking()
            .FirstOrDefaultAsync(
                export => export.Id == exportId.Value && export.MailboxAccountId == account.Value,
                cancellationToken);

        return row is null ? null : Describe(row);
    }

    /// <inheritdoc />
    public async Task<MailboxExport?> FindInFlightAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        var row = await dbContext.MailboxExports
            .AsNoTracking()
            .Where(export => export.MailboxAccountId == account.Value)
            .Where(export => export.State == MailboxExportState.Queued || export.State == MailboxExportState.Running)
            .OrderByDescending(export => export.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : Describe(row);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxExport>> ListAsync(
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var rows = await dbContext.MailboxExports
            .AsNoTracking()
            .Where(export => export.MailboxAccountId == account.Value)
            .OrderByDescending(export => export.RequestedAt)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(Describe)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxExport>> FindDueForExpiryAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var rows = await dbContext.MailboxExports
            .AsNoTracking()
            .Where(export => export.State == MailboxExportState.Completed)
            .Where(export => export.ExpiresAt != null && export.ExpiresAt <= asOf)
            .OrderBy(export => export.ExpiresAt)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. rows.Select(Describe)];
    }

    /// <inheritdoc />
    public async Task<bool> SaveAsync(
        MailboxExport updatedExport,
        MailboxExportState expectedState,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updatedExport);

        var affected = await dbContext.MailboxExports
            .Where(export => export.Id == updatedExport.Id.Value && export.State == expectedState)
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(export => export.State, updatedExport.State)
                    .SetProperty(export => export.MessageCount, updatedExport.MessageCount)
                    .SetProperty(export => export.ByteCount, updatedExport.ByteCount)
                    .SetProperty(export => export.ArchiveByteLength, updatedExport.ArchiveByteLength)
                    .SetProperty(export => export.ObjectLocator, updatedExport.ObjectLocator)
                    .SetProperty(export => export.CompletedAt, updatedExport.CompletedAt)
                    .SetProperty(export => export.ExpiresAt, updatedExport.ExpiresAt)
                    .SetProperty(
                        export => export.FailureCode,
                        updatedExport.FailureCode == null ? null : updatedExport.FailureCode.Value.Value),
                cancellationToken);

        return affected > 0;
    }

    /// <inheritdoc />
    public Task SaveProgressAsync(
        MailboxExportId exportId,
        long messageCount,
        long byteCount,
        CancellationToken cancellationToken) =>
        dbContext.MailboxExports
            .Where(export => export.Id == exportId.Value && export.State == MailboxExportState.Running)
            .ExecuteUpdateAsync(
                row => row
                    .SetProperty(export => export.MessageCount, messageCount)
                    .SetProperty(export => export.ByteCount, byteCount),
                cancellationToken);

    private static MailboxExport Describe(MailboxExportEntity row) => new(
        MailboxExportId.Create(row.Id),
        MailAccountId.Create(row.MailboxAccountId),
        row.FolderPath,
        row.State,
        row.RequestedAt,
        row.MessageCount,
        row.ByteCount,
        row.ArchiveByteLength,
        row.ObjectLocator,
        row.CompletedAt,
        row.ExpiresAt,
        FailureCodeOf(row.FailureCode));

    /// <summary>Reads back a recorded failure code, leaving a number this build does not publish unread.</summary>
    /// <remarks>
    /// A code written by a later build is answered as no code rather than as a value nothing names, which is the same
    /// rule a job type read back under an unknown name follows: an older replica reports what it can rather than
    /// refusing the row.
    /// </remarks>
    private static MailFathomErrorCode? FailureCodeOf(int? value) => value is { } code
        ? MailFathomErrorCode.All.FirstOrDefault(candidate => candidate.Value == code) is { IsSpecified: true } found
            ? found
            : null
        : null;
}
