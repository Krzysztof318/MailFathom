// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds outgoing records in memory, with the lease and the stage rules the real store enforces.</summary>
/// <remarks>
/// <para>
/// It reproduces the behaviors the callers above depend on and nothing else: a request whose identity already has a
/// record reads that record back rather than producing a second, a claim hands a record to exactly one holder and
/// counts the attempt, and every write an attempt makes afterwards is refused once the record is held by a later one.
/// The constraints that make those true against a real database — the unique index and the claim statement's
/// <c>FOR UPDATE SKIP LOCKED</c> — are what the integration suite proves; here the dictionary stands in for them.
/// </para>
/// <para>
/// A write becomes visible only for a session the test declares as committing, which is what keeps the double's
/// identity guarantee the real store's: there a record is durable when <c>SaveChangesAsync</c> succeeded, so a session
/// that ends in a conflict leaves nothing behind. Without that, a losing attempt's own write would be what a retry read
/// back, and a store that persisted the loser instead of resolving to the winner would pass unnoticed.
/// </para>
/// </remarks>
/// <param name="sessionCommits">Answers whether a session's writes become durable, and admits every session when absent.</param>
/// <param name="timeProvider">Stamps what the store writes, and answers whether a lease has run out. A test that does not supply one gets a clock standing still at the Unix epoch, never the machine's.</param>
internal sealed class InMemoryOutgoingEmailStore(
    Func<IPersistenceSession, bool>? sessionCommits = null,
    TimeProvider? timeProvider = null)
    : IOutgoingEmailStore
{
    private readonly Dictionary<(string Account, OutgoingEmailOrigin Origin, string Identity), OutgoingEmailId>
        identities = [];

    private readonly Dictionary<OutgoingEmailId, StoredRow> rows = [];
    private readonly List<OutgoingEmailRequest> openRequests = [];
    private readonly TimeProvider clock = timeProvider ?? new FakeTimeProvider(DateTimeOffset.UnixEpoch);

    /// <summary>Gets every request that reached the store, including the ones that read a record back.</summary>
    internal IReadOnlyList<OutgoingEmailRequest> OpenRequests => this.openRequests;

    /// <summary>Gets or sets which records every write is refused for, and refuses none by default.</summary>
    /// <remarks>
    /// It stands in for the database going away while one send's answer was being written, which is the one failure an
    /// attempt cannot record its way out of: the recovery write meets the same refusal the write it recovers from did.
    /// </remarks>
    internal Func<OutgoingEmailId, bool> RefusesWrites { get; set; } = _ => false;

    /// <summary>Writes a record the way a writer that has already committed left it, without going through a session.</summary>
    /// <param name="request">The request the other writer recorded.</param>
    /// <param name="mimeByteLength">How many bytes of MIME it stored.</param>
    /// <returns>The record now in the store.</returns>
    /// <remarks>
    /// This is how a test arranges a race it has to interleave: the winner's row appears while the loser holds an open
    /// session, which is the moment the real unique index refuses the loser's insert.
    /// </remarks>
    internal OutgoingEmailRecord Publish(OutgoingEmailRequest request, long mimeByteLength)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recorded = this.Record(request, mimeByteLength);
        this.Commit(request, recorded);

        return recorded;
    }

    /// <summary>Reads back exactly what the store holds for one record, whatever stage it has reached.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <returns>The record.</returns>
    internal OutgoingEmailRecord Read(OutgoingEmailId outgoingEmailId) => this.rows[outgoingEmailId].Record;

    /// <summary>Reads the instant from which one record may be claimed again.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <returns>The instant the claim compares against.</returns>
    internal DateTimeOffset ReadAvailableAt(OutgoingEmailId outgoingEmailId) => this.rows[outgoingEmailId].AvailableAt;

    /// <summary>Answers whether one record is held by an attempt at all.</summary>
    /// <param name="outgoingEmailId">The record to read.</param>
    /// <returns><see langword="true" /> while a lease owner is stamped on it.</returns>
    internal bool IsLeased(OutgoingEmailId outgoingEmailId) => this.rows[outgoingEmailId].LeaseOwner is not null;

    /// <summary>Hands one record to a different holder, which is how a test makes a live attempt's lease stale.</summary>
    /// <param name="outgoingEmailId">The record to hand over.</param>
    /// <returns>The lease the new holder has it under.</returns>
    internal OutgoingEmailLease Reassign(OutgoingEmailId outgoingEmailId)
    {
        var row = this.rows[outgoingEmailId];
        var lease = new OutgoingEmailLease(Guid.CreateVersion7(), this.clock.GetUtcNow().AddMinutes(10));

        row.LeaseOwner = lease.Owner;
        row.LeaseExpiresAt = lease.ExpiresAt;

        return lease;
    }

    public Task<OutgoingEmailRecord> OpenAsync(
        IPersistenceSession session,
        OutgoingEmailRequest request,
        long mimeByteLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        this.openRequests.Add(request);

        if (this.identities.TryGetValue(IdentityOf(request), out var existing))
        {
            return Task.FromResult(this.rows[existing].Record);
        }

        var recorded = this.Record(request, mimeByteLength);

        // A session that will not commit leaves the record staged and nothing durable behind it, exactly as a losing
        // insert does against the unique index.
        if (sessionCommits?.Invoke(session) ?? true)
        {
            this.Commit(request, recorded);
        }

        return Task.FromResult(recorded);
    }

    public Task<OutgoingEmailRecord?> FindAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) => Task.FromResult(
            this.rows.TryGetValue(outgoingEmailId, out var row) ? row.Record : null);

    public Task<IReadOnlyList<OutgoingEmailRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<OutgoingEmailRecord> outstanding =
        [
            .. this.rows.Values
                .Select(row => row.Record)
                .Where(record => record.AccountId == accountId && !record.IsTerminal)
                .OrderBy(record => record.RecordedAt)
                .ThenBy(record => record.Id.Value)
                .Take(limit),
        ];

        return Task.FromResult(outstanding);
    }

    public Task<IReadOnlyList<ClaimedOutgoingEmail>> ClaimAsync(
        OutgoingEmailClaimRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var claimedAt = this.clock.GetUtcNow();
        var expiresAt = claimedAt + request.LeaseDuration;
        var due = this.rows.Values
            .Where(row => row.Record.AccountId == request.AccountId
                && row.Record.Stage == OutgoingEmailStage.Recorded
                && row.AvailableAt <= claimedAt
                && (row.LeaseExpiresAt is null || row.LeaseExpiresAt <= claimedAt))
            .OrderBy(row => row.AvailableAt)
            .ThenBy(row => row.Record.Id.Value)
            .Take(request.BatchSize)
            .ToArray();

        List<ClaimedOutgoingEmail> claimed = [];
        foreach (var row in due)
        {
            row.LeaseOwner = request.Owner;
            row.LeaseExpiresAt = expiresAt;
            row.Record = row.Record with { AttemptCount = row.Record.AttemptCount + 1 };

            claimed.Add(new ClaimedOutgoingEmail(row.Record, new OutgoingEmailLease(request.Owner, expiresAt)));
        }

        return Task.FromResult<IReadOnlyList<ClaimedOutgoingEmail>>(claimed);
    }

    public Task<int> MarkUnknownOutcomesAsync(MailAccountId accountId, CancellationToken cancellationToken)
    {
        var markedAt = this.clock.GetUtcNow();
        var stranded = this.rows.Values
            .Where(row => row.Record.AccountId == accountId
                && row.Record.Stage == OutgoingEmailStage.TransmissionBegun
                && row.Record.LastFailure != MailFathomErrorCode.OutgoingEmailOutcomeUnknown
                && (row.LeaseExpiresAt is null || row.LeaseExpiresAt <= markedAt))
            .ToArray();

        foreach (var row in stranded)
        {
            row.Record = row.Record with { LastFailure = MailFathomErrorCode.OutgoingEmailOutcomeUnknown };
        }

        return Task.FromResult(stranded.Length);
    }

    public Task DeferAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        DateTimeOffset availableAt,
        MailFathomErrorCode? failure,
        CancellationToken cancellationToken)
    {
        var row = this.RequireLeased(lease, outgoingEmailId);

        row.Record = row.Record with
        {
            Stage = OutgoingEmailStage.Recorded,
            StageChangedAt = this.clock.GetUtcNow(),
            LastFailure = failure ?? row.Record.LastFailure,
        };

        row.AvailableAt = availableAt;
        row.LeaseOwner = null;
        row.LeaseExpiresAt = null;

        return Task.CompletedTask;
    }

    public Task ReleaseAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var row = this.RequireLeased(lease, outgoingEmailId);

        row.Record = row.Record with
        {
            Stage = OutgoingEmailStage.Recorded,
            StageChangedAt = this.clock.GetUtcNow(),
            AttemptCount = Math.Max(0, row.Record.AttemptCount - 1),
        };

        row.LeaseOwner = null;
        row.LeaseExpiresAt = null;

        return Task.CompletedTask;
    }

    public Task RecordTransmissionBegunAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var row = this.RequireLeased(lease, outgoingEmailId);

        row.Record = row.Record with
        {
            Stage = OutgoingEmailStage.TransmissionBegun,
            StageChangedAt = this.clock.GetUtcNow(),
        };

        return Task.CompletedTask;
    }

    public Task AdvanceAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        OutgoingEmailStage stage,
        int? replyCode,
        CancellationToken cancellationToken)
    {
        var row = this.RequireLeased(lease, outgoingEmailId);

        row.Record = row.Record with
        {
            Stage = stage,
            StageChangedAt = this.clock.GetUtcNow(),
            LastReplyCode = replyCode ?? row.Record.LastReplyCode,
        };

        row.LeaseOwner = null;
        row.LeaseExpiresAt = null;

        return Task.CompletedTask;
    }

    public Task RecordRecipientOutcomesAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        var row = this.RequireLeased(lease, outgoingEmailId);
        var answered = outcomes.ToDictionary(outcome => outcome.Recipient.Address.NormalizedAddress);
        var carried = row.Record.Recipients
            .Select(recipient => recipient.Recipient.Address.NormalizedAddress)
            .ToHashSet(StringComparer.Ordinal);

        // The real store refuses an outcome naming an address the record does not, and a double that accepted one
        // would let a caller reporting against the wrong recipient pass every test built on it.
        if (answered.Keys.Any(address => !carried.Contains(address)))
        {
            throw new InvalidOperationException(
                $"Outgoing email record {outgoingEmailId.Value} was answered about a recipient it does not name.");
        }

        row.Record = row.Record with
        {
            Recipients =
            [
                .. row.Record.Recipients.Select(existing =>
                    existing.IsOutstanding
                    && answered.TryGetValue(existing.Recipient.Address.NormalizedAddress, out var settled)
                        ? settled
                        : existing),
            ],
        };

        return Task.CompletedTask;
    }

    public Task RecordFailureAsync(
        IPersistenceSession session,
        OutgoingEmailLease lease,
        OutgoingEmailId outgoingEmailId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        var row = this.RequireLeased(lease, outgoingEmailId);
        row.Record = row.Record with { LastFailure = failure };

        return Task.CompletedTask;
    }

    private static (string Account, OutgoingEmailOrigin Origin, string Identity) IdentityOf(
        OutgoingEmailRequest request) =>
        (request.AccountId.Value, request.Requester.Origin, request.Requester.Identity);

    private StoredRow RequireLeased(OutgoingEmailLease lease, OutgoingEmailId outgoingEmailId)
    {
        ArgumentNullException.ThrowIfNull(lease);

        if (this.RefusesWrites(outgoingEmailId))
        {
            throw new InvalidOperationException("The store would not take a write for this outgoing email.");
        }

        // The order is the real store's: an identifier nothing carries is refused before the lease is compared, and a
        // record nothing attempts again after it. A dictionary lookup would report a missing record as a
        // KeyNotFoundException, which is not the failure the port documents.
        if (!this.rows.TryGetValue(outgoingEmailId, out var row))
        {
            throw new InvalidOperationException($"No outgoing email record carries the identifier {outgoingEmailId}.");
        }

        if (row.LeaseOwner != lease.Owner)
        {
            throw new OutgoingEmailLeaseLostException(outgoingEmailId, lease.Owner);
        }

        if (row.Record.Stage is OutgoingEmailStage.Sent
            or OutgoingEmailStage.Refused
            or OutgoingEmailStage.Cancelled)
        {
            throw new InvalidOperationException(
                $"Outgoing email record {outgoingEmailId} is at terminal stage {row.Record.Stage} and is never attempted again.");
        }

        return row;
    }

    private void Commit(OutgoingEmailRequest request, OutgoingEmailRecord recorded)
    {
        this.identities[IdentityOf(request)] = recorded.Id;
        this.rows[recorded.Id] = new StoredRow { Record = recorded, AvailableAt = recorded.RecordedAt };
    }

    private OutgoingEmailRecord Record(OutgoingEmailRequest request, long mimeByteLength) => new()
    {
        Id = OutgoingEmailId.Create(Guid.CreateVersion7()),
        AccountId = request.AccountId,
        Requester = request.Requester,
        Recipients = [.. request.Recipients.Select(OutgoingRecipientOutcome.Unanswered)],
        Stage = OutgoingEmailStage.Recorded,
        MimeByteLength = mimeByteLength,
        AttemptCount = 0,
        RecordedAt = this.clock.GetUtcNow(),
        StageChangedAt = this.clock.GetUtcNow(),
        LastFailure = null,
        LastReplyCode = null,
    };

    /// <summary>Holds one record beside the claim state the record itself does not publish.</summary>
    private sealed class StoredRow
    {
        public required OutgoingEmailRecord Record { get; set; }

        public required DateTimeOffset AvailableAt { get; set; }

        public Guid? LeaseOwner { get; set; }

        public DateTimeOffset? LeaseExpiresAt { get; set; }
    }
}
