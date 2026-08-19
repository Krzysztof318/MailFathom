// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers the one way a draft is written, revised, or given up, and what each of those owes the mailbox.</summary>
public sealed class MailDraftBookTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Moment = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A saved draft is stored and appended in one call, and needs nobody to be addressed to.</summary>
    [Fact]
    public async Task SaveAsync_NewDraftAddressedToNobody_StoresItAndAppendsItAnyway()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);

        // Act
        var draft = await harness.Book.SaveAsync(
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version", recipients: []),
            revises: null,
            CancellationToken.None);

        // Assert
        Assert.Empty(draft.Recipients);
        Assert.Equal(MailDraftStage.Filed, draft.Stage);
        Assert.Equal(1, harness.AppendCount);
        Assert.Equal("first version", Encoding.ASCII.GetString(harness.Contents.Peek(draft.Id).Span)[^13..]);
    }

    /// <summary>An edit stores the new message over the old one and leaves the owner one draft in the folder.</summary>
    [Fact]
    public async Task SaveAsync_RevisionOfAHeldDraft_ReplacesTheMessageAndTheCopy()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");

        // Act
        var revised = await harness.Book.SaveAsync(
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("second version"),
            draft.Id,
            CancellationToken.None);

        // Assert
        Assert.Equal(draft.Id, revised.Id);
        Assert.Equal(2, revised.Revision);
        Assert.Equal(MailDraftStage.Filed, revised.Stage);
        Assert.Equal(2, harness.AppendCount);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        Assert.Single(harness.Drafts.Drafts);
    }

    /// <summary>Giving up a draft takes back the copy this system appended and removes the record with it.</summary>
    [Fact]
    public async Task DiscardAsync_HeldDraft_WithdrawsOnlyTheOccurrenceItAppended()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");

        // Act
        var result = await harness.Book.DiscardAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Discarded, result.Outcome);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>
    /// A draft this system did not write is unreachable from here: nothing is held under an identifier it never
    /// minted, so the refusal comes before any folder is opened and no message in the mailbox is touched.
    /// </summary>
    [Fact]
    public async Task DiscardAsync_DraftThisSystemNeverWrote_IsRefusedWithoutReachingTheMailbox()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        await SaveAsync(harness, "first version");
        var foreign = MailDraftId.Create(Guid.CreateVersion7(Moment));

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.DiscardAsync(foreign, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Empty(harness.Withdrawn);
        Assert.Single(harness.Drafts.Drafts);
    }

    /// <summary>Revising something this system does not hold is the same answer, so nothing appends over a stranger's mail.</summary>
    [Fact]
    public async Task SaveAsync_RevisingADraftThisSystemNeverWrote_IsRefusedAndAppendsNothing()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                Account,
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed("second version"),
                MailDraftId.Create(Guid.CreateVersion7(Moment)),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Equal(0, harness.AppendCount);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>A draft of another account is refused as one nobody holds, so revising reaches no mailbox but its own.</summary>
    [Fact]
    public async Task SaveAsync_RevisingADraftOfAnotherAccount_IsRefused()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                MailAccountId.Create("personal"),
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed("second version"),
                draft.Id,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Equal(1, harness.Drafts.Peek(draft.Id)!.Revision);
    }

    /// <summary>A caller that may not send may not draft either, because a draft is one command away from being one.</summary>
    [Fact]
    public async Task SaveAsync_CallerWithoutTheSendingGrant_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var harness = Harness(MailFathomPermission.MailRead);
        harness.MapDraftsFolder(Account);

        // Act
        var refusal = () => harness.Book.SaveAsync(
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version"),
            revises: null,
            CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
        Assert.Empty(harness.Drafts.Drafts);
    }

    private static MailDraftHarness Harness(params IEnumerable<MailFathomPermission> permissions) => new(
        new FakeTimeProvider(Moment),
        new InMemoryOutgoingEmailStore(),
        MailOutboxSettings.Create(
            maxDeliveriesPerPass: 10,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(7),
            maxAttempts: 5,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(8)),
        permissions);

    private static Task<MailDraftRecord> SaveAsync(MailDraftHarness harness, string body) =>
        harness.Book.SaveAsync(
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed(body),
            revises: null,
            CancellationToken.None);

    private static ComposedMailDraft Composed(string body, IReadOnlyList<OutgoingRecipient>? recipients = null) =>
        new(
            recipients ?? [Recipient()],
            InternetMessageId.Mint("example.test"),
            Encoding.ASCII.GetBytes($"Subject: a draft\r\n\r\n{body}").AsMemory());

    private static OutgoingRecipient Recipient()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        return OutgoingRecipient.Create(address, OutgoingRecipientRole.To);
    }
}
