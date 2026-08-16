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
/// It reproduces the one behavior the callers above depend on and nothing else: a request whose identity already has a
/// record reads that record back rather than producing a second. The constraint that makes it true against a real
/// database is the unique index, which the integration suite proves; here the dictionary stands in for it.
/// </remarks>
internal sealed class InMemoryOutgoingMessageStore : IOutgoingMessageStore
{
    private readonly Dictionary<(string Account, OutgoingMessageOrigin Origin, string Identity), OutgoingMessageRecord>
        recordsByIdentity = [];

    private readonly List<OutgoingMessageRequest> openRequests = [];

    /// <summary>Gets every request that reached the store, including the ones that read a record back.</summary>
    internal IReadOnlyList<OutgoingMessageRequest> OpenRequests => this.openRequests;

    public Task<OutgoingMessageRecord> OpenAsync(
        IPersistenceSession session,
        OutgoingMessageRequest request,
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

        var recorded = new OutgoingMessageRecord
        {
            Id = OutgoingMessageId.Create(Guid.CreateVersion7()),
            AccountId = request.AccountId,
            Requester = request.Requester,
            Recipients = [.. request.Recipients.Select(OutgoingRecipientOutcome.Unanswered)],
            Stage = OutgoingMessageStage.Recorded,
            MimeByteLength = mimeByteLength,
            AttemptCount = 0,
            RecordedAt = DateTimeOffset.UnixEpoch,
            StageChangedAt = DateTimeOffset.UnixEpoch,
            LastFailure = null,
            LastReplyCode = null,
        };

        this.recordsByIdentity[identity] = recorded;

        return Task.FromResult(recorded);
    }

    public Task<OutgoingMessageRecord?> FindAsync(
        OutgoingMessageId outgoingMessageId,
        CancellationToken cancellationToken) => Task.FromResult(
            this.recordsByIdentity.Values.SingleOrDefault(record => record.Id == outgoingMessageId));

    public Task<IReadOnlyList<OutgoingMessageRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        IReadOnlyList<OutgoingMessageRecord> outstanding =
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
        OutgoingMessageId outgoingMessageId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double counts an attempt.");

    public Task RecordTransmissionBegunAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double transmits anything.");

    public Task AdvanceAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        OutgoingMessageStage stage,
        int? replyCode,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double advances a stage.");

    public Task RecordRecipientOutcomesAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        IReadOnlyList<OutgoingRecipientOutcome> outcomes,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double answers about a recipient.");

    public Task RecordFailureAsync(
        IPersistenceSession session,
        OutgoingMessageId outgoingMessageId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("No test above this double records a failure.");
}
