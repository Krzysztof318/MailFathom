// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds outgoing records in memory, keyed by the idempotency identity a request carries.</summary>
/// <remarks>
/// <para>
/// It reproduces the one behavior the callers above depend on and nothing else: a request whose identity already has a
/// record reads that record back rather than producing a second. The constraint that makes it true against a real
/// database is the unique index, which the integration suite proves; here the dictionary stands in for it.
/// </para>
/// <para>
/// A write becomes visible only for a session the test declares as committing, which is what keeps the double's
/// identity guarantee the real store's: there a record is durable when <c>SaveChangesAsync</c> succeeded, so a session
/// that ends in a conflict leaves nothing behind. Without that, a losing attempt's own write would be what a retry read
/// back, and a store that persisted the loser instead of resolving to the winner would pass unnoticed.
/// </para>
/// </remarks>
/// <param name="sessionCommits">Answers whether a session's writes become durable, and admits every session when absent.</param>
internal sealed class InMemoryOutgoingEmailStore(Func<IPersistenceSession, bool>? sessionCommits = null)
    : IOutgoingEmailStore
{
    private readonly Dictionary<(string Account, OutgoingEmailOrigin Origin, string Identity), OutgoingEmailRecord>
        recordsByIdentity = [];

    private readonly List<OutgoingEmailRequest> openRequests = [];

    /// <summary>Gets every request that reached the store, including the ones that read a record back.</summary>
    internal IReadOnlyList<OutgoingEmailRequest> OpenRequests => this.openRequests;

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

        var identity = (request.AccountId.Value, request.Requester.Origin, request.Requester.Identity);
        var recorded = Record(request, mimeByteLength);
        this.recordsByIdentity[identity] = recorded;

        return recorded;
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

        var identity = (request.AccountId.Value, request.Requester.Origin, request.Requester.Identity);
        if (this.recordsByIdentity.TryGetValue(identity, out var existing))
        {
            return Task.FromResult(existing);
        }

        var recorded = Record(request, mimeByteLength);

        // A session that will not commit leaves the record staged and nothing durable behind it, exactly as a losing
        // insert does against the unique index.
        if (sessionCommits?.Invoke(session) ?? true)
        {
            this.recordsByIdentity[identity] = recorded;
        }

        return Task.FromResult(recorded);
    }

    public Task<OutgoingEmailRecord?> FindAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) => Task.FromResult(
            this.recordsByIdentity.Values.SingleOrDefault(record => record.Id == outgoingEmailId));

    public Task<IReadOnlyList<OutgoingEmailRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<OutgoingEmailRecord> outstanding =
        [
            .. this.recordsByIdentity.Values
                .Where(record => record.AccountId == accountId && !record.IsTerminal)
                .OrderBy(record => record.RecordedAt)
                .Take(limit),
        ];

        return Task.FromResult(outstanding);
    }

    public Task<int> CountAttemptAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double counts an attempt.");

    public Task RecordTransmissionBegunAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double transmits anything.");

    public Task AdvanceAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        OutgoingEmailStage stage,
        int? replyCode,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double advances a stage.");

    public Task RecordRecipientOutcomesAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double answers about a recipient.");

    public Task RecordFailureAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double records a failure.");

    private static OutgoingEmailRecord Record(OutgoingEmailRequest request, long mimeByteLength) => new()
    {
        Id = OutgoingEmailId.Create(Guid.CreateVersion7()),
        AccountId = request.AccountId,
        Requester = request.Requester,
        Recipients = [.. request.Recipients.Select(OutgoingRecipientOutcome.Unanswered)],
        Stage = OutgoingEmailStage.Recorded,
        MimeByteLength = mimeByteLength,
        AttemptCount = 0,
        RecordedAt = DateTimeOffset.UnixEpoch,
        StageChangedAt = DateTimeOffset.UnixEpoch,
        LastFailure = null,
        LastReplyCode = null,
    };
}
