// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers the ceiling that sits between a run's tool loop and the provider it sends through.</summary>
/// <remarks>
/// The decorator is exercised over a scripted client rather than a run, because what it owns is one decision made once
/// per call — whether this question may make another — and one charge made once per run, which is what disposing it is.
/// </remarks>
public sealed class BudgetedChatClientTests
{
    // Both namespaces publish a ChatMessage and a ChatRole, so every name from either is qualified: importing one would
    // leave the other written as a bare name a reader would take for the imported type.
    private static readonly Microsoft.Extensions.AI.ChatMessage[] Conversation =
        [new(Microsoft.Extensions.AI.ChatRole.User, "was the invoice attached")];

    [Fact]
    public async Task GetResponseAsync_ARunInsideItsCeilings_SendsTheCallOn()
    {
        // Arrange
        using var inner = ScriptedChatClient.Answering("The invoice was attached.");
        await using var client = new BudgetedChatClient(inner, LedgerAllowing(providerCalls: 2), Substitute.For<IMailAnsweringSpendLedger>());

        // Act
        var response = await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("The invoice was attached.", response.Text);
        Assert.Single(inner.Calls);
    }

    /// <summary>A call the deployment's own ceiling refused must never reach the endpoint, its circuit, or its health record.</summary>
    [Fact]
    public async Task GetResponseAsync_ARunPastItsCallCeiling_RefusesBeforeAnythingIsSent()
    {
        // Arrange
        using var inner = ScriptedChatClient.Answering("never reached");
        await using var client = new BudgetedChatClient(inner, LedgerAllowing(providerCalls: 1), Substitute.For<IMailAnsweringSpendLedger>());

        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Run, failure.Scope);
        Assert.Single(inner.Calls);
    }

    /// <summary>
    /// The two ledgers answer different questions and are therefore written at different moments: the run's stops this
    /// question and is charged per call, the period's stops the next one and is charged once as the run ends.
    /// </summary>
    [Fact]
    public async Task GetResponseAsync_AProviderThatReportedUsage_ChargesTheRunAtOnceAndLeavesThePeriodUntouched()
    {
        // Arrange
        using var inner = ScriptedChatClient.AnsweringWithUsage("The invoice was attached.", inputTokens: 90, outputTokens: 30);
        var runLedger = LedgerAllowing(tokens: 100);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        await using var client = new BudgetedChatClient(inner, runLedger, spendLedger);

        // Act
        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(120L, runLedger.Read().Tokens);
        await spendLedger.DidNotReceive().RecordSpendAsync(Arg.Any<ChatTokenUsage>(), Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// One charge for the whole run rather than one per turn, which is what makes a tool loop over a backfill cost the
    /// period one write instead of one per message.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ARunThatMadeSeveralCalls_ChargesThePeriodOnceWithWhatTheRunConsumed()
    {
        // Arrange
        using var inner = ScriptedChatClient.AnsweringWithUsage(
            "The invoice was attached.",
            inputTokens: 90,
            outputTokens: 30,
            answers: 2);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        var client = new BudgetedChatClient(inner, LedgerAllowing(), spendLedger);

        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);
        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Act
        await client.DisposeAsync();

        // Assert
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(180, 60), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The provider calls are answered and paid for before the charge is written, so a caller that abandons its request
    /// between the two must not leave a spend that happened uncounted.
    /// </summary>
    [Fact]
    public async Task DisposeAsync_ACallerThatCancelledItsRequest_StillChargesThePeriod()
    {
        // Arrange
        using var inner = ScriptedChatClient.AnsweringWithUsage("The invoice was attached.", inputTokens: 90, outputTokens: 30);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        var client = new BudgetedChatClient(inner, LedgerAllowing(), spendLedger);
        using var abandoned = new CancellationTokenSource();

        await client.GetResponseAsync(Conversation, options: null, abandoned.Token);
        await abandoned.CancelAsync();

        // Act
        await client.DisposeAsync();

        // Assert
        await spendLedger.Received(1).RecordSpendAsync(new ChatTokenUsage(90, 30), CancellationToken.None);
    }

    /// <summary>Disposing twice charges once, so a caller that disposes by hand and by scope does not pay twice.</summary>
    [Fact]
    public async Task DisposeAsync_TheSameClientTwice_ChargesThePeriodOnce()
    {
        // Arrange
        using var inner = ScriptedChatClient.AnsweringWithUsage("The invoice was attached.", inputTokens: 90, outputTokens: 30);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        var client = new BudgetedChatClient(inner, LedgerAllowing(), spendLedger);

        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Act
        await client.DisposeAsync();
        await client.DisposeAsync();

        // Assert
        await spendLedger.Received(1).RecordSpendAsync(Arg.Any<ChatTokenUsage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An endpoint that reports nothing leaves the token ceilings unreachable, which is why the call ceilings exist beside them.</summary>
    [Fact]
    public async Task DisposeAsync_ARunWhoseProviderReportedNoUsage_ChargesNothingAndStillCountsTheCall()
    {
        // Arrange
        using var inner = ScriptedChatClient.Answering("The invoice was attached.");
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        var client = new BudgetedChatClient(inner, LedgerAllowing(providerCalls: 1), spendLedger);

        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Act
        await client.DisposeAsync();

        // Assert
        await spendLedger.DidNotReceive().RecordSpendAsync(Arg.Any<ChatTokenUsage>(), Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A synchronous owner cannot charge the period, so releasing a run that spent tokens that way is refused rather
    /// than dropping that spend silently — every construction site awaits disposal, and this is what reports the one
    /// that stops.
    /// </summary>
    [Fact]
    public async Task Dispose_ARunWhoseTokensNothingHasCharged_IsRefused()
    {
        // Arrange
        using var inner = ScriptedChatClient.AnsweringWithUsage("The invoice was attached.", inputTokens: 90, outputTokens: 30);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        var client = new BudgetedChatClient(inner, LedgerAllowing(), spendLedger);

        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Act, Assert
        Assert.Throws<NotSupportedException>(client.Dispose);
        await spendLedger.DidNotReceive().RecordSpendAsync(Arg.Any<ChatTokenUsage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A run that charged the period is released synchronously without complaint, which is the path disposal itself takes.</summary>
    [Fact]
    public async Task Dispose_ARunThatAlreadyChargedThePeriod_IsAccepted()
    {
        // Arrange
        using var inner = ScriptedChatClient.AnsweringWithUsage("The invoice was attached.", inputTokens: 90, outputTokens: 30);
        var client = new BudgetedChatClient(inner, LedgerAllowing(), Substitute.For<IMailAnsweringSpendLedger>());

        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);
        await client.DisposeAsync();

        // Act, Assert
        client.Dispose();
    }

    /// <summary>No path may exist on which a run could stream past the ceilings this decorator applies.</summary>
    [Fact]
    public void GetStreamingResponseAsync_AnyCall_IsRefused()
    {
        // Arrange
        using var inner = ScriptedChatClient.Answering("never reached");
        using var client = new BudgetedChatClient(inner, LedgerAllowing(), Substitute.For<IMailAnsweringSpendLedger>());

        // Act, Assert
        Assert.Throws<NotSupportedException>(
            () => client.GetStreamingResponseAsync(Conversation, options: null, CancellationToken.None));
    }

    [Fact]
    public void Constructor_WithoutALedger_IsRefused()
    {
        // Arrange
        using var inner = ScriptedChatClient.Answering("never reached");

        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => new BudgetedChatClient(inner, null!, Substitute.For<IMailAnsweringSpendLedger>()));
        Assert.Throws<ArgumentNullException>(() => new BudgetedChatClient(inner, LedgerAllowing(), null!));
    }

    private static MailAnsweringRunLedger LedgerAllowing(int providerCalls = 8, long tokens = 80_000) =>
        new(MailAnsweringRunBounds.Create(20_000, providerCalls, tokens));
}
