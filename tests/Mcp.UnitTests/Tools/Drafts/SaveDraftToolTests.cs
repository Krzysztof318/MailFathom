// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Results;
using MailFathom.Mcp.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Drafts;

/// <summary>Covers what the <c>save_draft</c> tool itself owns: reading a call and naming the draft it asked for.</summary>
/// <remarks>
/// <para>
/// The tool calls the real use cases rather than substitutes for them, so what a test proves is that the arguments a
/// caller sends reach one of them as the message they describe — and which of the two, because a draft is the one
/// authored act with two shapes.
/// </para>
/// <para>
/// Two properties are asserted throughout rather than in one test of their own: a call refused over its own arguments
/// writes no draft, and no refusal message repeats an address, a subject, or a body the caller sent.
/// </para>
/// </remarks>
public sealed class SaveDraftToolTests
{
    private const string Recipient = "anna@example.test";

    /// <summary>Nothing has been sent when the answer is produced, and nothing ever will be until somebody asks.</summary>
    [Fact]
    public async Task SaveDraftAsync_AMessageOfItsOwn_HoldsTheDraftAndQueuesNoSend()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var result = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch on Thursday",
            [Recipient],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(Guid.TryParse(result.DraftId, out _));
        Assert.Equal(DraftedMailDeployment.ServedAccount, result.AccountId);
        Assert.Equal(1, result.Revision);
        Assert.Equal(1, result.RecipientCount);
        Assert.Equal(DraftedMailDeployment.Moment, result.SavedAt);
        Assert.Single(deployment.Drafts.Drafts);
        Assert.Empty(deployment.OutgoingEmails.ReceivedCalls());
    }

    /// <summary>The account maps no drafts folder here, so the draft is held and the owner's own client shows nothing yet.</summary>
    [Fact]
    public async Task SaveDraftAsync_AnAccountWhoseMailboxWasNotReached_ReportsTheDraftAsHeldRatherThanFiled()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var result = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SavedDraftState.Held, result.State);
    }

    /// <summary>Each header is its own argument, and which one somebody was named in is what the composition writes them into.</summary>
    [Fact]
    public async Task SaveDraftAsync_EveryHeaderNamed_AddressesEachRecipientInTheHeaderItWasNamedIn()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            [Recipient],
            cc: ["bartek@example.test"],
            bcc: ["celina@example.test"],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (OutgoingRecipientRole.To, Recipient),
                (OutgoingRecipientRole.Cc, "bartek@example.test"),
                (OutgoingRecipientRole.Bcc, "celina@example.test"),
            ],
            deployment.ComposedMessage.Recipients.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>Writing the message before deciding who reads it is what drafting is for, so a draft addressed to nobody is written.</summary>
    [Fact]
    public async Task SaveDraftAsync_NobodyAddressed_WritesAnOrdinaryDraft()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var result = await deployment.SaveTool.SaveDraftAsync(
            "Something to finish later.",
            DraftedMailDeployment.ServedAccount,
            "Draft",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.RecipientCount);
        Assert.Single(deployment.Drafts.Drafts);
    }

    /// <summary>There is no idempotency key, so asking twice is two drafts rather than one message written down twice.</summary>
    [Fact]
    public async Task SaveDraftAsync_TheSameMessageTwice_WritesTwoDrafts()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var first = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            [Recipient],
            cancellationToken: TestContext.Current.CancellationToken);
        var second = await deployment.SaveTool.SaveDraftAsync(
            "Shall we?",
            DraftedMailDeployment.ServedAccount,
            "Lunch",
            [Recipient],
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(first.DraftId, second.DraftId);
        Assert.Equal(2, deployment.Drafts.Drafts.Count);
    }

    /// <summary>A draft that answers nothing has nowhere to read an account or a subject from, so both are required.</summary>
    [Theory]
    [InlineData(null, "Lunch")]
    [InlineData(DraftedMailDeployment.ServedAccount, null)]
    public async Task SaveDraftAsync_AMessageOfItsOwnMissingWhatItStates_IsRefusedAndWritesNothing(
        string? account,
        string? subject)
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SaveTool.SaveDraftAsync(
                "Shall we?",
                account,
                subject,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Empty(deployment.Drafts.Drafts);
    }

    /// <summary>Naming the email a draft answers and naming which answer it is go together, because neither states an answer alone.</summary>
    [Fact]
    public async Task SaveDraftAsync_AnAnsweredEmailWithoutTheAnswerItIs_IsRefusedAndWritesNothing()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SaveTool.SaveDraftAsync(
                "Thank you.",
                answeredEmailId: Guid.CreateVersion7(DraftedMailDeployment.Moment).ToString(),
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Empty(deployment.Drafts.Drafts);
    }

    /// <summary>The other half of the same pair: an answer named without the email it answers names nothing to answer.</summary>
    [Fact]
    public async Task SaveDraftAsync_AnAnswerWithoutTheEmailItAnswers_IsRefusedAndWritesNothing()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SaveTool.SaveDraftAsync(
                "Thank you.",
                answering: DraftedAnswer.SenderOnly,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Empty(deployment.Drafts.Drafts);
    }

    /// <summary>An answer reads its account and its subject from the email it answers, so stating either describes two messages.</summary>
    [Theory]
    [InlineData(DraftedMailDeployment.ServedAccount, null)]
    [InlineData(null, "Re: Lunch")]
    public async Task SaveDraftAsync_AnAnswerStatingAnAccountOrASubject_IsRefusedAndWritesNothing(
        string? account,
        string? subject)
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SaveTool.SaveDraftAsync(
                "Thank you.",
                account,
                subject,
                answeredEmailId: Guid.CreateVersion7(DraftedMailDeployment.Moment).ToString(),
                answering: DraftedAnswer.SenderOnly,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.Empty(deployment.Drafts.Drafts);
    }

    /// <summary>Naming an answered email is what routes the call to the use case that derives an answer from stored mail.</summary>
    /// <remarks>
    /// <para>
    /// This deployment holds no mail, so the answer it produces is the one an email nobody holds gets. What that
    /// establishes is the routing: a message of its own would have been composed and written without the mailbox being
    /// read at all.
    /// </para>
    /// <para>
    /// The answer travels as its own name rather than as the value, because the enumeration is internal to the
    /// protocol boundary and a public test signature cannot carry one.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(nameof(DraftedAnswer.SenderOnly))]
    [InlineData(nameof(DraftedAnswer.Everyone))]
    [InlineData(nameof(DraftedAnswer.Forward))]
    public async Task SaveDraftAsync_AnAnswerToAnEmailThisDeploymentDoesNotHold_IsRefusedAsAnEmailItCannotAnswer(
        string answer)
    {
        // Arrange
        var answering = Enum.Parse<DraftedAnswer>(answer);
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SaveTool.SaveDraftAsync(
                "Thank you.",
                answeredEmailId: Guid.CreateVersion7(DraftedMailDeployment.Moment).ToString(),
                answering: answering,
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AnsweredEmailUnavailable, refusal.ErrorCode);
        Assert.Empty(deployment.Drafts.Drafts);
    }

    /// <summary>Text that could name no account is answered as a statement about the argument rather than about the accounts served.</summary>
    [Fact]
    public async Task SaveDraftAsync_TextThatNamesNoAccount_IsRefusedWithoutNamingWhatIsServed()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SaveTool.SaveDraftAsync(
                "Shall we?",
                "   ",
                "Lunch",
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.DoesNotContain(DraftedMailDeployment.ServedAccount, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(deployment.Drafts.Drafts);
    }

    /// <summary>A recipient that carries no address names nobody, and the refusal says which header rather than what was in it.</summary>
    [Fact]
    public async Task SaveDraftAsync_ARecipientCarryingNoAddress_IsRefusedWithoutRepeatingWhatWasSent()
    {
        // Arrange
        var deployment = new DraftedMailDeployment();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => deployment.SaveTool.SaveDraftAsync(
                "Shall we?",
                DraftedMailDeployment.ServedAccount,
                "Lunch on Thursday",
                [" "],
                cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
        Assert.DoesNotContain("Lunch on Thursday", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Shall we?", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(deployment.Drafts.Drafts);
    }
}
