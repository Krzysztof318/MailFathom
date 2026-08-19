// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the <c>send_email</c> tool itself owns: reading a call and naming the message asked for.</summary>
/// <remarks>
/// <para>
/// The tool calls the real <see cref="AuthoredMailSubmission" /> rather than a substitute for it, so what a test proves
/// is that the arguments a caller sends reach the use case as the message they describe. Only the composition and the
/// stores beneath it are substituted, because MIME belongs to the MimeKit adapter's own suite and a durable row to the
/// database's.
/// </para>
/// <para>
/// Two properties are asserted throughout rather than in one test of their own: a call refused over its own arguments
/// never reaches the use case, and no refusal message repeats an address, a subject, or a body the caller sent.
/// </para>
/// </remarks>
public sealed class SendEmailToolTests
{
    private const string Account = "work";

    private static readonly DateTimeOffset Recorded = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly ReadOnlyMemory<byte> ComposedMime =
        Encoding.ASCII.GetBytes("Message-ID: <one@example.test>\r\n\r\nHello.").AsMemory();

    /// <summary>Nothing has been transmitted when the answer is produced, which is the one thing the result has to say.</summary>
    [Fact]
    public async Task SendEmailAsync_AMessageSomebodyWrote_PublishesTheQueuedRecordRatherThanADelivery()
    {
        // Arrange
        var tool = ToolOver(out _);

        // Act
        var result = await tool.SendEmailAsync(
            Account,
            ["anna@example.test"],
            "Lunch on Thursday",
            "Shall we?",
            "send-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SendEmailState.Queued, result.State);
        Assert.Equal(Account, result.AccountId);
        Assert.Equal(1, result.RecipientCount);
        Assert.Equal(Recorded, result.QueuedAt);
        Assert.True(Guid.TryParse(result.OutgoingEmailId, out _));
    }

    /// <summary>Each header is its own argument, and which one somebody was named in is what the composition writes them into.</summary>
    [Fact]
    public async Task SendEmailAsync_EveryHeaderNamed_AddressesEachRecipientInTheHeaderItWasNamedIn()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.SendEmailAsync(
            Account,
            ["anna@example.test"],
            "Lunch",
            "Shall we?",
            "send-1",
            cc: ["bartek@example.test"],
            bcc: ["celina@example.test"],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (OutgoingRecipientRole.To, "anna@example.test"),
                (OutgoingRecipientRole.Cc, "bartek@example.test"),
                (OutgoingRecipientRole.Bcc, "celina@example.test"),
            ],
            ComposedMessage(composer).Recipients.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>An HTML alternative is optional, and its absence is a message sent as the plain text alone.</summary>
    [Fact]
    public async Task SendEmailAsync_NoHtmlAlternative_ComposesThePlainTextAlone()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.SendEmailAsync(
            Account,
            ["anna@example.test"],
            "Lunch",
            "Shall we?",
            "send-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var authored = ComposedMessage(composer);
        Assert.Equal("Shall we?", authored.PlainTextBody);
        Assert.Null(authored.HtmlBody);
        Assert.Empty(authored.Attachments);
    }

    /// <summary>The key the caller chose is the whole of the identity the record is written under, so a retry finds it.</summary>
    [Fact]
    public async Task SendEmailAsync_TheSameIdempotencyKeyTwice_AnswersWithTheSameQueuedMessage()
    {
        // Arrange
        var tool = ToolOver(out _);
        var first = await tool.SendEmailAsync(
            Account,
            ["anna@example.test"],
            "Lunch",
            "Shall we?",
            "send-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var retried = await tool.SendEmailAsync(
            Account,
            ["anna@example.test"],
            "Lunch",
            "Shall we?",
            "send-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.OutgoingEmailId, retried.OutgoingEmailId);
    }

    /// <summary>A key of its own is a message of its own, which is what lets somebody write to the same person twice.</summary>
    [Fact]
    public async Task SendEmailAsync_ASecondKey_QueuesASecondMessage()
    {
        // Arrange
        var tool = ToolOver(out _);
        var first = await tool.SendEmailAsync(
            Account,
            ["anna@example.test"],
            "Lunch",
            "Shall we?",
            "send-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Act
        var second = await tool.SendEmailAsync(
            Account,
            ["anna@example.test"],
            "Lunch",
            "Shall we?",
            "send-2",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(first.OutgoingEmailId, second.OutgoingEmailId);
    }

    /// <summary>A key no record could be written under is refused as the caller's own argument, before anything is composed.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("send\u0001one")]
    public async Task SendEmailAsync_AnIdempotencyKeyNoRecordCouldBeWrittenUnder_RefusesBeforeComposing(string key)
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.SendEmailAsync(
                Account,
                ["anna@example.test"],
                "Lunch",
                "Shall we?",
                key,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        composer.DidNotReceiveWithAnyArgs().Compose(default, default!, default!, default!);
    }

    /// <summary>A key longer than the record's column is refused here, so the caller reads a bound rather than an argument failure.</summary>
    [Fact]
    public async Task SendEmailAsync_AnIdempotencyKeyLongerThanARecordHolds_NamesTheLengthItMayHave()
    {
        // Arrange
        var tool = ToolOver(out _);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.SendEmailAsync(
                Account,
                ["anna@example.test"],
                "Lunch",
                "Shall we?",
                new string('k', OutgoingEmailRequester.MaximumIdentityLength + 1),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("128", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>Text that names no account at all is the caller's own argument rather than an account nobody serves.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("work\nother")]
    public async Task SendEmailAsync_TextNamingNoAccount_RefusesBeforeComposing(string account)
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.SendEmailAsync(
                account,
                ["anna@example.test"],
                "Lunch",
                "Shall we?",
                "send-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        composer.DidNotReceiveWithAnyArgs().Compose(default, default!, default!, default!);
    }

    /// <summary>An account this deployment does not serve is the same answer whichever spelling named it.</summary>
    [Fact]
    public async Task SendEmailAsync_AnAccountThisDeploymentDoesNotServe_Refuses()
    {
        // Arrange
        var tool = ToolOver(out _);

        // Act
        var refusal = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => tool.SendEmailAsync(
                "archive",
                ["anna@example.test"],
                "Lunch",
                "Shall we?",
                "send-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailAccountNotAccessible, refusal.ErrorCode);
    }

    /// <summary>An entry that names nobody is refused against the header it was written in, so the caller knows which list to fix.</summary>
    [Fact]
    public async Task SendEmailAsync_ARecipientEntryCarryingNoAddress_RefusesNamingItsHeader()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.SendEmailAsync(
                Account,
                ["anna@example.test"],
                "Lunch",
                "Shall we?",
                "send-1",
                cc: [" "],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Contains("cc recipients", refusal.Message, StringComparison.Ordinal);
        composer.DidNotReceiveWithAnyArgs().Compose(default, default!, default!, default!);
    }

    /// <summary>A list longer than a record holds is refused on its length, before anything of it is read.</summary>
    /// <remarks>
    /// The use case bounds the same number for every author, so what this proves is that the tool refuses in front of
    /// the expansion rather than after it: a caller cannot make this boundary build a list of whatever length it sent.
    /// </remarks>
    [Fact]
    public async Task SendEmailAsync_MorePeopleThanARecordHolds_RefusesWithoutReadingTheList()
    {
        // Arrange
        var tool = ToolOver(out var composer);
        var addresses = Enumerable
            .Range(0, OutgoingEmailRequest.MaximumRecipientCount + 1)
            .Select(index => $"person{index}@example.test")
            .ToArray();

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.SendEmailAsync(
                Account,
                addresses,
                "Lunch",
                "Shall we?",
                "send-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Contains(
            OutgoingEmailRequest.MaximumRecipientCount.ToString(CultureInfo.InvariantCulture),
            refusal.Message,
            StringComparison.Ordinal);
        composer.DidNotReceiveWithAnyArgs().Compose(default, default!, default!, default!);
    }

    /// <summary>The grant is the use case's to require, and a tool test proves the tool does not reach past it.</summary>
    [Fact]
    public async Task SendEmailAsync_CallerWithoutTheSendGrant_Refuses()
    {
        // Arrange
        var tool = ToolOver(
            out _,
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => tool.SendEmailAsync(
                Account,
                ["anna@example.test"],
                "Lunch",
                "Shall we?",
                "send-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
    }

    /// <summary>A refusal a caller reads carries what to change and nothing that was in the message.</summary>
    [Fact]
    public async Task SendEmailAsync_Refused_NamesNoAddressSubjectOrBody()
    {
        // Arrange
        var tool = ToolOver(out _);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.SendEmailAsync(
                Account,
                ["anna@example.test"],
                "Lunch on Thursday",
                "Shall we meet?",
                string.Empty,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("anna@example.test", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Lunch on Thursday", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Shall we meet?", refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AuthoredEmail ComposedMessage(IAuthoredEmailComposer composer) => (AuthoredEmail)composer
        .ReceivedCalls()
        .Single(call => call.GetMethodInfo().Name == nameof(IAuthoredEmailComposer.Compose))
        .GetArguments()[2]!;

    private static SendEmailTool ToolOver(
        out IAuthoredEmailComposer composer,
        AccessAuthorization? authorization = null)
    {
        composer = ComposerThatComposes();
        var granted = authorization ?? AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend);

        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new SendEmailTool(new AuthoredMailSubmission(
            new StubMailAccountCatalog(Account),
            new NamedRecipientResolver(Substitute.For<IContactDirectory>()),
            composer,
            new MailOutbox(
                OutgoingEmailStoreThatRecords(),
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
                new FakeTimeProvider(Recorded)),
            AuthoredSendGovernors.Permitting(granted),
            granted,
            new FakeTimeProvider(Recorded)));
    }

    /// <summary>An outgoing store that keeps one record per idempotency identity, which is what the tool's retry claim rests on.</summary>
    /// <remarks>
    /// It is the port rather than a database, because what a tool test needs from it is the one behaviour a caller can
    /// observe through this tool: the same key answers with the record the first call wrote. Everything else about the
    /// row — the columns, the constraint, the stages — is the store's own suite and the integration suite's.
    /// </remarks>
    private static IOutgoingEmailStore OutgoingEmailStoreThatRecords()
    {
        var recordsByIdentity = new Dictionary<string, OutgoingEmailRecord>(StringComparer.Ordinal);
        var store = Substitute.For<IOutgoingEmailStore>();
        store
            .OpenAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<OutgoingEmailRequest>(),
                Arg.Any<OutgoingEmailPrincipal>(),
                Arg.Any<long>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.ArgAt<OutgoingEmailRequest>(1);
                var identity = $"{request.AccountId.Value}\u0000{request.Requester.Identity}";

                if (!recordsByIdentity.TryGetValue(identity, out var opened))
                {
                    opened = new OutgoingEmailRecord
                    {
                        Id = OutgoingEmailId.Create(Guid.CreateVersion7(Recorded)),
                        AccountId = request.AccountId,
                        Requester = request.Requester,
                        Principal = call.ArgAt<OutgoingEmailPrincipal>(2),
                        Recipients = [.. request.Recipients.Select(OutgoingRecipientOutcome.Unanswered)],
                        Stage = OutgoingEmailStage.Recorded,
                        MimeByteLength = call.ArgAt<long>(3),
                        AttemptCount = 0,
                        RecordedAt = Recorded,
                        StageChangedAt = Recorded,
                        AvailableAt = Recorded,
                        DueAt = null,
                        LastFailure = null,
                        LastReplyCode = null,
                        Filings = [],
                        LastFilingFailure = null,
                    };

                    recordsByIdentity[identity] = opened;
                }

                return opened;
            });

        return store;
    }

    /// <summary>A composer that produces a message from whatever it is handed, so a tool test says nothing about MIME.</summary>
    private static IAuthoredEmailComposer ComposerThatComposes()
    {
        var composer = Substitute.For<IAuthoredEmailComposer>();
        composer
            .Compose(
                Arg.Any<MailAccountId>(),
                Arg.Any<OutgoingEmailRequester>(),
                Arg.Any<AuthoredEmail>(),
                Arg.Any<MailDeliveryCapabilities>())
            .Returns(call =>
            {
                var authored = call.ArgAt<AuthoredEmail>(2);

                return AuthoredEmailComposition.Composed(new ComposedOutgoingEmail(
                    OutgoingEmailRequest.Create(
                        call.ArgAt<MailAccountId>(0),
                        call.ArgAt<OutgoingEmailRequester>(1),
                        [.. authored.Recipients.Select(recipient => OutgoingRecipient.Create(
                            Address(recipient.Address),
                            recipient.Role,
                            recipient.Contact))]),
                    InternetMessageId.Mint("example.test"),
                    ComposedMime));
            });

        return composer;
    }

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }
}
