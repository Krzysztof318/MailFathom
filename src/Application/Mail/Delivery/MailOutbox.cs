// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery;

/// <summary>Takes a message somebody authored and makes it durable before anything can try to send it.</summary>
/// <remarks>
/// <para>
/// This is the one way into the outbox, and it exists because the two writes beneath it are one decision. A record
/// whose message was never stored describes a send with nothing to transmit; a message stored under no record is bytes
/// nothing will ever read. Both cross the same transaction here, so a crash between them leaves neither rather than
/// half of a send.
/// </para>
/// <para>
/// Enqueuing is idempotent by the identity the request carries. The same authored request arriving twice — a rule that
/// ran again, a retried command, a client that resent a call — reads back the record the first one wrote and stores
/// nothing further, so it produces one delivery. What decides that is the unique constraint under the store rather than
/// any check here: two callers arriving together both reach the database, and the loser's retry finds the winner's row.
/// </para>
/// <para>
/// Nothing is sent by this. The record it leaves is at <see cref="OutgoingMessageStage.Recorded" /> with every recipient
/// unanswered, which is the state a delivery attempt reads and continues from.
/// </para>
/// </remarks>
/// <param name="outgoingMessages">Holds the durable record and its idempotency identity.</param>
/// <param name="contentStore">Holds the composed MIME the record points at.</param>
/// <param name="retryPolicy">Commits both writes together and resolves a lost race for the same identity.</param>
public sealed class MailOutbox(
    IOutgoingMessageStore outgoingMessages,
    IEmailContentStore contentStore,
    OptimisticConcurrencyRetryPolicy retryPolicy)
{
    /// <summary>Writes down a message to be sent, or answers with the record an identical request already left.</summary>
    /// <param name="request">The send that was asked for.</param>
    /// <param name="rawMime">The composed RFC 822 bytes to transmit, stored once and read back for every attempt.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The durable record for this request, whether this call created it or an earlier one did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when the write lost its race for the same identity on every allowed attempt.</exception>
    /// <remarks>
    /// The message is not recomposed for a request that already has a record, and the bytes supplied here are then
    /// ignored rather than written over the stored ones. That is what keeps a resumed send one message: a
    /// <c>Message-ID</c> that changed between attempts would thread as a second message in every recipient's client.
    /// </remarks>
    public Task<OutgoingMessageRecord> EnqueueAsync(
        OutgoingMessageRequest request,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (rawMime.IsEmpty)
        {
            throw new ArgumentException(
                "An outgoing message is recorded with the MIME it will be transmitted as.",
                nameof(rawMime));
        }

        return retryPolicy.CommitAsync(
            async (session, attemptCancellationToken) =>
            {
                var record = await outgoingMessages.OpenAsync(
                    session,
                    request,
                    rawMime.Length,
                    attemptCancellationToken);

                await contentStore.SaveOutgoingContentAsync(
                    session,
                    record.Id,
                    rawMime,
                    attemptCancellationToken);

                return record;
            },
            cancellationToken);
    }
}
