// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Time.Testing;
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

    private static OutgoingRecipient Recipient()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        return OutgoingRecipient.Create(address, OutgoingRecipientRole.To);
    }
}
