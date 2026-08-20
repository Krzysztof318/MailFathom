// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers the pass that finishes what a stopped process, or an unreachable server, left a draft owing.</summary>
public sealed class MailDraftPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Moment = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An account whose drafts are all settled costs one read and reaches no mail server at all.</summary>
    [Fact]
    public async Task SettleOutstandingAsync_EverythingAlreadySettled_ReachesNoMailServer()
    {
        // Arrange
        var harness = Harness();
        await SaveAsync(harness, "first version");

        // Act
        var results = await harness.Pass.SettleOutstandingAsync(Account, CancellationToken.None);

        // Assert
        Assert.Empty(results);
        Assert.Equal(1, harness.AppendCount);
        Assert.Empty(harness.Withdrawn);
    }

    /// <summary>A draft whose folder was unreachable when it was written is appended by the pass that follows.</summary>
    [Fact]
    public async Task SettleOutstandingAsync_DraftNothingCouldAppendYet_AppendsItOnTheNextPass()
    {
        // Arrange
        var harness = new MailDraftHarness(
            new FakeTimeProvider(Moment),
            new InMemoryOutgoingEmailStore(),
            Settings());

        var draft = await SaveAsync(harness, "first version");
        Assert.Equal(MailDraftStage.Composed, harness.Drafts.Peek(draft.Id)!.Stage);

        harness.MapDraftsFolder(Account);

        // Act
        var results = await harness.Pass.SettleOutstandingAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Filed, Assert.Single(results).Outcome);
        Assert.Equal(1, harness.AppendCount);
        Assert.Equal(MailDraftStage.Filed, harness.Drafts.Peek(draft.Id)!.Stage);
    }

    /// <summary>A draft of another account is left to that account's own pass.</summary>
    [Fact]
    public async Task SettleOutstandingAsync_DraftOfAnotherAccount_IsLeftAlone()
    {
        // Arrange
        var harness = new MailDraftHarness(
            new FakeTimeProvider(Moment),
            new InMemoryOutgoingEmailStore(),
            Settings());

        await SaveAsync(harness, "first version");
        harness.MapDraftsFolder(Account);

        // Act
        var results = await harness.Pass.SettleOutstandingAsync(
            MailAccountId.Create("personal"),
            CancellationToken.None);

        // Assert
        Assert.Empty(results);
        Assert.Equal(0, harness.AppendCount);
    }

    /// <summary>
    /// The mark that gives a promoted draft up is written by the pass that delivered its send, and once. A process
    /// that died between the delivery and that write leaves a draft whose copy stands in the owner's folder for a
    /// message already on its way, so the next pass is what has to reach it.
    /// </summary>
    [Fact]
    public async Task SettleOutstandingAsync_PromotedDraftWhoseGiveUpNeverCommitted_WithdrawsTheCopyOnTheNextPass()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var harness = new MailDraftHarness(new FakeTimeProvider(Moment), outgoingEmails, Settings());
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");
        await PromoteAsync(harness, outgoingEmails, draft, OutgoingEmailStage.Sent);

        // Act
        var results = await harness.Pass.SettleOutstandingAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Discarded, Assert.Single(results).Outcome);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>A promotion still waiting to be delivered leaves the message the owner wrote exactly where it is.</summary>
    [Fact]
    public async Task SettleOutstandingAsync_PromotedDraftWhoseSendIsStillQueued_LeavesTheCopyStanding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var harness = new MailDraftHarness(new FakeTimeProvider(Moment), outgoingEmails, Settings());
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");
        await PromoteAsync(harness, outgoingEmails, draft, OutgoingEmailStage.Recorded);

        // Act
        var results = await harness.Pass.SettleOutstandingAsync(Account, CancellationToken.None);

        // Assert
        Assert.Empty(results);
        Assert.Empty(harness.Withdrawn);
        Assert.Equal(MailDraftStage.Filed, harness.Drafts.Peek(draft.Id)!.Stage);
    }

    private static async Task PromoteAsync(
        MailDraftHarness harness,
        InMemoryOutgoingEmailStore outgoingEmails,
        MailDraftRecord draft,
        OutgoingEmailStage stage)
    {
        var send = outgoingEmails.Publish(
            OutgoingEmailRequest.Create(
                Account,
                OutgoingEmailRequester.Draft(draft.Id),
                [.. draft.Recipients.Select(recipient => recipient.Recipient)]),
            mimeByteLength: 64);

        outgoingEmails.Arrange(send.Id, stage);

        // Written without the give-up that ordinarily follows it, which is the state a crash between the two leaves.
        await harness.Drafts.RecordPromotedAsync(
            Substitute.For<IPersistenceSession>(),
            draft.Id,
            send.Id,
            CancellationToken.None);
    }

    private static MailDraftHarness Harness()
    {
        var harness = new MailDraftHarness(
            new FakeTimeProvider(Moment),
            new InMemoryOutgoingEmailStore(),
            Settings());

        harness.MapDraftsFolder(Account);

        return harness;
    }

    private static MailOutboxSettings Settings() => MailOutboxSettings.Create(
        maxDeliveriesPerPass: 10,
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(7),
        maxAttempts: 5,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(8));

    private static Task<MailDraftRecord> SaveAsync(MailDraftHarness harness, string body) =>
        harness.Book.SaveAsync(
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            new ComposedMailDraft(
                [Recipient()],
                InternetMessageId.Mint("example.test"),
                Encoding.ASCII.GetBytes($"Subject: a draft\r\n\r\n{body}").AsMemory()),
            revises: null,
            CancellationToken.None);

    private static MailDraftRecipient Recipient()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        return new MailDraftRecipient(
            OutgoingRecipient.Create(address, OutgoingRecipientRole.To),
            AuthoredRecipientProvenance.NamedByCaller);
    }
}
