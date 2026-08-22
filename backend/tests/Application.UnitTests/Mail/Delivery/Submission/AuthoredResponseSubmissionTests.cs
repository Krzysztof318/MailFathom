// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Submission;

/// <summary>
/// Covers queueing the two sends that begin from mail this deployment already holds: which grants are asked for and in
/// which order, that the account and the addressing are the answered email's rather than the caller's, and that every
/// answer this deployment will not author becomes one refusal a caller can act on and learn nothing from.
/// </summary>
/// <remarks>
/// The use case runs over the real <see cref="StoredEmailResponseAuthoring" /> rather than a stand-in, because what is
/// under test is the composition of the three steps and a stand-in for the first would prove only that this class can
/// arrange one. The composer and the stores beneath are substituted: MIME belongs to the MimeKit adapter's own suite
/// and a durable row to the database's.
/// </remarks>
public sealed class AuthoredResponseSubmissionTests
{
    private const string SendingAddress = "mailfathom@example.test";

    private static readonly byte[] StoredRawMime = Encoding.UTF8.GetBytes("From: author@example.test\r\n\r\nBody");

    private static readonly DateTimeOffset SentAt = new(2026, 8, 17, 9, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Recorded = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> ComposedMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nThank you.").AsMemory();

    /// <summary>Nothing has been transmitted when the answer is produced, which is the one thing the record has to say.</summary>
    [Fact]
    public async Task SubmitAsync_AReplySomebodyWrote_QueuesOneRecordRatherThanDeliveringAnything()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(), out _);

