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
using MailFathom.Mcp.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the <c>forward_email</c> tool itself owns: reading a call and naming the forward asked for.</summary>
/// <remarks>
/// A forward addresses nobody of its own, so the recipient list is the whole of where it goes and is the argument this
/// tool is mostly about. Everything it carries — the subject, the quoted message, the files — is read from the stored
/// copy by the use case, which is what these tests observe rather than restate.
/// </remarks>
public sealed class ForwardEmailToolTests
{
    /// <summary>Nothing has been transmitted when the answer is produced, which is the one thing the result has to say.</summary>
    [Fact]
    public async Task ForwardEmailAsync_AForwardSomebodyWrote_PublishesTheQueuedRecordRatherThanADelivery()
    {
        // Arrange
        var tool = ToolOver(out _);

        // Act
        var result = await tool.ForwardEmailAsync(
            AnsweredEmailId,
            ["reader@example.test"],
            "Passing this on.",
            "forward-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SendEmailState.Queued, result.State);
        Assert.Equal(AnsweredMailSubmissions.ServedAccountId, result.AccountId);
        Assert.Equal(1, result.RecipientCount);
        Assert.Equal(AnsweredMailSubmissions.RecordedAt, result.QueuedAt);
    }

    /// <summary>
    /// A forward goes only where the caller sent it: nobody the original named is addressed, which is what separates it
    /// from either reply.
    /// </summary>
    [Fact]
    public async Task ForwardEmailAsync_AForward_AddressesOnlyThePeopleTheCallerNamed()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.ForwardEmailAsync(
            AnsweredEmailId,
            ["reader@example.test"],
            "Passing this on.",
            "forward-1",
            cc: ["watcher@example.test"],
            bcc: ["archive@example.test"],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (OutgoingRecipientRole.To, "reader@example.test"),
                (OutgoingRecipientRole.Cc, "watcher@example.test"),
                (OutgoingRecipientRole.Bcc, "archive@example.test"),
            ],
            AnsweredMailSubmissions
                .ComposedAnswer(composer)
                .Recipients
                .Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>The files belong to the message being forwarded and are read from the stored copy rather than fetched or rebuilt.</summary>
    [Fact]
    public async Task ForwardEmailAsync_AMessageCarryingFiles_CarriesThemFromTheStoredCopy()
    {
        // Arrange
        var tool = ToolOver(
            out var composer,
            rendering: AnsweredMailSubmissions.AnsweredRendering(
                [AnsweredMailSubmissions.CarriedFile("invoice.pdf", sizeOctets: 8)]));

        // Act
        await tool.ForwardEmailAsync(
            AnsweredEmailId,
            ["reader@example.test"],
            "Passing this on.",
            "forward-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var carried = Assert.Single(AnsweredMailSubmissions.ComposedAnswer(composer).Attachments);
        Assert.Equal("application/pdf", carried.MediaType);
    }

    /// <summary>The subject is the original's under the conventional prefix, and no argument states it.</summary>
    [Fact]
    public async Task ForwardEmailAsync_AForward_DerivesTheSubjectFromTheStoredCopy()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await tool.ForwardEmailAsync(
            AnsweredEmailId,
            ["reader@example.test"],
            "Passing this on.",
            "forward-1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        var answer = AnsweredMailSubmissions.ComposedAnswer(composer);
        Assert.Contains("Quarterly report", answer.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("Re:", answer.Subject, StringComparison.Ordinal);
    }

    /// <summary>
    /// A message carrying more files than this deployment composes is refused naming the bound rather than forwarded
    /// with files quietly dropped.
    /// </summary>
    [Fact]
    public async Task ForwardEmailAsync_AMessageCarryingMoreFilesThanTheDeploymentComposes_IsRefusedNamingTheBound()
    {
        // Arrange
        var tool = ToolOver(
            out _,
            rendering: AnsweredMailSubmissions.AnsweredRendering(
            [
                AnsweredMailSubmissions.CarriedFile("one.pdf", sizeOctets: 8),
                AnsweredMailSubmissions.CarriedFile("two.pdf", sizeOctets: 8),
                AnsweredMailSubmissions.CarriedFile("three.pdf", sizeOctets: 8),
                AnsweredMailSubmissions.CarriedFile("four.pdf", sizeOctets: 8),
            ]));

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.ForwardEmailAsync(
                AnsweredEmailId,
                ["reader@example.test"],
                "Passing this on.",
                "forward-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Contains(
            AnsweredMailSubmissions.Bounds().MaxAttachmentCount.ToString(),
            refusal.Message,
            StringComparison.Ordinal);
    }

    /// <summary>Text naming no email this system issued an identifier for is refused before anything is looked up.</summary>
    [Fact]
    public async Task ForwardEmailAsync_TextNamingNoEmail_IsRefusedWithoutReachingTheUseCase()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        await Assert.ThrowsAsync<StoredEmailIdentifierMalformedException>(
            () => tool.ForwardEmailAsync(
                "not-a-uuid",
                ["reader@example.test"],
                "Passing this on.",
                "forward-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Empty(composer.ReceivedCalls());
    }

    /// <summary>An entry that carries no address names nobody, and is refused rather than composed away.</summary>
    [Fact]
    public async Task ForwardEmailAsync_ARecipientCarryingNoAddress_IsRefusedNamingTheHeader()
    {
        // Arrange
        var tool = ToolOver(out var composer);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.ForwardEmailAsync(
                AnsweredEmailId,
                ["reader@example.test", "   "],
                "Passing this on.",
                "forward-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Empty(composer.ReceivedCalls());
    }

    /// <summary>An email nothing may read is an email nothing may forward, and the refusal says no more than that.</summary>
    [Fact]
    public async Task ForwardEmailAsync_AnEmailOfAFolderWithheldFromTools_IsRefusedAsNoEmailItCanAnswer()
    {
        // Arrange
        var tool = ToolOver(out _, participationReader: StubMailFolderParticipation.Nothing);

        // Act
        var refusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => tool.ForwardEmailAsync(
                AnsweredEmailId,
                ["reader@example.test"],
                "Passing this on.",
                "forward-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AnsweredEmailUnavailable, refusal.ErrorCode);
    }

    /// <summary>An email whose content this deployment no longer holds is refused in exactly the same words.</summary>
    [Fact]
    public async Task ForwardEmailAsync_AnEmailWhoseContentIsNotHeld_IsRefusedInTheSameWordsAsAWithheldOne()
    {
        // Arrange
        var withheld = ToolOver(out _, participationReader: StubMailFolderParticipation.Nothing);
        var unreadable = ToolOver(
            out _,
            summary: AnsweredMailSubmissions.AnsweredEmail(StoredEmailContentAvailability.ExceededSizeLimit));

        // Act
        var withheldRefusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => withheld.ForwardEmailAsync(
                AnsweredEmailId,
                ["reader@example.test"],
                "Passing this on.",
                "forward-1",
                cancellationToken: TestContext.Current.CancellationToken));
        var unreadableRefusal = await Assert.ThrowsAsync<MailSubmissionRefusedException>(
            () => unreadable.ForwardEmailAsync(
                AnsweredEmailId,
                ["reader@example.test"],
                "Passing this on.",
                "forward-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(withheldRefusal.ErrorCode, unreadableRefusal.ErrorCode);
        Assert.Equal(withheldRefusal.Message, unreadableRefusal.Message);
    }

    /// <summary>A caller without the sending grant reaches nothing, whatever the descriptor it read said.</summary>
    [Fact]
    public async Task ForwardEmailAsync_ACallerWithoutTheSendingGrant_IsRefused()
    {
        // Arrange
        var tool = ToolOver(
            out _,
            authorization: AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead));

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => tool.ForwardEmailAsync(
                AnsweredEmailId,
                ["reader@example.test"],
                "Passing this on.",
                "forward-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.MailSend, refusal.RequiredPermission);
    }

    /// <summary>
    /// A forward carries somebody else's correspondence, so a refusal about one is the place its content would most
    /// easily escape into a log line.
    /// </summary>
    [Fact]
    public async Task ForwardEmailAsync_ARefusal_NamesNothingOfTheMailItRefusedToForward()
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
            () => tool.ForwardEmailAsync(
                AnsweredEmailId,
                ["reader@example.test"],
                "Passing this on.",
                "forward-1",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.All(
            arranged,
            secret => Assert.DoesNotContain(secret, refusal.Message, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The identifier every call names, which the summary reader answers for whatever it is asked.</summary>
    private static string AnsweredEmailId => Guid.CreateVersion7().ToString();

    private static ForwardEmailTool ToolOver(
        out IAuthoredEmailComposer composer,
        EmailSummary? summary = null,
        EmailContentRendering? rendering = null,
        StubMailFolderParticipation? participationReader = null,
        AccessAuthorization? authorization = null) =>
        new(AnsweredMailSubmissions.Over(out composer, summary, rendering, participationReader, authorization));
}
