// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds mutation records in memory and answers the two questions a synchronization run asks of them.</summary>
/// <remarks>
/// The reads reproduce the filters the port documents rather than returning everything and leaving the domain predicate
/// to sort it out. A fake that answered more broadly than the real query would let a record the database never returns
/// still decide a test, so the narrowing is part of what is being reproduced.
/// </remarks>
internal sealed class InMemoryMailboxMutationReconciliationStore : IMailboxMutationReconciliationStore
{
    private readonly Dictionary<MailboxMutationRecordId, MailboxMutationRecord> recordsById = [];

    /// <summary>Puts a record into the state a completed mutation would have left.</summary>
    internal void Add(MailboxMutationRecord record) => this.recordsById[record.Id] = record;

    /// <summary>Reads back one record as a test asserts against it.</summary>
    internal MailboxMutationRecord RecordOf(MailboxMutationRecordId recordId) => this.recordsById[recordId];

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxMutationRecord>> ReadRelocationsPlacedAtAsync(
        MailAccountId accountId,
        RemoteFolderPath destinationPath,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        IReadOnlyList<MailboxMutationRecord> placed =
        [
            .. this.recordsById.Values
                .Where(record => record.Request.Occurrence.AccountId == accountId
                    && record.Request.Mutation == MailboxMutation.Relocate
                    && record.Stage == MailboxMutationStage.Completed
                    && record.PlacementObservedAt is null
                    && record.Request.DestinationPath?.Value == destinationPath.Value
                    && record.Placement.UidValidity == uidValidity
                    && record.Placement.Uid is { } placedUid
                    && uids.Contains(placedUid))
                .OrderBy(record => record.RecordedAt),
        ];

        return Task.FromResult(placed);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxMutationRecord>> ReadMutationsRemovingAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        IReadOnlyList<MailboxMutationRecord> removing =
        [
            .. this.recordsById.Values
                .Where(record => record.Request.Occurrence.AccountId == accountId
                    && record.Request.Occurrence.FolderResolutionId == folderResolutionId
                    && record.Request.Occurrence.UidValidity == uidValidity
                    && uids.Contains(record.Request.Occurrence.Uid)
                    && (record.Request.Mutation == MailboxMutation.Relocate
                        || record.Request.Mutation == MailboxMutation.Delete))
                .OrderBy(record => record.RecordedAt),
        ];

        return Task.FromResult(removing);
    }

    /// <inheritdoc />
    public Task RecordPlacementObservedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var record = this.Require(recordId);

        this.recordsById[recordId] = record with
        {
            PlacementObservedAt = record.PlacementObservedAt ?? observedAt,
            SourceRemovalObservedAt = record.SourceRemovalObservedAt ?? observedAt,
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordSourceRemovalObservedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var record = this.Require(recordId);

        this.recordsById[recordId] = record with
        {
            SourceRemovalObservedAt = record.SourceRemovalObservedAt ?? observedAt,
        };

        return Task.CompletedTask;
    }

    private MailboxMutationRecord Require(MailboxMutationRecordId recordId) =>
        this.recordsById.TryGetValue(recordId, out var record)
            ? record
            : throw new InvalidOperationException($"No mailbox mutation record carries the identifier {recordId}.");
}
