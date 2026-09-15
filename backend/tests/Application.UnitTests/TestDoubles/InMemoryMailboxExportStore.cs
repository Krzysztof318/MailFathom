// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps exports in a dictionary, with the same conditional state write the database does.</summary>
/// <remarks>
/// The condition is the part worth reproducing rather than the storage: an operator cancelling and the writing job
/// completing reach one row at the same moment, and every test about that outcome depends on the losing write
/// reporting that it changed nothing.
/// </remarks>
internal sealed class InMemoryMailboxExportStore : IMailboxExportStore
{
    private readonly Dictionary<MailboxExportId, MailboxExport> exports = [];

    /// <summary>Gets how many state writes were refused because the row was no longer in the expected state.</summary>
    internal int RefusedWrites { get; private set; }

    /// <summary>Puts one export in the store without going through the use case that records one.</summary>
    /// <param name="export">The export.</param>
    /// <returns>The same store, so an arrangement reads as one expression.</returns>
    internal InMemoryMailboxExportStore Holding(MailboxExport export)
    {
        this.exports[export.Id] = export;

        return this;
    }

    /// <summary>Reads one export as the store now holds it.</summary>
    /// <param name="exportId">The export.</param>
    /// <returns>The export, or <see langword="null" /> when the store holds none.</returns>
    internal MailboxExport? Find(MailboxExportId exportId) =>
        this.exports.TryGetValue(exportId, out var held) ? held : null;

    /// <inheritdoc />
    public Task RecordAsync(MailboxExport queuedExport, CancellationToken cancellationToken)
    {
        this.exports[queuedExport.Id] = queuedExport;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<MailboxExport?> FindAsync(
        MailAccountId account,
        MailboxExportId exportId,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.exports.TryGetValue(exportId, out var held) && held.Account == account ? held : null);

    /// <inheritdoc />
    public Task<MailboxExport?> FindInFlightAsync(MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult(this.exports.Values
            .Where(export => export.Account == account && export.IsInFlight)
            .OrderByDescending(export => export.RequestedAt)
            .FirstOrDefault());

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxExport>> ListAsync(
        MailAccountId account,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailboxExport>>(
        [
            .. this.exports.Values
                .Where(export => export.Account == account)
                .OrderByDescending(export => export.RequestedAt)
                .Take(limit),
        ]);

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxExport>> FindDueForExpiryAsync(
        DateTimeOffset asOf,
        int limit,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MailboxExport>>(
        [
            .. this.exports.Values
                .Where(export => export.State == MailboxExportState.Completed)
                .Where(export => export.ExpiresAt is { } expiresAt && expiresAt <= asOf)
                .OrderBy(export => export.ExpiresAt)
                .Take(limit),
        ]);

    /// <inheritdoc />
    public Task<bool> SaveAsync(
        MailboxExport updatedExport,
        MailboxExportState expectedState,
        CancellationToken cancellationToken)
    {
        if (!this.exports.TryGetValue(updatedExport.Id, out var held) || held.State != expectedState)
        {
            this.RefusedWrites++;

            return Task.FromResult(false);
        }

        this.exports[updatedExport.Id] = updatedExport;

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task SaveProgressAsync(
        MailboxExportId exportId,
        long messageCount,
        long byteCount,
        CancellationToken cancellationToken)
    {
        if (this.exports.TryGetValue(exportId, out var held) && held.State == MailboxExportState.Running)
        {
            this.exports[exportId] = held with { MessageCount = messageCount, ByteCount = byteCount };
        }

        return Task.CompletedTask;
    }
}
