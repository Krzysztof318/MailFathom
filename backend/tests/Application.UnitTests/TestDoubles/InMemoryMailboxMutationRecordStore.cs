// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
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
    private readonly Dictionary<MailFolderResolutionId, MailFolderResolution> folderBindings = [];
    private DateTimeOffset now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Gets how many requests were written down, which is one per idempotency identity however often it was asked.</summary>
    internal int OpenedRecordCount => this.recordsById.Count;

    /// <summary>Gets the requests written down, in the order they were opened.</summary>
    /// <remarks>
    /// The order matters to a caller that asks for several changes to one email, because the order they are asked for
    /// in is the order MailFathom applies them.
    /// </remarks>
    internal IReadOnlyList<MailboxMutationRequest> OpenedRequests =>
        [.. this.recordsById.Values.OrderBy(record => record.RecordedAt).Select(record => record.Request)];

    /// <summary>Gets or sets what the account's audit trail setting resolves to when a record is opened.</summary>
    /// <remarks>
    /// It is a property of the store because that is where the real one resolves it: the answer is written onto the row
    /// once and never re-read, so a test arranges it here rather than by changing a setting mid-run.
    /// </remarks>
    internal bool AuditsMutations { get; set; }

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
            IsAudited = this.AuditsMutations,
            RequiresSourceRemoval = false,
            Placement = RemoteEmailPlacement.NotReported(),
            AttemptCount = 0,
            RecordedAt = this.now,
            StageChangedAt = this.now,
            LastFailure = null,
            PlacementObservedAt = null,
            SourceRemovalObservedAt = null,
        };

        this.identities[identity] = record.Id;
        this.recordsById[record.Id] = record;

        return Task.FromResult(record);
    }

    /// <inheritdoc />
    public Task<bool> HasRecordAsync(
        StoredEmailId storedEmailId,
        MailboxMutation mutation,
        MailboxMutationOrigin origin,
        CancellationToken cancellationToken) => Task.FromResult(this.recordsById.Values.Any(record =>
            record.Request.StoredEmailId == storedEmailId
            && record.Request.Mutation == mutation
            && record.Request.Requester.Origin == origin));

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxMutationRecord>> ReadAsync(
        MailOwnerId owner,
        IReadOnlyList<MailboxMutationRecordId> recordIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MailboxMutationRecord> held =
        [
            .. recordIds
                .Distinct()
                .Select(recordId => this.recordsById.GetValueOrDefault(recordId))
                .OfType<MailboxMutationRecord>()
                .Where(record => record.Owner == owner)
                .OrderBy(record => record.RecordedAt)
                .ThenBy(record => record.Id.Value),
        ];

        return Task.FromResult(held);
    }

    /// <inheritdoc />
    public Task<MailboxMutationRecord?> WithdrawAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken)
    {
        if (this.recordsById.GetValueOrDefault(recordId) is not { } record || record.Owner != owner)
        {
            return Task.FromResult<MailboxMutationRecord?>(null);
        }

        if (record.Stage is MailboxMutationStage.Recorded)
        {
            record = record with { Stage = MailboxMutationStage.Cancelled, StageChangedAt = this.Advance() };
            this.recordsById[recordId] = record;
        }

        return Task.FromResult<MailboxMutationRecord?>(record);
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
    public Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingAsync(
        MailAccountIdentity account,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<OutstandingMailboxMutation> outstanding =
        [
            .. this.OutstandingOf(account.Id)
                .OrderBy(record => record.RecordedAt)
                .Take(limit)
                .Select(record => new OutstandingMailboxMutation(record, this.BindingOf(record))),
        ];

        return Task.FromResult(outstanding);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<MailboxMutationLifecycleCount>> ReadLifecycleCountsAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<MailboxMutationLifecycleCount> counts =
        [
            .. this.OutstandingOf(account.Id)
                .GroupBy(record => new { record.Request.Mutation, record.Lifecycle })
                .Select(group => new MailboxMutationLifecycleCount(
                    group.Key.Mutation,
                    group.Key.Lifecycle,
                    group.Count(),
                    group.Min(record => record.RecordedAt))),
        ];

        return Task.FromResult(counts);
    }

    /// <summary>States the remote folder one alias binding names, for the records read back through it.</summary>
    /// <remarks>
    /// The real store joins the binding row; there is none here, so a test that cares which folder a resumed mutation
    /// selects says so. Everything else takes the alias itself as the path, which is the shape most fixtures use.
    /// </remarks>
    internal void BindFolder(MailFolderResolution resolution) =>
        this.folderBindings[resolution.Id] = resolution;

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

    private IEnumerable<MailboxMutationRecord> OutstandingOf(MailAccountId accountId) =>
        this.recordsById.Values.Where(record => record.Request.Occurrence.AccountId == accountId &&
            record.Stage != MailboxMutationStage.Completed &&
            record.Stage != MailboxMutationStage.Cancelled);

    private MailFolderResolution BindingOf(MailboxMutationRecord record)
    {
        var folderResolutionId = record.Request.Occurrence.FolderResolutionId;

        return this.folderBindings.TryGetValue(folderResolutionId, out var binding)
            ? binding
            : new MailFolderResolution(
                folderResolutionId.Alias,
                folderResolutionId.Generation,
                RemoteFolderPath.Create(folderResolutionId.Alias.Value, hierarchyDelimiter: null));
    }

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
