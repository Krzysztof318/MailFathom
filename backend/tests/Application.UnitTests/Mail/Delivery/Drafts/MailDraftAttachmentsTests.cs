// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers taking files onto a draft and back off it, which is how an author attaches anything at all.</summary>
public sealed class MailDraftAttachmentsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    /// <summary>A staged file joins the draft and is described by what it was uploaded as.</summary>
    [Fact]
    public async Task StageAsync_AFileOnADraftThisOwnerIsWriting_JoinsTheDraftAsItWasUploaded()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);
        var attachments = AttachmentsOver(drafts);

        // Act
        var staged = await attachments.StageAsync(
            draft.Id,
            File("report.pdf", "application/pdf", 2048),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("report.pdf", staged.FileName);
        Assert.Equal("application/pdf", staged.MediaType);
        Assert.Equal(2048, staged.ByteLength);
        Assert.Equal([staged.Id], drafts.Peek(draft.Id)!.Attachments.Select(attachment => attachment.Id));
    }

    /// <summary>The composition reads the octets back, which is what makes a later revision carry the file.</summary>
    [Fact]
    public async Task StageAsync_AFileAlreadyStaged_IsReadBackWithItsOctetsForTheNextComposition()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);
        var attachments = AttachmentsOver(drafts);

        await attachments.StageAsync(
            draft.Id,
            File("note.txt", "text/plain", "Remember the milk."),
            TestContext.Current.CancellationToken);

        // Act
        var content = await drafts.ReadAttachmentContentAsync(draft.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["Remember the milk."],
            content.Select(file => Encoding.UTF8.GetString(file.Content.Span)));
    }

    /// <summary>A file larger than this deployment composes is refused, naming the bound rather than the file.</summary>
    [Fact]
    public async Task StageAsync_AFileLargerThanThisDeploymentComposes_IsRefusedNamingTheBound()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);
        var attachments = AttachmentsOver(drafts);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => attachments.StageAsync(
                draft.Id,
                File("huge.bin", "application/octet-stream", 4096),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Empty(drafts.Peek(draft.Id)!.Attachments);
    }

    /// <summary>A draft already carrying as many files as a message may is refused another.</summary>
    [Fact]
    public async Task StageAsync_ADraftAlreadyCarryingAsManyFilesAsAMessageMay_IsRefused()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);
        var attachments = AttachmentsOver(drafts);

        await attachments.StageAsync(draft.Id, File("one.txt", "text/plain", 8), TestContext.Current.CancellationToken);
        await attachments.StageAsync(draft.Id, File("two.txt", "text/plain", 8), TestContext.Current.CancellationToken);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => attachments.StageAsync(
                draft.Id,
                File("three.txt", "text/plain", 8),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Equal(2, drafts.Peek(draft.Id)!.Attachments.Count);
    }

    /// <summary>A file named by nothing at all is refused, because a header cannot carry it.</summary>
    [Fact]
    public async Task StageAsync_AFileTheAuthorNamedWithNothing_IsRefused()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);
        var attachments = AttachmentsOver(drafts);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => attachments.StageAsync(
                draft.Id,
                File(string.Empty, "text/plain", 8),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
    }

    /// <summary>A draft another owner holds is refused as one nobody holds, so no file reaches it.</summary>
    [Fact]
    public async Task StageAsync_ADraftAnotherOwnerHolds_IsRefusedAsOneNobodyHolds()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var theirs = await OpenAsync(drafts, SyntheticMailOwner.Another);
        var attachments = AttachmentsOver(drafts);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => attachments.StageAsync(
                theirs.Id,
                File("note.txt", "text/plain", 8),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Empty(drafts.Peek(theirs.Id)!.Attachments);
    }

    /// <summary>Taking a file off removes it, and taking it off again answers that the draft carries no such file.</summary>
    [Fact]
    public async Task UnstageAsync_AFileTakenOffTwice_RemovesItOnceAndReportsTheSecondAsCarryingNone()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);
        var attachments = AttachmentsOver(drafts);

        var staged = await attachments.StageAsync(
            draft.Id,
            File("note.txt", "text/plain", 8),
            TestContext.Current.CancellationToken);

        // Act
        var first = await attachments.UnstageAsync(draft.Id, staged.Id, TestContext.Current.CancellationToken);
        var second = await attachments.UnstageAsync(draft.Id, staged.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(first);
        Assert.False(second);
        Assert.Empty(drafts.Peek(draft.Id)!.Attachments);
    }

    /// <summary>Attaching a file is writing the draft, so a caller holding only the sending grant is refused.</summary>
    [Fact]
    public async Task StageAsync_CallerHoldingOnlyTheSendingGrant_IsRefused()
    {
        // Arrange
        var drafts = new InMemoryMailDraftStore();
        var draft = await OpenAsync(drafts, SyntheticMailOwner.Deployment);
        var attachments = AttachmentsOver(
            drafts,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend));

        // Act
        var refusal = () => attachments.StageAsync(
            draft.Id,
            File("note.txt", "text/plain", 8),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>Builds the staging over the store a test arranged, under bounds small enough to reach in a test.</summary>
    private static MailDraftAttachments AttachmentsOver(
        InMemoryMailDraftStore drafts,
        AccessAuthorization? authorization = null)
    {
        var callerAuthorization =
            authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailDraftsWrite);

        return new MailDraftAttachments(
            new MailDraftDirectory(
                OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Work)),
                drafts,
                new InMemoryMailDraftContentStore(),
                Substitute.For<IOutgoingMailTextReader>(),
                callerAuthorization),
            drafts,
            CommittingPolicy(),
            new OutgoingEmailBounds
            {
                MaxRecipientCount = 16,
                MaxBodyCharacters = 100_000,
                MaxAttachmentCount = 2,
                MaxAttachmentBytes = 2048,
                MaxMessageBytes = 1_000_000,
            },
            callerAuthorization,
            new FakeTimeProvider(Moment));
    }

    /// <summary>Builds a commit policy whose every attempt commits, which is what a test about staging needs.</summary>
    private static OptimisticConcurrencyRetryPolicy CommittingPolicy()
    {
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new OptimisticConcurrencyRetryPolicy(
            sessions,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider(Moment));
    }

    /// <summary>Builds one uploaded file of a stated size.</summary>
    private static AuthoredEmailAttachment File(string fileName, string mediaType, int byteLength) =>
        new(fileName, mediaType, new byte[byteLength].AsMemory());

    /// <summary>Builds one uploaded file carrying stated text, for a test that reads the octets back.</summary>
    private static AuthoredEmailAttachment File(string fileName, string mediaType, string content) =>
        new(fileName, mediaType, Encoding.UTF8.GetBytes(content).AsMemory());

    /// <summary>Writes one draft down for one owner, which is the arrangement every test here starts from.</summary>
    private static Task<MailDraftRecord> OpenAsync(InMemoryMailDraftStore drafts, MailOwnerId owner) =>
        drafts.OpenAsync(
            Substitute.For<IPersistenceSession>(),
            MailAccountIdentity.Create(owner, Work),
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            [],
            "a draft",
            mimeByteLength: 64,
            Moment,
            TestContext.Current.CancellationToken);
}
