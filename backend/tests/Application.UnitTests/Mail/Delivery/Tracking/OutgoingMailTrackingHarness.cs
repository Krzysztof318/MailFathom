// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Tracking;

/// <summary>Arranges a store holding one queued send, and the two use cases that read and withdraw it.</summary>
/// <remarks>
/// Both classes ask the same two questions of the same state — whose send is this, and can it still be stopped — so
/// they are arranged from one place. What a test varies is the principal it calls under, the principal the record was
/// queued under, and how far the record has been taken by an attempt.
/// </remarks>
internal sealed class OutgoingMailTrackingHarness
{
    /// <summary>The account every record here is queued against.</summary>
    internal static readonly MailAccountId Account = MailAccountId.Create("work");

    /// <summary>The identity the calling principal is admitted under unless a test says otherwise.</summary>
    internal const string CallerIdentity = "agent-key";

    internal OutgoingMailTrackingHarness(
        string callerIdentity = CallerIdentity,
        IReadOnlyList<MailFathomPermission>? granted = null)
    {
        var authorization = AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Caller(
            callerIdentity,
            granted ?? [MailFathomPermission.MailSend]));

        this.Store = new InMemoryOutgoingEmailStore(timeProvider: this.Clock);
        this.Reader = new OutgoingMailReader(this.Store, authorization);

        // The withdrawal is the operator's own statement, reached here under the sending grant instead, so the double
        // stands in for that port and performs it against the same rows rather than reimplementing what it decides.
        var outbox = Substitute.For<IOutboxOperationStore>();

        outbox.CancelAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
            .Returns(call => this.Store.Withdraw(call.Arg<OutgoingEmailId>(), this.Clock.GetUtcNow()));

        this.Cancellation = new OutgoingMailCancellation(this.Reader, outbox, authorization);
    }

    /// <summary>Gets the clock every stamp and every lease is judged against.</summary>
    internal FakeTimeProvider Clock { get; } = new(DateTimeOffset.UnixEpoch);

    /// <summary>Gets the store the records live in.</summary>
    internal InMemoryOutgoingEmailStore Store { get; }

    /// <summary>Gets the use case that reads a send back.</summary>
    internal OutgoingMailReader Reader { get; }

    /// <summary>Gets the use case that withdraws one.</summary>
    internal OutgoingMailCancellation Cancellation { get; }

    /// <summary>Writes one queued send into the store, under the principal and origin a test is about.</summary>
    /// <param name="queuedBy">The identity the send was admitted under, or absent for a record written before principals were kept.</param>
    /// <param name="origin">What asked for it.</param>
    /// <param name="identity">The idempotency identity, which only has to differ between records in one test.</param>
    /// <returns>The record now in the store.</returns>
    internal OutgoingEmailRecord Queue(
        string? queuedBy = CallerIdentity,
        OutgoingEmailOrigin origin = OutgoingEmailOrigin.Command,
        string identity = "send-1")
    {
        var requester = origin == OutgoingEmailOrigin.Command
            ? OutgoingEmailRequester.Command(identity)
            : OutgoingEmailRequester.Rule("archive", "r1", StoredEmailId.Create(Guid.CreateVersion7()));

        var request = OutgoingEmailRequest.Create(
            Account,
            requester,
            [OutgoingRecipient.Create(Address("anna@example.test"), OutgoingRecipientRole.To)]);

        return this.Store.Publish(
            request,
            mimeByteLength: 512,
            queuedBy is null ? null : OutgoingEmailPrincipal.Of(queuedBy));
    }

    /// <summary>Hands the record to a delivery attempt that still holds it, without transmitting anything.</summary>
    /// <param name="outgoingEmailId">The record to hand over.</param>
    /// <returns>The lease that attempt holds it under.</returns>
    internal OutgoingEmailLease HandToADeliveryAttempt(OutgoingEmailId outgoingEmailId) =>
        this.Store.Reassign(outgoingEmailId);

    /// <summary>Takes one record to the stage at which its body has begun to go out.</summary>
    /// <param name="outgoingEmailId">The record to advance.</param>
    /// <returns>A task that completes when the stage is written.</returns>
    internal async Task BeginTransmissionAsync(OutgoingEmailId outgoingEmailId)
    {
        var lease = this.HandToADeliveryAttempt(outgoingEmailId);

        await using var session = new CommittingSession();

        await this.Store.RecordTransmissionBegunAsync(
            session,
            lease,
            outgoingEmailId,
            TestContext.Current.CancellationToken);
    }

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var parsed))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return parsed;
    }

    /// <summary>A session that commits, because nothing here is about a conflict the policy has to retry.</summary>
    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
