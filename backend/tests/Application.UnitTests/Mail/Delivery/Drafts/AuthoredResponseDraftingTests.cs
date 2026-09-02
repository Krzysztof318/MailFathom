// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers what asking to draft an answer to stored mail refuses, and what it leaves behind when it does.</summary>
/// <remarks>
/// What the answer itself is made of — the quotation, the threading identifiers, the carried files — belongs to the
/// authoring beneath this and is covered where that lives. What is asserted here is the half this use case owns: the
/// grant, the bound it checks before the stored mail is read, and that a refusal writes no draft.
/// </remarks>
public sealed class AuthoredResponseDraftingTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Moment = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An answer to a message this deployment does not hold is refused, and no folder is opened for it.</summary>
    [Fact]
    public async Task SaveAsync_AnsweredEmailThisDeploymentDoesNotHold_IsRefusedAndWritesNoDraft()
    {
        // Arrange
        var harness = Harness();
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        var drafting = DraftingOver(harness, summaries);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => drafting.SaveAsync(Request(), CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.AnsweredEmailUnavailable, refusal.ErrorCode);
        Assert.Empty(harness.Drafts.Drafts);
        Assert.Equal(0, harness.AppendCount);
    }

    /// <summary>
    /// A recipient list longer than any record could hold is refused before the answered mail is read, because the
    /// read carries what the caller supplied.
    /// </summary>
    [Fact]
    public async Task SaveAsync_MoreRecipientsThanARecordCanHold_IsRefusedBeforeTheStoredMailIsRead()
    {
        // Arrange
        var harness = Harness();
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        var drafting = DraftingOver(harness, summaries);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => drafting.SaveAsync(
                Request() with
                {
                    Recipients =
                    [
                        .. Enumerable
                            .Range(0, OutgoingEmailRequest.MaximumRecipientCount + 1)
                            .Select(ordinal => NamedRecipient.AtAddress(
                                OutgoingRecipientRole.To,
                                $"person{ordinal}@example.test")),
                    ],
                },
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Empty(summaries.ReceivedCalls());
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>Drafting is its own grant, and reading the mail an answer would quote is not it.</summary>
    [Fact]
    public async Task SaveAsync_CallerWithoutTheDraftingGrant_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        var harness = Harness();
        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        var drafting = DraftingOver(
            harness,
            summaries,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = () => drafting.SaveAsync(Request(), CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
        Assert.Empty(summaries.ReceivedCalls());
    }

    /// <summary>A file staged against the draft joins the files the answered message already carried, rather than replacing them.</summary>
    /// <remarks>
    /// A revision re-authors from the answered email every time, so what the author uploaded is not in what the
    /// authoring produced and has to be appended to it. The message being forwarded carries a file of its own here, so
    /// a revision that lost the original's file, one that lost the uploaded file, and one that composed them the other
    /// way round are all held against — an author who attaches something to a forward means it to arrive after what
    /// they forwarded rather than in front of it.
    /// </remarks>
    [Fact]
    public async Task SaveAsync_ARevisionOfADraftCarryingAStagedFile_ComposesItAfterWhatTheAnsweredMessageCarried()
    {
        // Arrange
        var harness = Harness();
        var drafting = DraftingOverAnsweredMail(harness, out var composer);

        var draft = await drafting.SaveAsync(Forward(), TestContext.Current.CancellationToken);

        await harness.Drafts.StageAttachmentAsync(
            Substitute.For<IPersistenceSession>(),
            draft.Id,
            new AuthoredEmailAttachment("report.pdf", "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7").AsMemory()),
            Moment,
            TestContext.Current.CancellationToken);

        // Act
        await drafting.SaveAsync(
            Forward() with { Revises = draft.Id, PlainTextBody = "Thank you, again." },
            TestContext.Current.CancellationToken);

        // Assert
        var composed = (AuthoredEmail)composer
            .ReceivedCalls()
            .Last(call => call.GetMethodInfo().Name == nameof(IAuthoredEmailComposer.ComposeDraft))
            .GetArguments()[1]!;

        Assert.Equal(["carried.pdf", "report.pdf"], composed.Attachments.Select(file => file.FileName));
    }

    private static MailResponseDraftRequest Request() => new()
    {
        AnsweredEmailId = StoredEmailId.Create(Guid.CreateVersion7(Moment)),
        Act = AuthoredResponseAct.Reply,
        PlainTextBody = "Thank you.",
        Author = OutgoingEmailRequester.Command("mfctl-4f2a"),
    };

    /// <summary>Asks to forward the answered message, which is the act that carries that message's own files.</summary>
    private static MailResponseDraftRequest Forward() => Request() with
    {
        Act = AuthoredResponseAct.Forward,
        Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.To, "someone@example.test")],
    };

    private static MailDraftHarness Harness()
    {
        var harness = new MailDraftHarness(
            new FakeTimeProvider(Moment),
            new InMemoryOutgoingEmailStore(),
            MailOutboxSettings.Create(
                maxDeliveriesPerPass: 10,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(8)));

        harness.MapDraftsFolder(Account);

        return harness;
    }

    private static AuthoredResponseDrafting DraftingOver(
        MailDraftHarness harness,
        IStoredEmailSummaryReader summaries,
        AccessAuthorization? authorization = null)
    {
        var granted = authorization ?? AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailDraftsWrite,
            MailFathomPermission.MailRead);

        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(Account)]);

        var authoring = new StoredEmailResponseAuthoring(
            summaries,
            ContentStores.Substituted(),
            Substitute.For<IEmailContentRenderer>(),
            Substitute.For<IEmailAttachmentContentReader>(),
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Nothing,
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            Substitute.For<IOutgoingSenderIdentityReader>(),
            new NamedRecipientResolver(new InMemoryContactBookStore(), ContactBookOwnerships.For(granted)),
            Bounds(),
            granted);

        return new AuthoredResponseDrafting(
            authoring,
            Substitute.For<IAuthoredEmailComposer>(),
            harness.Book,
            granted);
    }

    /// <summary>Builds the drafting over an answered message this deployment does hold, which is what a save needs.</summary>
    /// <remarks>
    /// Every other test here is a refusal and reaches nothing, so the arrangement lives beside them rather than in the
    /// shared helper: the summary, the stored copy, the rendering, the sender identity, and the folder the answered
    /// message sits in are all facts a save reads and a refusal never gets to.
    /// </remarks>
    private static AuthoredResponseDrafting DraftingOverAnsweredMail(
        MailDraftHarness harness,
        out IAuthoredEmailComposer composer)
    {
        var granted = AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailDraftsWrite,
            MailFathomPermission.MailRead);

        var answered = SyntheticEmailSummaries.Create(accountId: Account.Value, subject: "Third-quarter numbers");

        var summaries = Substitute.For<IStoredEmailSummaryReader>();
        summaries.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<EmailSummary?>(answered));

        var storedMime = Encoding.UTF8.GetBytes("From: author@example.test\r\n\r\nBody");
        var contentStore = ContentStores.Substituted();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(
                new StoredEmailContent(storedMime, storedMime.Length, SHA256.HashData(storedMime))));

        const string PlainText = "The numbers are attached.";
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(new EmailContentRendering(
                new EmailContentHeaders(
                    "Third-quarter numbers",
                    Moment,
                    Moment,
                    [new EmailParticipant(EmailAddressRole.From, Address("author@example.test"))],
                    EmailThreadReferences.Create("parent@example.test", inReplyTo: null, references: null)),
                new EmailBodyRepresentation(PlainText, PlainText.Length, EmailBodyTruncation.None),
                null,
                new EmailBodyForms(PlainText: true, Html: false),
                false,
                EmailAttachmentSummary.Create(
                    [Carried],
                    inlineResourceCount: 0,
                    false,
                    carriesUnverifiedSignature: false,
                    containsUnexpandedTnefPart: false),
                [Carried]))));

        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        senderIdentities
            .FindSenderIdentity(Arg.Any<MailAccountId>())
            .Returns(OutgoingSenderIdentity.Create(Account, Address("owner@example.test")));

        var attachmentContents = Substitute.For<IEmailAttachmentContentReader>();
        attachmentContents
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(OpenedEmailAttachmentResult.Opened(new StubOpenedEmailAttachment())));

        var catalog = Substitute.For<ICallerMailAccountCatalog>();
        catalog.OwnedAccounts.Returns([SyntheticServedAccount.Of(Account)]);

        var authoring = new StoredEmailResponseAuthoring(
            summaries,
            contentStore,
            renderer,
            attachmentContents,
            Substitute.For<IEmailContentRepairRequestStore>(),
            new MailboxScopeResolver(
                catalog,
                StubMailFolderParticipation.Mapping(new MailFolderIdentity(answered.AccountId, answered.FolderAlias)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            senderIdentities,
            new NamedRecipientResolver(new InMemoryContactBookStore(), ContactBookOwnerships.For(granted)),
            Bounds(),
            granted);

        composer = ComposingAuthoredEmails.ThatComposesDrafts(
            Encoding.ASCII.GetBytes("Subject: an answer\r\n\r\nThank you.").AsMemory());

        return new AuthoredResponseDrafting(authoring, composer, harness.Book, granted);
    }

    /// <summary>The one file the answered message carries, which a forward brings into the draft ahead of any upload.</summary>
    private static ExtractedEmailAttachment Carried => new(
        AttachmentFileName.TryNormalize("carried.pdf", out var normalized) ? normalized : null,
        "application/pdf",
        8);

    /// <summary>Reads one address, failing the suite rather than the code when the literal names no mailbox.</summary>
    private static EmailAddress Address(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return emailAddress;
    }

    private static OutgoingEmailBounds Bounds() => new()
    {
        MaxRecipientCount = 8,
        MaxBodyCharacters = 4096,
        MaxAttachmentCount = 3,
        MaxAttachmentBytes = 128,
        MaxMessageBytes = 300,
    };

    /// <summary>The answered message's own file as the authoring opens it, which is what a forward writes into the draft.</summary>
    private sealed class StubOpenedEmailAttachment : IOpenedEmailAttachment
    {
        public ExtractedEmailAttachment Description => Carried;

        public Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);

            return destination.WriteAsync(Encoding.UTF8.GetBytes("carried!!"), cancellationToken).AsTask();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