        // Act
        var record = await submission.SubmitAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(Recorded, record.RecordedAt);
        Assert.Equal(0, record.AttemptCount);
    }

    /// <summary>
    /// The account is the one the answered email was stored from and is never named by the caller, which is what keeps
    /// a reply on the mailbox the correspondent has heard from.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AReply_SendsAsTheAccountTheAnsweredEmailWasStoredFrom()
    {
        // Arrange
        var summary = SyntheticEmailSummaries.Create(accountId: "work");
        var submission = SubmissionOver(Rendering(), out _, summary: summary);

        // Act
        var record = await submission.SubmitAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(summary.AccountId, record.AccountId);
    }

    /// <summary>A reply goes where the message asked for answers to go, and the caller never states it.</summary>
    [Fact]
    public async Task SubmitAsync_AReplyToTheSenderAlone_AddressesWhoeverAskedForAnswers()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(participants: Exchange()), out var composer);

        // Act
        await submission.SubmitAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(OutgoingRecipientRole.To, "author@example.test")],
            ComposedMessage(composer).Recipients.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>The two replies are different acts, and which one was asked for is what decides who receives the message.</summary>
    [Fact]
    public async Task SubmitAsync_AReplyToEverybody_KeepsTheRestOfTheConversationAndLeavesTheAccountOut()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(participants: Exchange()), out var composer);

        // Act
        await submission.SubmitAsync(
            Request(AuthoredResponseAct.ReplyToAll),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (OutgoingRecipientRole.To, "author@example.test"),
                (OutgoingRecipientRole.Cc, "colleague@example.test"),
            ],
            ComposedMessage(composer).Recipients.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>A forward addresses nobody of its own, so the people it reaches are the ones the author named and no others.</summary>
    [Fact]
    public async Task SubmitAsync_AForward_AddressesOnlyThePeopleTheAuthorNamed()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(participants: Exchange()), out var composer);

        // Act
        await submission.SubmitAsync(
            Request(AuthoredResponseAct.Forward) with
            {
                Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.To, "reader@example.test")],
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(OutgoingRecipientRole.To, "reader@example.test")],
            ComposedMessage(composer).Recipients.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>Somebody the author copies in is added to whoever the reply already reaches rather than replacing them.</summary>
    [Fact]
    public async Task SubmitAsync_AReplyCopyingSomebodyIn_AddsThemBesideThePersonBeingAnswered()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(participants: Exchange()), out var composer);

        // Act
        await submission.SubmitAsync(
            Request() with
            {
                Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.Cc, "reader@example.test")],
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (OutgoingRecipientRole.To, "author@example.test"),
                (OutgoingRecipientRole.Cc, "reader@example.test"),
            ],
            ComposedMessage(composer).Recipients.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>The threading identifiers ride on the authored message, so the reply rejoins the conversation it answers.</summary>
    [Fact]
    public async Task SubmitAsync_AReply_ComposesTheAnsweredMessageIdentifiersRatherThanNone()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(), out var composer);

        // Act
        await submission.SubmitAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(OutgoingThreadPlacement.None, ComposedMessage(composer).Threading);
    }

    /// <summary>
    /// The message is composed against what stays correct whatever a submission server turns out to say, because no
    /// server is being talked to and the record has to exist before a connection is worth opening.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AReply_ComposesBeforeAnySubmissionServerHasSpoken()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(), out var composer);

        // Act
        await submission.SubmitAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(
            MailDeliveryCapabilities.BeforeAnyServerHasSpoken,
            composer
                .ReceivedCalls()
                .Single(call => call.GetMethodInfo().Name == nameof(IAuthoredEmailComposer.Compose))
                .GetArguments()[3]);
    }

    /// <summary>A retry carrying the key the first call carried reads back that call's record and queues nothing further.</summary>
    [Fact]
    public async Task SubmitAsync_TheSameIdempotencyKeyTwice_QueuesOneRecord()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(), out _);
        var request = Request();

        // Act
        var first = await submission.SubmitAsync(request, TestContext.Current.CancellationToken);
        var second = await submission.SubmitAsync(request, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Id, second.Id);
    }

    /// <summary>The retry above is one answer sent once, so what records the send records it once as well.</summary>
    [Fact]
    public async Task SubmitAsync_TheSameIdempotencyKeyTwice_RecordsTheSendOnce()
    {
        // Arrange
        var auditor = Substitute.For<IAuthoredSendAuditor>();
        var submission = SubmissionOver(
            Rendering(),
            out _,
            governor: AuthoredSendGovernors.Governing(auditor: auditor));
        var request = Request();
        await submission.SubmitAsync(request, TestContext.Current.CancellationToken);

        // Act
        await submission.SubmitAsync(request, TestContext.Current.CancellationToken);

        // Assert
        await auditor.Received(1).RecordAuthoredSendAsync(
            Arg.Any<AuthoredSend>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Sending is refused first, because a caller that may not send has no business reaching a use case that reads the
    /// mail it would have quoted.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_ACallerWithoutTheSendingGrant_IsRefusedBeforeAnyMailIsRead()
    {
        // Arrange
        var summaryReader = SummaryReaderReturning(SyntheticEmailSummaries.Create());
        var submission = SubmissionOver(
            Rendering(),
            out _,
            summaryReader: summaryReader,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => submission.SubmitAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
        await summaryReader
            .DidNotReceive()
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Reading is refused beneath, because an answer quotes the message it answers — so a caller granted only the
    /// sending half would read mail by asking to reply to it.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_ACallerWithoutTheReadingGrant_IsRefusedByTheAuthoringBeneath()
    {
        // Arrange
        var submission = SubmissionOver(
            Rendering(),
            out _,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => submission.SubmitAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailRead, refusal.RequiredPermission);
    }

    /// <summary>
    /// A folder an operator withheld from tools, an identity this deployment holds nothing for, and a local copy whose
    /// content cannot be read are one answer, so nobody learns which mail exists by asking to reply to it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EmailsThatCannotBeAnswered))]
    public async Task SubmitAsync_AnEmailThisDeploymentWillNotAnswer_IsRefusedIdenticallyInEveryCase(
        string caseName,
        AnsweredEmailArrangement arrangement)
    {
        // Arrange
        Assert.NotEmpty(caseName);
        var submission = SubmissionOver(
            Rendering(),
            out _,
            summary: arrangement.Summary,
            summaryReader: arrangement.SummaryReader,
            contentStore: arrangement.ContentStore,
            folderParticipation: arrangement.FolderParticipation);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AnsweredEmailUnavailable, refusal.ErrorCode);
        Assert.Equal("No email this deployment can answer is held under that identifier.", refusal.Message);
    }

    /// <summary>The three arrangements a caller must not be able to tell apart, each reached a different way.</summary>
    public static TheoryData<string, AnsweredEmailArrangement> EmailsThatCannotBeAnswered() => new()
    {
        {
            "a folder withheld from tools",
            new AnsweredEmailArrangement { FolderParticipation = StubMailFolderParticipation.Nothing }
        },
        {
            "an identity this deployment holds nothing for",
            new AnsweredEmailArrangement { SummaryReader = SummaryReaderReturning(summary: null) }
        },
        {
            "a local copy whose content is missing",
            new AnsweredEmailArrangement { ContentStore = ContentStoreReturning(storedContent: null) }
        },
        {
            "content synchronization deliberately left unstored",
            new AnsweredEmailArrangement
            {
                Summary = SyntheticEmailSummaries.Create() with
                {
                    ContentAvailability = StoredEmailContentAvailability.ExceededSizeLimit,
                },
            }
        },
    };

    /// <summary>A damaged local copy is still recorded for repair, even though the caller is told nothing about it.</summary>
    [Fact]
    public async Task SubmitAsync_AnEmailWhoseStoredCopyIsMissing_RecordsARepairRequestTheCallerIsNotTold()
    {
        // Arrange
        var repairRequests = new RecordingEmailContentRepairRequestStore();
        var summary = SyntheticEmailSummaries.Create();
        var submission = SubmissionOver(
            Rendering(),
            out _,
            summary: summary,
            contentStore: ContentStoreReturning(storedContent: null),
            repairRequestStore: repairRequests);

        // Act
        await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        var recorded = Assert.Single(repairRequests.Recorded);
        Assert.Equal(summary.StoredEmailId, recorded.StoredEmailId);
        Assert.Equal(EmailContentDefect.Missing, recorded.Defect);
    }

    /// <summary>
    /// The files belong to the message being forwarded, so the answer is the only place their number and size can be
    /// judged — and the refusal names the bound rather than forwarding a message without them.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AForwardCarryingMoreFilesThanTheDeploymentComposes_IsRefusedNamingTheBound()
    {
        // Arrange
        var submission = SubmissionOver(
            Rendering(attachments:
            [
                Description("one.pdf", sizeOctets: 8),
                Description("two.pdf", sizeOctets: 8),
                Description("three.pdf", sizeOctets: 8),
                Description("four.pdf", sizeOctets: 8),
            ]),
            out _);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(
                Request(AuthoredResponseAct.Forward) with
                {
                    Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.To, "reader@example.test")],
                },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Contains("3", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The account that configures no address to send from cannot answer, and that is the operator's to fix.</summary>
    [Fact]
    public async Task SubmitAsync_AnAccountConfiguringNoSendingAddress_IsRefusedAsADeploymentThatCannotSend()
    {
        // Arrange
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        senderIdentities.FindSenderIdentity(Arg.Any<MailAccountId>()).Returns((OutgoingSenderIdentity?)null);
        var submission = SubmissionOver(Rendering(), out _, senderIdentities: senderIdentities);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailSendingUnavailable, refusal.ErrorCode);
    }

    /// <summary>A person the book does not hold refuses the answer, in the terms an author of a new message meets.</summary>
    [Fact]
    public async Task SubmitAsync_ACopiedRecipientNamingAContactTheBookDoesNotHold_IsRefusedAsAnUnresolvedRecipient()
    {
        // Arrange
        var submission = SubmissionOver(Rendering(), out _);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(
                Request() with
                {
                    Recipients =
                    [
                        NamedRecipient.ByContact(OutgoingRecipientRole.Cc, ContactId.Create(Guid.CreateVersion7())),
                    ],
                },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailRecipientUnresolved, refusal.ErrorCode);
    }

    /// <summary>
    /// A list this long describes an answer no record could be written for however the book answered, so it is refused
    /// before anything is read.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_MoreCopiedRecipientsThanARecordHolds_IsRefusedBeforeAnyMailIsRead()
    {
        // Arrange
        var summaryReader = SummaryReaderReturning(SyntheticEmailSummaries.Create());
        var submission = SubmissionOver(Rendering(), out _, summaryReader: summaryReader);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(
                Request() with
                {
                    Recipients =
                    [
                        .. Enumerable
                            .Range(0, OutgoingEmailRequest.MaximumRecipientCount + 1)
                            .Select(position => NamedRecipient.AtAddress(
                                OutgoingRecipientRole.Cc,
                                $"reader{position}@example.test")),
                    ],
                },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        await summaryReader
            .DidNotReceive()
            .FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A message this deployment will not compose is refused as a message rather than as an answer.</summary>
    [Fact]
    public async Task SubmitAsync_AnAnswerTheCompositionRefuses_ReportsTheCompositionsOwnRefusal()
    {
        // Arrange
        var composer = Substitute.For<IAuthoredEmailComposer>();
        composer
            .Compose(
                Arg.Any<MailAccountId>(),
                Arg.Any<OutgoingEmailRequester>(),
                Arg.Any<AuthoredEmail>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(AuthoredEmailComposition.Refused(new AuthoredEmailRefusal(
                AuthoredEmailRefusalReason.FieldUnusable,
                AuthoredEmailField.PlainTextBody)));
        var submission = SubmissionOver(Rendering(), out _, composing: composer);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
    }

    /// <summary>
    /// Nothing a refusal says is the correspondence of the people the answered message is between, whichever refusal it
    /// is: an address, a subject, and body text would otherwise reach every log line and every error a client keeps.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_EveryRefusalThisUseCaseRaises_NamesNothingOfTheMailItRefusedToAnswer()
    {
        // Arrange
        string[] arranged =
            ["author@example.test", "colleague@example.test", "Quarterly report", "The report is attached."];
        var submission = SubmissionOver(
            Rendering(participants: Exchange()),
            out _,
            folderParticipation: StubMailFolderParticipation.Nothing);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => submission.SubmitAsync(Request(), TestContext.Current.CancellationToken));

        // Assert
        Assert.All(
            arranged,
            secret => Assert.DoesNotContain(secret, refusal.Message, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A reply addresses whoever asked for answers, which this deployment derived rather than the caller naming, so the
    /// strictest posture leaves answering mail working exactly as it did.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AReplyUnderTheStrictestPosture_IsStillAdmitted()
    {
        // Arrange
        var submission = SubmissionOver(
            Rendering(participants: Exchange()),
            out var composer,
            governor: AuthoredSendGovernors.Governing(
                settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse)));

        // Act
        await submission.SubmitAsync(Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(OutgoingRecipientRole.To, "author@example.test")],
            ComposedMessage(composer).Recipients.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>
    /// An address the caller added to an answer is the caller's word rather than the conversation's, and under the
    /// strict posture a deployment holding no record of it refuses the whole message.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AReplyCopyingInSomebodyNothingVouchesFor_IsRefused()
    {
        // Arrange
        var submission = SubmissionOver(
            Rendering(participants: Exchange()),
            out _,
            governor: AuthoredSendGovernors.Governing(
                settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse)));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => submission.SubmitAsync(
                Request() with
                {
                    Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.Cc, "accomplice@elsewhere.test")],
                },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientUnvouched, refusal.ErrorCode);
        Assert.DoesNotContain("elsewhere.test", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A forward addresses nobody of its own, so every address on it is the caller's word and is judged — which is the
    /// instruction the strict posture exists to refuse and the thing an operator has to know before adopting it.
    /// </summary>
    [Fact]
    public async Task SubmitAsync_AForwardToSomebodyNothingVouchesFor_IsRefused()
    {
        // Arrange
        var submission = SubmissionOver(
            Rendering(participants: Exchange()),
            out _,
            governor: AuthoredSendGovernors.Governing(
                settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse)));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => submission.SubmitAsync(
                Request(AuthoredResponseAct.Forward) with
                {
                    Recipients = [NamedRecipient.AtAddress(OutgoingRecipientRole.To, "accomplice@elsewhere.test")],
                },
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientUnvouched, refusal.ErrorCode);
    }

    /// <summary>What a test varies about the email being answered, so the theory above states one case per row.</summary>
    /// <remarks>
    /// It is public because a theory's data crosses the xUnit serialization boundary, and every member is optional
    /// because each row varies exactly one of them: the point of the theory is that four different arrangements are
    /// answered with one sentence.
    /// </remarks>
    public sealed record AnsweredEmailArrangement
    {
        /// <summary>Gets the summary the answered email resolves to, or <see langword="null" /> for the ordinary one.</summary>
        public EmailSummary? Summary { get; init; }

        /// <summary>Gets the reader answering for it, or <see langword="null" /> for one answering with the summary.</summary>
        public IStoredEmailSummaryReader? SummaryReader { get; init; }

        /// <summary>Gets the content store, or <see langword="null" /> for one holding an intact copy.</summary>
        public IEmailContentStore? ContentStore { get; init; }

        /// <summary>Gets what the deployment maps for tools, or <see langword="null" /> to map the answered folder.</summary>
        public IMailFolderParticipationReader? FolderParticipation { get; init; }
    }

    private static AuthoredResponseSubmission SubmissionOver(
        EmailContentRendering rendering,
        out IAuthoredEmailComposer composer,
        EmailSummary? summary = null,
        IStoredEmailSummaryReader? summaryReader = null,
        IEmailContentStore? contentStore = null,
        IEmailContentRepairRequestStore? repairRequestStore = null,
        IMailFolderParticipationReader? folderParticipation = null,
        IOutgoingSenderIdentityReader? senderIdentities = null,
        IAuthoredEmailComposer? composing = null,
        AccessAuthorization? authorization = null,
        AuthoredSendGovernor? governor = null)
    {
        var answered = summary ?? SyntheticEmailSummaries.Create();
        composer = composing ?? ComposingAuthoredEmails.ThatComposes(ComposedMime);
        var granted = authorization
            ?? AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.MailSend,
                MailFathomPermission.MailRead);

        var authoring = new StoredEmailResponseAuthoring(
            summaryReader ?? SummaryReaderReturning(answered),
            contentStore ?? ContentStoreReturning(IntactContent()),
            RendererReturning(rendering),
            ContentReaderOpening(),
            repairRequestStore ?? new RecordingEmailContentRepairRequestStore(),
            new MailboxScopeResolver(
                CatalogServing(answered.AccountId),
                folderParticipation ?? StubMailFolderParticipation.Mapping(
                    new MailFolderIdentity(answered.AccountId, answered.FolderAlias)),
                StubJunkMailFolderCatalog.None,
                StubMailFolderMappings.ResolvingNothing),
            senderIdentities ?? SenderIdentitiesFor(answered.AccountId),
            new NamedRecipientResolver(new InMemoryContactBookStore()),
            Bounds(),
            granted);

        return new AuthoredResponseSubmission(
            authoring,
            composer,
            OutboxOver(granted),
            governor ?? AuthoredSendGovernors.Permitting(granted),
            granted);
    }

    private static MailOutbox OutboxOver(AccessAuthorization granted)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new MailOutbox(
            new InMemoryOutgoingEmailStore(timeProvider: new FakeTimeProvider(Recorded)),
            Substitute.For<IEmailContentStore>(),
            new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions(),
                new FakeTimeProvider()),
            new MailOutboxSignal(capacity: 8),
            Substitute.For<IJobStore>(),
            Substitute.For<IOutboxOperationStore>(),
            granted,
            OutgoingMailGovernors.Permitting(),
            OutgoingMailScreenings.Inactive(),
            new FakeTimeProvider(Recorded));
    }

    private static AuthoredEmail ComposedMessage(IAuthoredEmailComposer composer) => (AuthoredEmail)composer
        .ReceivedCalls()
        .First(call => call.GetMethodInfo().Name == nameof(IAuthoredEmailComposer.Compose))
        .GetArguments()[2]!;

    private static MailResponseSubmissionRequest Request(AuthoredResponseAct act = AuthoredResponseAct.Reply) =>
        new()
        {
            AnsweredEmailId = StoredEmailId.Create(Guid.CreateVersion7()),
            Act = act,
            PlainTextBody = "Thank you.",
            Requester = OutgoingEmailRequester.Command("answer-1"),
        };

    /// <summary>An exchange between the answered author, this account, and one other person.</summary>
    private static IReadOnlyList<EmailParticipant> Exchange() =>
    [
        Participant(EmailAddressRole.From, "author@example.test"),
        Participant(EmailAddressRole.To, SendingAddress),
        Participant(EmailAddressRole.Cc, "colleague@example.test"),
    ];

    private static EmailParticipant Participant(EmailAddressRole role, string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var emailAddress));

        return new EmailParticipant(role, emailAddress);
    }

    private static EmailContentRendering Rendering(
        IReadOnlyList<EmailParticipant>? participants = null,
        IReadOnlyList<ExtractedEmailAttachment>? attachments = null)
    {
        const string PlainText = "The report is attached.";
        var carried = attachments ?? [];

        return new EmailContentRendering(
            new EmailContentHeaders(
                "Quarterly report",
                SentAt,
                SentAt,
                participants ?? [Participant(EmailAddressRole.From, "author@example.test")],
                EmailThreadReferences.Create("parent@example.test", inReplyTo: null, references: null)),
            new EmailBodyRepresentation(PlainText, PlainText.Length, EmailBodyTruncation.None),
            null,
            false,
            EmailAttachmentSummary.Create(
                carried,
                inlineResourceCount: 0,
                false,
                carriesUnverifiedSignature: false,
                containsUnexpandedTnefPart: false),
            carried);
    }

    private static ExtractedEmailAttachment Description(string fileName, long sizeOctets) => new(
        AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
        "application/pdf",
        sizeOctets);

    private static IStoredEmailSummaryReader SummaryReaderReturning(EmailSummary? summary)
    {
        var reader = Substitute.For<IStoredEmailSummaryReader>();
        reader.FindAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(summary));

        return reader;
    }

    private static IEmailContentStore ContentStoreReturning(StoredEmailContent? storedContent)
    {
        var contentStore = Substitute.For<IEmailContentStore>();
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(storedContent));

        return contentStore;
    }

    private static IEmailContentRenderer RendererReturning(EmailContentRendering rendering)
    {
        var renderer = Substitute.For<IEmailContentRenderer>();
        renderer
            .RenderAsync(
                Arg.Any<StoredEmailContent>(),
                Arg.Any<EmailContentRenderingBounds>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailContentRenderingResult.Rendered(rendering)));

        return renderer;
    }

    private static IEmailAttachmentContentReader ContentReaderOpening()
    {
        var contentReader = Substitute.For<IEmailAttachmentContentReader>();
        contentReader
            .OpenAsync(Arg.Any<StoredEmailContent>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(
                OpenedEmailAttachmentResult.Opened(new StubOpenedEmailAttachment())));

        return contentReader;
    }

    private static IOutgoingSenderIdentityReader SenderIdentitiesFor(MailAccountId accountId)
    {
        var senderIdentities = Substitute.For<IOutgoingSenderIdentityReader>();
        Assert.True(EmailAddress.TryCreate("MailFathom", SendingAddress, out var address));
        senderIdentities
            .FindSenderIdentity(Arg.Any<MailAccountId>())
            .Returns(OutgoingSenderIdentity.Create(accountId, address));

        return senderIdentities;
    }

    private static IMailAccountCatalog CatalogServing(MailAccountId accountId)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccounts.Returns([SyntheticServedAccount.Of(accountId)]);

        return catalog;
    }

    private static OutgoingEmailBounds Bounds() => new()
    {
        MaxRecipientCount = 8,
        MaxBodyCharacters = 4096,
        MaxAttachmentCount = 3,
        MaxAttachmentBytes = 128,
        MaxMessageBytes = 300,
    };

    private static StoredEmailContent IntactContent() =>
        new(StoredRawMime, StoredRawMime.Length, SHA256.HashData(StoredRawMime));

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }

    /// <summary>An opened attachment carrying a few octets, which is all a forward needs to be composed from here.</summary>
    private sealed class StubOpenedEmailAttachment : IOpenedEmailAttachment
    {
        public ExtractedEmailAttachment Description { get; } = new(
            AttachmentFileName.TryNormalize("carried.pdf", out var normalized) ? normalized : null,
            "application/pdf",
            8);

        public Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(destination);

            return destination.WriteAsync(Encoding.UTF8.GetBytes("carried!!"), cancellationToken).AsTask();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
