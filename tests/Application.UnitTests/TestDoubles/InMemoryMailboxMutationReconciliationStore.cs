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
/// <para>
/// The reads reproduce the filters the port documents rather than returning everything and leaving the domain predicate
/// to sort it out. A fake that answered more broadly than the real query would let a record the database never returns
/// still decide a test, so the narrowing is part of what is being reproduced.
/// </para>
/// <para>
/// They reproduce its ordering for the same reason. Attributing one disappearance to the oldest of several records is
/// the first match in that order, so a fake ordering by the timestamp alone would decide a tied-timestamp test on
/// whatever order the dictionary happened to enumerate — behavior the real store does not have, because it breaks the
/// tie on the identifier.
/// </para>
/// </remarks>
internal sealed class InMemoryMailboxMutationReconciliationStore : IMailboxMutationReconciliationStore
{
    private readonly Dictionary<MailboxMutationRecordId, MailboxMutationRecord> recordsById = [];

    /// <summary>The mutations whose whole effect is a value a <c>FLAGS</c> response reports back.</summary>
    /// <remarks>Written out rather than shared with the real store, for the reason this whole double exists: a fake that reached into the implementation would pass whatever that implementation did.</remarks>
    private static readonly MailboxMutation[] FlagWritingMutations =
    [
        MailboxMutation.SetSeen,
        MailboxMutation.SetFlagged,
        MailboxMutation.AddKeywords,
        MailboxMutation.RemoveKeywords,
        MailboxMutation.SetKeywords,
    ];

    /// <summary>Gets how many times a caller asked which flag and keyword changes were MailFathom's own.</summary>
    /// <remarks>
    /// A window over a mailbox nobody has touched must ask nothing, which is a cost guarantee no assertion about the
    /// answer can express: a store that returned nothing would satisfy every other test while still costing a query on
    /// every run of every folder. It counts calls rather than values asked about, which is also what proves the three
    /// values are attributed against one read rather than three.
    /// </remarks>
    internal int FlagChangeReadCount { get; private set; }

    /// <summary>Gets the age bound the last flag-change read was narrowed by.</summary>
    /// <remarks>
    /// The bound is what keeps that read from growing without limit as one occurrence accumulates records, and passing
    /// one that is too late would silently withhold the record that explains a value. Nothing in the answer reports
    /// which bound produced it, so the argument is recorded here instead.
    /// </remarks>
    internal DateTimeOffset? LastFlagChangeReadIssuedAfter { get; private set; }

    /// <summary>Puts a record into the state a completed mutation would have left.</summary>
    internal void Add(MailboxMutationRecord record) => this.recordsById[record.Id] = record;

    /// <summary>Reads back one record as a test asserts against it.</summary>
    internal MailboxMutationRecord RecordOf(MailboxMutationRecordId recordId) => this.recordsById[recordId];

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxMutationRecord>> ReadPlacementsAtAsync(
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
                    && (record.Request.Mutation == MailboxMutation.Relocate
                        || record.Request.Mutation == MailboxMutation.Copy)
                    && record.Stage == MailboxMutationStage.Completed
                    && record.PlacementObservedAt is null
                    && record.Request.DestinationPath?.Value == destinationPath.Value
                    && record.Placement.UidValidity == uidValidity
                    && record.Placement.Uid is { } placedUid
                    && uids.Contains(placedUid))
                .OrderBy(record => record.RecordedAt)
                .ThenBy(record => record.Id.Value),
        ];

        return Task.FromResult(placed);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxMutationRecord>> ReadFlagChangesOnAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        DateTimeOffset issuedAfter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        this.FlagChangeReadCount++;
        this.LastFlagChangeReadIssuedAfter = issuedAfter;

        IReadOnlyList<MailboxMutationRecord> writing =
        [
            .. this.recordsById.Values
                .Where(record => record.Request.Occurrence.AccountId == accountId
                    && record.Request.Occurrence.FolderResolutionId == folderResolutionId
                    && record.Request.Occurrence.UidValidity == uidValidity
                    && uids.Contains(record.Request.Occurrence.Uid)
                    && FlagWritingMutations.Contains(record.Request.Mutation)
                    && record.Stage != MailboxMutationStage.Recorded
                    && record.StageChangedAt > issuedAfter)
                .GroupBy(record => record.Request.Occurrence.Uid)
                .SelectMany(occurrence => occurrence
                    .OrderByDescending(record => record.StageChangedAt)
                    .ThenByDescending(record => record.Id.Value)
                    .Take(IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsPerOccurrence))
                .OrderBy(record => record.StageChangedAt)
                .ThenBy(record => record.Id.Value),
        ];

        return Task.FromResult(writing);
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
                .OrderBy(record => record.RecordedAt)
                .ThenBy(record => record.Id.Value),
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

            // A copy takes nothing out of its source folder, so the real store settles no removal for one and a fake
            // that did would let a test pass against a record production never writes.
            SourceRemovalObservedAt = record.Request.Mutation == MailboxMutation.Relocate
                ? record.SourceRemovalObservedAt ?? observedAt
                : record.SourceRemovalObservedAt,
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
