// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
/// per call: whether this question may make another, and what the answer to it cost.
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
        using var client = new BudgetedChatClient(inner, LedgerAllowing(providerCalls: 2), Substitute.For<IMailAnsweringSpendLedger>());

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
        using var client = new BudgetedChatClient(inner, LedgerAllowing(providerCalls: 1), Substitute.For<IMailAnsweringSpendLedger>());

        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Run, failure.Scope);
        Assert.Single(inner.Calls);
    }

    /// <summary>
    /// Both ledgers are written, because they answer different questions: the run's is what stops this question, and the
    /// period's is what stops the next one.
    /// </summary>
    [Fact]
    public async Task GetResponseAsync_AProviderThatReportedUsage_ChargesItToTheRunAndToThePeriod()
    {
        // Arrange
        using var inner = ScriptedChatClient.AnsweringWithUsage("The invoice was attached.", inputTokens: 90, outputTokens: 30);
        var runLedger = LedgerAllowing(tokens: 100);
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        using var client = new BudgetedChatClient(inner, runLedger, spendLedger);

        // Act
        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Assert
        spendLedger.Received(1).RecordSpend(new ChatTokenUsage(90, 30));
        await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));
    }

    /// <summary>An endpoint that reports nothing leaves the token ceilings unreachable, which is why the call ceilings exist beside them.</summary>
    [Fact]
    public async Task GetResponseAsync_AProviderThatReportedNoUsage_ChargesNothingAndStillCountsTheCall()
    {
        // Arrange
        using var inner = ScriptedChatClient.Answering("The invoice was attached.");
        var spendLedger = Substitute.For<IMailAnsweringSpendLedger>();
        using var client = new BudgetedChatClient(inner, LedgerAllowing(providerCalls: 1), spendLedger);

        // Act
        await client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken);

        // Assert
        spendLedger.DidNotReceive().RecordSpend(Arg.Any<ChatTokenUsage>());
        await Assert.ThrowsAsync<MailAnsweringBudgetExhaustedException>(
            () => client.GetResponseAsync(Conversation, options: null, TestContext.Current.CancellationToken));
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
