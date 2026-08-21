// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Outgoing;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the <c>reply_to_email</c> tool itself owns: reading a call and naming the answer asked for.</summary>
/// <remarks>
/// The tool calls the real use case rather than a substitute for it, so what a test proves is that the arguments a
/// caller sends reach it as the reply they describe. The one mapping this tool makes on its own is the audience, which
/// is the argument that decides who receives the message and the one a caller can get wrong irreversibly.
/// </remarks>
public sealed class ReplyToEmailToolTests
{
    /// <summary>Nothing has been transmitted when the answer is produced, which is the one thing the result has to say.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_AReplySomebodyWrote_PublishesTheQueuedRecordRatherThanADelivery()
    {
        // Arrange
        var tool = ToolOver(out _);

        // Act
        var result = await tool.ReplyToEmailAsync(
            AnsweredEmailId,
            ReplyAudience.SenderOnly,
            "Thank you.",
            "reply-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SendEmailState.Queued, result.State);
        Assert.Equal(AnsweredMailSubmissions.ServedAccountId, result.AccountId);
        Assert.Equal(AnsweredMailSubmissions.RecordedAt, result.QueuedAt);
        Assert.True(Guid.TryParse(result.OutgoingEmailId, out _));
    }

    /// <summary>
    /// Answering the sender alone reaches whoever asked for answers and nobody else, which is the half of the audience
    /// a caller chooses when the answer is private.
    /// </summary>
    [Fact]
    public async Task ReplyToEmailAsync_AnsweringTheSenderAlone_AddressesWhoeverAskedForAnswersAndNobodyElse()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.ReplyToEmailAsync(
            AnsweredEmailId,
            ReplyAudience.SenderOnly,
            "Thank you.",
            "reply-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(OutgoingRecipientRole.To, AnsweredMailSubmissions.AnsweredAuthorAddress)],
            AnsweredMailSubmissions
                .ComposedAnswer(composer)
                .Recipients
                .Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>Answering everybody keeps the rest of the conversation and leaves this account's own address out of it.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_AnsweringEverybody_CopiesTheConversationWithoutMailingTheAccountItself()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.ReplyToEmailAsync(
            AnsweredEmailId,
            ReplyAudience.Everyone,
            "Thank you.",
            "reply-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var addressed = AnsweredMailSubmissions.ComposedAnswer(composer).Recipients;
        Assert.Equal(
            [
                (OutgoingRecipientRole.To, AnsweredMailSubmissions.AnsweredAuthorAddress),
                (OutgoingRecipientRole.Cc, AnsweredMailSubmissions.AnsweredCopiedAddress),
            ],
            addressed.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>Somebody a caller copies in is added beside whoever the reply already reaches rather than replacing them.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_CopyingSomebodyIn_AddsThemBesideThePersonBeingAnswered()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.ReplyToEmailAsync(
            AnsweredEmailId,
            ReplyAudience.SenderOnly,
            "Thank you.",
            "reply-1",
            cc: ["reader@example.test"],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (OutgoingRecipientRole.To, AnsweredMailSubmissions.AnsweredAuthorAddress),
                (OutgoingRecipientRole.Cc, "reader@example.test"),
            ],
            AnsweredMailSubmissions
                .ComposedAnswer(composer)
                .Recipients
                .Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>Nothing about the answered message is an argument, so the subject and the threading are derived rather than sent.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_AReply_DerivesTheSubjectAndTheThreadingFromTheStoredCopy()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.ReplyToEmailAsync(
            AnsweredEmailId,
            ReplyAudience.SenderOnly,
            "Thank you.",
            "reply-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var answer = AnsweredMailSubmissions.ComposedAnswer(composer);
        Assert.StartsWith("Re:", answer.Subject, StringComparison.Ordinal);
        Assert.NotEqual(OutgoingThreadPlacement.None, answer.Threading);
    }

    /// <summary>What the caller wrote is placed above the quoted original rather than replacing it.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_AReply_PlacesWhatTheCallerWroteAboveTheQuotedOriginal()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.ReplyToEmailAsync(
            AnsweredEmailId,
            ReplyAudience.SenderOnly,
            "Thank you.",
            "reply-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var body = AnsweredMailSubmissions.ComposedAnswer(composer).PlainTextBody;
        Assert.StartsWith("Thank you.", body, StringComparison.Ordinal);
        Assert.Contains("The report is attached.", body, StringComparison.Ordinal);
    }

    /// <summary>A reply carries no files of its own, and nothing on this surface accepts octets from a caller.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_AReply_CarriesNoAttachments()
    {
        // Arrange
        var tool = ToolOver(
            out var composer,
            rendering: AnsweredMailSubmissions.AnsweredRendering(
                [AnsweredMailSubmissions.CarriedFile("invoice.pdf", sizeOctets: 8)]));

        // Act
        await tool.ReplyToEmailAsync(
            AnsweredEmailId,
            ReplyAudience.SenderOnly,
            "Thank you.",
            "reply-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(AnsweredMailSubmissions.ComposedAnswer(composer).Attachments);
    }

    /// <summary>Text naming no email this system issued an identifier for is refused before anything is looked up.</summary>
    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    public async Task ReplyToEmailAsync_TextNamingNoEmail_IsRefusedWithoutReachingTheUseCase(string storedEmailId)
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await Assert.ThrowsAsync<StoredEmailIdentifierMalformedException>(
            () => tool.ReplyToEmailAsync(
                storedEmailId,
                ReplyAudience.SenderOnly,
                "Thank you.",
                "reply-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(composer.ReceivedCalls());
    }

    /// <summary>
    /// A folder an operator withheld from tools is outside every mailbox read, and a reply must not become the path by
    /// which its content leaves.
    /// </summary>
    [Fact]
    public async Task ReplyToEmailAsync_AnEmailOfAFolderWithheldFromTools_IsRefusedAsNoEmailItCanAnswer()
    {
        // Arrange
        var tool = ToolOver(out _, participationReader: StubMailFolderParticipation.Nothing);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.ReplyToEmailAsync(
                AnsweredEmailId,
                ReplyAudience.SenderOnly,
                "Thank you.",
                "reply-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AnsweredEmailUnavailable, refusal.ErrorCode);
    }

    /// <summary>An email whose content this deployment no longer holds is refused in exactly the same words.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_AnEmailWhoseContentIsNotHeld_IsRefusedInTheSameWordsAsAWithheldOne()
    {
        // Arrange
        var withheld = ToolOver(out _, participationReader: StubMailFolderParticipation.Nothing);
        var unreadable = ToolOver(
            out _,
            summary: AnsweredMailSubmissions.AnsweredEmail(StoredEmailContentAvailability.ExceededSizeLimit));

        // Act
        var withheldRefusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => withheld.ReplyToEmailAsync(
                AnsweredEmailId,
                ReplyAudience.SenderOnly,
                "Thank you.",
                "reply-1",
                cancellationToken: TestContext.Current.CancellationToken));
        var unreadableRefusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => unreadable.ReplyToEmailAsync(
                AnsweredEmailId,
                ReplyAudience.SenderOnly,
                "Thank you.",
                "reply-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(withheldRefusal.ErrorCode, unreadableRefusal.ErrorCode);
        Assert.Equal(withheldRefusal.Message, unreadableRefusal.Message);
    }

    /// <summary>An idempotency key no record could be written under is refused about the argument the caller sent.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("key\nwith-a-break")]
    public async Task ReplyToEmailAsync_AnIdempotencyKeyNoRecordCouldBeWrittenUnder_IsRefusedNamingTheKey(string key)
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.ReplyToEmailAsync(
                AnsweredEmailId,
                ReplyAudience.SenderOnly,
                "Thank you.",
                key,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Empty(composer.ReceivedCalls());
    }

    /// <summary>An audience naming neither act is refused rather than resolved into one of them.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_AnAudienceThisSurfaceDoesNotDeclare_IsRefusedRatherThanDefaulted()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.ReplyToEmailAsync(
                AnsweredEmailId,
                (ReplyAudience)7,
                "Thank you.",
                "reply-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Empty(composer.ReceivedCalls());
    }

    /// <summary>A caller without the sending grant reaches nothing, whatever the descriptor it read said.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_ACallerWithoutTheSendingGrant_IsRefused()
    {
        // Arrange
        var tool = ToolOver(
            out _,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => tool.ReplyToEmailAsync(
                AnsweredEmailId,
                ReplyAudience.SenderOnly,
                "Thank you.",
                "reply-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
    }

    /// <summary>No refusal repeats an address, a subject, or body text of the mail it refused to answer.</summary>
    [Fact]
    public async Task ReplyToEmailAsync_ARefusal_NamesNothingOfTheMailItRefusedToAnswer()
    {
        // Arrange
        string[] arranged =
        [
            AnsweredMailSubmissions.AnsweredAuthorAddress,
            AnsweredMailSubmissions.AnsweredCopiedAddress,
            "Quarterly report",
            "The report is attached.",
        ];
        var tool = ToolOver(out _, participationReader: StubMailFolderParticipation.Nothing);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.ReplyToEmailAsync(
                AnsweredEmailId,
                ReplyAudience.SenderOnly,
                "Thank you.",
                "reply-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.All(
            arranged,
            secret => Assert.DoesNotContain(secret, refusal.Message, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The identifier every call names, which the summary reader answers for whatever it is asked.</summary>
    private static string AnsweredEmailId => Guid.CreateVersion7().ToString();

    private static ReplyToEmailTool ToolOver(
        out IAuthoredEmailComposer composer,
        EmailSummary? summary = null,
        EmailContentRendering? rendering = null,
        StubMailFolderParticipation? participationReader = null,
        AccessAuthorization? authorization = null) =>
        new(AnsweredMailSubmissions.Over(out composer, summary, rendering, participationReader, authorization));
}
