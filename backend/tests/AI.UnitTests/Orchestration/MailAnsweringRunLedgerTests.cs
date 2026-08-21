// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Orchestration;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using Xunit;

namespace MailFathom.AI.UnitTests.Orchestration;

/// <summary>Covers what one run is allowed to send, call, and consume, and which of those refusals cuts rather than stops.</summary>
/// <remarks>
/// The ledger is decidable from its own bounds, so every test here runs it directly: no provider, no agent, and no
/// framework type is involved in deciding what a question may cost.
/// </remarks>
public sealed class MailAnsweringRunLedgerTests
{
    [Fact]
    public void AdmitPassages_ALookupInsideTheRunsCeiling_HandsOverEveryPassageAndCutsNothing()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 1_000);
        var found = PassagesOf(100, 100, 100);

        // Act
        var admitted = ledger.AdmitPassages(found);

        // Assert
        Assert.Equal(3, admitted.Count);
        Assert.False(ledger.RetrievalWasTruncated);
    }

    /// <summary>Whole passages rather than a cut across the last one: an extract ending mid-word buys a few hundred characters and costs the model a readable message.</summary>
    [Fact]
    public void AdmitPassages_ALookupPastTheRunsCeiling_HandsOverTheWholePassagesThatFitAndSaysItCut()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 250);
        var found = PassagesOf(100, 100, 100);

        // Act
        var admitted = ledger.AdmitPassages(found);

        // Assert
        Assert.Equal(2, admitted.Count);
        Assert.True(ledger.RetrievalWasTruncated);
    }

    /// <summary>Retrieval order is relevance order, so a run approaching its ceiling must not start preferring short messages to relevant ones.</summary>
    [Fact]
    public void AdmitPassages_ASmallerPassageBehindOneThatDoesNotFit_IsNotReachedForInstead()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 150);
        var found = PassagesOf(100, 200, 10);

        // Act
        var admitted = ledger.AdmitPassages(found);

        // Assert
        Assert.Single(admitted);
        Assert.Equal(100, admitted[0].Text.Length);
    }

    /// <summary>The ceiling holds across a run rather than across a lookup, which is the whole reason it exists beside the per-lookup bounds.</summary>
    [Fact]
    public void AdmitPassages_ASecondLookupAfterTheCeilingWasReached_HandsOverNothing()
    {
        // Arrange
        var ledger = LedgerAllowing(retrievedCharacters: 250);
        ledger.AdmitPassages(PassagesOf(100, 100, 100));

        // Act
        var admitted = ledger.AdmitPassages(PassagesOf(100));

        // Assert
        Assert.Empty(admitted);
        Assert.True(ledger.RetrievalWasTruncated);
    }

    [Fact]
    public void AdmitPassages_WithoutALookupResult_IsRefused()
    {
        // Arrange
        var ledger = LedgerAllowing();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ledger.AdmitPassages(null!));
    }

    [Fact]
    public void RequireAllowanceForNextCall_ARunInsideItsCallCeiling_TakesOneAllowancePerCall()
    {
        // Arrange
        var ledger = LedgerAllowing(providerCalls: 3);

        // Act, Assert
        ledger.RequireAllowanceForNextCall();
        ledger.RequireAllowanceForNextCall();
        ledger.RequireAllowanceForNextCall();

        Assert.Throws<MailAnsweringBudgetExhaustedException>(ledger.RequireAllowanceForNextCall);
    }

    /// <summary>The call ceiling is what holds when a provider reports no usage, which is why it exists beside the token one.</summary>
    [Fact]
    public void RequireAllowanceForNextCall_ARunPastItsCallCeiling_StopsTheRunRatherThanCuttingIt()
    {
        // Arrange
        var ledger = LedgerAllowing(providerCalls: 1);
        ledger.RequireAllowanceForNextCall();

        // Act
        var failure = Assert.Throws<MailAnsweringBudgetExhaustedException>(ledger.RequireAllowanceForNextCall);

        // Assert
        Assert.Equal(MailAnsweringBudgetScope.Run, failure.Scope);
    }

    /// <summary>What a call will cost is unknowable until it is answered, so the call that crosses the ceiling is paid for and the next one is refused.</summary>
    [Fact]
    public void RequireAllowanceForNextCall_ARunThatHasConsumedItsTokens_IsRefusedOnTheNextCall()
    {
        // Arrange
        var ledger = LedgerAllowing(tokens: 100);
        ledger.RequireAllowanceForNextCall();
        ledger.RecordSpend(new ChatTokenUsage(90, 30));

        // Act, Assert
        Assert.Throws<MailAnsweringBudgetExhaustedException>(ledger.RequireAllowanceForNextCall);
    }

    [Fact]
    public void RequireAllowanceForNextCall_ARunUnderItsTokenCeiling_IsStillAllowed()
    {
        // Arrange
        var ledger = LedgerAllowing(tokens: 100);
        ledger.RequireAllowanceForNextCall();
        ledger.RecordSpend(new ChatTokenUsage(40, 30));

        // Act, Assert
        ledger.RequireAllowanceForNextCall();
    }

    [Fact]
    public void RecordSpend_WithoutUsage_IsRefused()
    {
        // Arrange
        var ledger = LedgerAllowing();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ledger.RecordSpend(null!));
    }

    [Fact]
    public void Constructor_WithoutBounds_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailAnsweringRunLedger(null!));
    }

    private static MailAnsweringRunLedger LedgerAllowing(
        int retrievedCharacters = 20_000,
        int providerCalls = 8,
        long tokens = 80_000) =>
        new(MailAnsweringRunBounds.Create(retrievedCharacters, providerCalls, tokens));

    private static IReadOnlyList<EmailKnowledgePassage> PassagesOf(params int[] lengths) =>
        [.. lengths.Select(length => KnowledgePassages.Create(new string('a', length)))];
}
