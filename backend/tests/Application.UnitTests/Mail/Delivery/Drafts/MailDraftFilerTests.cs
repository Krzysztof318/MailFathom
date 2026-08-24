// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers what one draft owes the mailbox, including everything a process that stopped mid-way left behind.</summary>
public sealed class MailDraftFilerTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Moment = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A draft nobody has appended is appended, with the flag that makes a mail client read it as a draft.</summary>
    [Fact]
    public async Task SettleAsync_DraftNeverAppended_AppendsItAsADraft()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await OpenAsync(harness, "first version");

        // Act
        var result = await harness.Filer.SettleAsync(draft, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Filed, result.Outcome);
        await harness.WriteSession.Received(1).AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            OutgoingMailFiling.Draft.Flags,
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(MailDraftStage.Filed, harness.Drafts.Peek(draft.Id)!.Stage);
    }

    /// <summary>A settled draft is left alone, so a pass over a mailbox nobody edited reaches no mail server twice.</summary>
    [Fact]
    public async Task SettleAsync_DraftAlreadyStanding_AsksTheServerForNothing()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await OpenAsync(harness, "first version");
        await harness.Filer.SettleAsync(draft, CancellationToken.None);

        // Act
        var result = await harness.Filer.SettleAsync(harness.Drafts.Peek(draft.Id)!, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.AlreadySettled, result.Outcome);
        Assert.Equal(1, harness.AppendCount);
        Assert.Empty(harness.Withdrawn);
    }

    /// <summary>An edit appends the new version and takes the old one out, which leaves the owner one draft.</summary>
    [Fact]
    public async Task SettleAsync_RevisedDraft_AppendsTheNewVersionThenWithdrawsTheOld()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await OpenAsync(harness, "first version");
        await harness.Filer.SettleAsync(draft, CancellationToken.None);
        var revised = await ReviseAsync(harness, draft.Id, "second version");

        // Act
        var result = await harness.Filer.SettleAsync(revised, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Replaced, result.Outcome);
        Assert.Equal(2, harness.AppendCount);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        var settled = harness.Drafts.Peek(draft.Id)!;
        Assert.Equal(MailDraftStage.Filed, settled.Stage);
        Assert.Equal(2, settled.CurrentCopy!.Revision);
    }

    /// <summary>
    /// A process that stopped between the two commands of a replacement left one draft in the folder and a record
    /// saying which copy is which, so the pass that follows removes exactly the copy the edit replaced.
    /// </summary>
    /// <remarks>
    /// The crash is expressed by settling the revision under a filer whose withdrawal fails, which is the shape a
    /// process that died after the append and before the removal leaves: the new copy is in the folder, the old one is
    /// still standing, and nothing has told the record otherwise.
    /// </remarks>
    [Fact]
    public async Task SettleAsync_ResumedAfterAppendWithoutRemoval_WithdrawsOnlyTheReplacedCopy()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await OpenAsync(harness, "first version");
        await harness.Filer.SettleAsync(draft, CancellationToken.None);
        var revised = await ReviseAsync(harness, draft.Id, "second version");

        harness.Withdraw = (_, _) => Task.FromException(
            new MailboxUnavailableException(Account, new InvalidOperationException("unreachable")));

        var crashed = await harness.Filer.SettleAsync(revised, CancellationToken.None);

        var interrupted = harness.Drafts.Peek(draft.Id)!;
        Assert.Equal(MailDraftFilingOutcome.Failed, crashed.Outcome);
        Assert.Equal(MailDraftStage.ReplacementRemovalPending, interrupted.Stage);

        harness.Withdraw = (_, _) => Task.CompletedTask;

        // Act
        var resumed = await harness.Filer.SettleAsync(interrupted, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Replaced, resumed.Outcome);
        Assert.Equal(2, harness.AppendCount);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        Assert.Equal(MailDraftStage.Filed, harness.Drafts.Peek(draft.Id)!.Stage);
    }

    /// <summary>
    /// A copy the drafts role no longer resolves to is somebody else's mail as far as this system can prove, so it is
    /// left where it is and the divergence is written down.
    /// </summary>
    [Fact]
    public async Task SettleAsync_TrackedCopyInAFolderTheRoleNoLongerNames_LeavesItAndRecordsTheDivergence()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account, "drafts", "INBOX.Drafts");
        var draft = await OpenAsync(harness, "first version");
        await harness.Filer.SettleAsync(draft, CancellationToken.None);
        var revised = await ReviseAsync(harness, draft.Id, "second version");
        harness.MapDraftsFolder(Account, "drafts", "INBOX.OldDrafts");

        // Act
        var result = await harness.Filer.SettleAsync(revised, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Diverged, result.Outcome);
        Assert.Equal(MailDraftDivergenceReason.DestinationChanged, result.Divergence);
        Assert.Empty(harness.Withdrawn);
        var diverged = harness.Drafts.Peek(draft.Id)!;
        Assert.Equal(MailDraftDivergenceReason.DestinationChanged, diverged.Divergence!.Reason);
        Assert.Equal(MailDraftCopyStage.Abandoned, diverged.FindCopy(1)!.Stage);
    }

    /// <summary>A copy the server named no occurrence for cannot be pointed at, so nothing is expunged for it.</summary>
    [Fact]
    public async Task SettleAsync_ServerNamedNoPlacement_LeavesTheCopyAndRecordsTheDivergence()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        harness.Append = _ => Task.FromResult(
            new AppendedMailCopy(RemoteEmailPlacement.NotReported(), InternetMessageId: null));
        var draft = await OpenAsync(harness, "first version");
        await harness.Filer.SettleAsync(draft, CancellationToken.None);
        var revised = await ReviseAsync(harness, draft.Id, "second version");

        // Act
        var result = await harness.Filer.SettleAsync(revised, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftDivergenceReason.PlacementUnreported, result.Divergence);
        Assert.Empty(harness.Withdrawn);
    }

    /// <summary>An append the server never answered stops every later command, because nothing can name what it left.</summary>
    [Fact]
    public async Task SettleAsync_AppendTheServerNeverAnswered_IssuesNothingFurther()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        harness.Append = _ => Task.FromException<AppendedMailCopy>(
            new MailboxUnavailableException(Account, new InvalidOperationException("unreachable")));
        var draft = await OpenAsync(harness, "first version");

        var unknown = await harness.Filer.SettleAsync(draft, CancellationToken.None);
        Assert.Equal(MailDraftFilingOutcome.OutcomeUnknown, unknown.Outcome);

        // Act
        var result = await harness.Filer.SettleAsync(harness.Drafts.Peek(draft.Id)!, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.OutcomeUnknown, result.Outcome);
        Assert.Equal(MailDraftDivergenceReason.AppendOutcomeUnknown, result.Divergence);
        Assert.Equal(1, harness.AppendCount);
        Assert.Equal(MailDraftStage.AppendIssued, harness.Drafts.Peek(draft.Id)!.Stage);
    }

    /// <summary>A given-up draft has its copy taken out of the folder and its record removed with it.</summary>
    [Fact]
    public async Task SettleAsync_DiscardedDraft_WithdrawsTheCopyAndRemovesTheRecord()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await OpenAsync(harness, "first version");
        await harness.Filer.SettleAsync(draft, CancellationToken.None);
        await harness.Drafts.RecordDiscardedAsync(
            Substitute.For<IPersistenceSession>(),
            draft.Id,
            Moment,
            CancellationToken.None);

        // Act
        var result = await harness.Filer.SettleAsync(harness.Drafts.Peek(draft.Id)!, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Discarded, result.Outcome);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        Assert.Null(harness.Drafts.Peek(draft.Id));
    }

    /// <summary>An account whose drafts folder cannot be resolved is reported, and nothing is appended into nowhere.</summary>
    [Fact]
    public async Task SettleAsync_NoFolderPlaysTheDraftsRole_ReportsTheDestinationAsUnavailable()
    {
        // Arrange
        var harness = Harness();
        var draft = await OpenAsync(harness, "first version");

        // Act
        var result = await harness.Filer.SettleAsync(draft, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.DestinationUnavailable, result.Outcome);
        Assert.Equal(0, harness.AppendCount);
        Assert.NotNull(harness.Drafts.Peek(draft.Id)!.LastFailure);
    }

    private static MailDraftHarness Harness() => new(
        new FakeTimeProvider(Moment),
        new InMemoryOutgoingEmailStore(),
        Settings());

    private static MailOutboxSettings Settings() => MailOutboxSettings.Create(
        maxDeliveriesPerPass: 10,
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(7),
        maxAttempts: 5,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(8));

    private static async Task<MailDraftRecord> OpenAsync(MailDraftHarness harness, string body)
    {
        var session = Substitute.For<IPersistenceSession>();
        var mime = MimeOf(body);

        var draft = await harness.Drafts.OpenAsync(
            session,
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            [Recipient()],
            mime.Length,
            Moment,
            CancellationToken.None);

        await harness.Contents.SaveMailDraftContentAsync(
            session,
            draft.Id,
            PlacedEmailContent.InDatabase(mime),
            CancellationToken.None);

        return draft;
    }

    private static async Task<MailDraftRecord> ReviseAsync(
        MailDraftHarness harness,
        MailDraftId draftId,
        string body)
    {
        var session = Substitute.For<IPersistenceSession>();
        var mime = MimeOf(body);

        var revised = await harness.Drafts.ReviseAsync(
            session,
            draftId,
            [Recipient()],
            mime.Length,
            Moment.AddMinutes(1),
            CancellationToken.None);

        await harness.Contents.SaveMailDraftContentAsync(
            session,
            draftId,
            PlacedEmailContent.InDatabase(mime),
            CancellationToken.None);

        return revised;
    }

    private static ReadOnlyMemory<byte> MimeOf(string body) =>
        Encoding.ASCII.GetBytes($"Subject: a draft\r\n\r\n{body}").AsMemory();

    private static MailDraftRecipient Recipient()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        return new MailDraftRecipient(
            OutgoingRecipient.Create(address, OutgoingRecipientRole.To),
            AuthoredRecipientProvenance.NamedByCaller);
    }
}
