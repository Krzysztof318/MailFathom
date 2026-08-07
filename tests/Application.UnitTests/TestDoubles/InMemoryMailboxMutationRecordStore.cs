// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps mutation records in memory with the identity rule and the forward-only stage rule the real store has.</summary>
/// <remarks>
/// The session is accepted and unused, because what a persistence session guarantees is a transaction and there is none
/// here. The two rules that are reproduced are the two the performer's behavior rests on: one record per idempotency
/// identity, and a stage that never moves backwards or out of a terminal one.
/// </remarks>
internal sealed class InMemoryMailboxMutationRecordStore : IMailboxMutationRecordStore
{
    private readonly Dictionary<MailboxMutationRecordId, MailboxMutationRecord> recordsById = [];
    private readonly Dictionary<string, MailboxMutationRecordId> identities = [];
    private DateTimeOffset now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Gets how many requests were written down, which is one per idempotency identity however often it was asked.</summary>
    internal int OpenedRecordCount => this.recordsById.Count;

    /// <inheritdoc />
    public Task<MailboxMutationRecord> OpenAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        CancellationToken cancellationToken)
    {
        var identity = IdentityOf(request);

        if (this.identities.TryGetValue(identity, out var existingId))
        {
            return Task.FromResult(this.recordsById[existingId]);
        }

        var record = new MailboxMutationRecord
        {
            Id = MailboxMutationRecordId.Create(Guid.CreateVersion7(this.Advance())),
            Request = request,
            Stage = MailboxMutationStage.Recorded,
            RequiresSourceRemoval = false,
            Placement = RemoteEmailPlacement.NotReported(),
            AttemptCount = 0,
            RecordedAt = this.now,
            StageChangedAt = this.now,
            LastFailure = null,
        };

        this.identities[identity] = record.Id;
        this.recordsById[record.Id] = record;

        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<int> CountAttemptAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken)
    {
        var record = this.Require(recordId);
        var counted = record with { AttemptCount = record.AttemptCount + 1 };
        this.recordsById[recordId] = counted;

        return Task.FromResult(counted.AttemptCount);
    }

    /// <inheritdoc />
    public Task RecordPlacementIssuedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        bool requiresSourceRemoval,
        CancellationToken cancellationToken)
    {
        var record = this.Require(recordId);

        if (record.IsTerminal || MailboxMutationStage.PlacementIssued <= record.Stage)
        {
            throw new InvalidOperationException(
                $"Mailbox mutation record {recordId} is at stage {record.Stage} and cannot be moved to PlacementIssued.");
        }

        this.recordsById[recordId] = record with
        {
            Stage = MailboxMutationStage.PlacementIssued,
            RequiresSourceRemoval = requiresSourceRemoval,
            StageChangedAt = this.Advance(),
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AdvanceAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailboxMutationStage stage,
        RemoteEmailPlacement? placement,
        CancellationToken cancellationToken)
    {
        var record = this.Require(recordId);

        if (record.IsTerminal || stage <= record.Stage)
        {
            throw new InvalidOperationException(
                $"Mailbox mutation record {recordId} is at stage {record.Stage} and cannot be moved to {stage}.");
        }

        this.recordsById[recordId] = record with
        {
            Stage = stage,
            StageChangedAt = this.Advance(),
            Placement = placement ?? record.Placement,
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RecordFailureAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        this.recordsById[recordId] = this.Require(recordId) with { LastFailure = failure };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxMutationRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<MailboxMutationRecord> outstanding =
        [
            .. this.recordsById.Values
                .Where(record => record.Request.Occurrence.AccountId == accountId &&
                    record.Stage != MailboxMutationStage.Completed)
                .OrderBy(record => record.RecordedAt)
                .Take(limit),
        ];

        return Task.FromResult(outstanding);
    }

    /// <summary>Reads back the one record written for a request, as a test asserts against it.</summary>
    internal MailboxMutationRecord RecordOf(MailboxMutationRequest request) =>
        this.recordsById[this.identities[IdentityOf(request)]];

    /// <summary>Puts a record into the state a previous run would have left, without going through a mutation.</summary>
    internal void Arrange(MailboxMutationRequest request, Func<MailboxMutationRecord, MailboxMutationRecord> arrange)
    {
        var recordId = this.identities[IdentityOf(request)];
        this.recordsById[recordId] = arrange(this.recordsById[recordId]);
    }

    private static string IdentityOf(MailboxMutationRequest request) => string.Join(
        '|',
        request.Occurrence.FolderResolutionId,
        request.Occurrence.UidValidity.Value,
        request.Occurrence.Uid.Value,
        request.Requester.Origin,
        request.Requester.Identity,
        request.Mutation.Name);

    private MailboxMutationRecord Require(MailboxMutationRecordId recordId) =>
        this.recordsById.TryGetValue(recordId, out var record)
            ? record
            : throw new InvalidOperationException($"No mailbox mutation record carries the identifier {recordId}.");

    private DateTimeOffset Advance()
    {
        this.now = this.now.AddSeconds(1);

        return this.now;
    }
}
