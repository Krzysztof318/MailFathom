// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using Xunit;

namespace MailFathom.Application.UnitTests.Retrieval.AskMail;

/// <summary>Covers what one answering run may draw out of the mailbox and spend before it is stopped.</summary>
public sealed class MailAnsweringRunBoundsTests
{
    [Fact]
    public void Default_TheBoundsADeploymentReceives_AreConservativeRatherThanGenerous()
    {
        // Act
        var bounds = MailAnsweringRunBounds.Default;

        // Assert
        Assert.Equal(20_000, bounds.MaximumRetrievedCharacters);
        Assert.Equal(8, bounds.MaximumProviderCalls);
        Assert.Equal(80_000L, bounds.MaximumTokens);
    }

    /// <summary>A run that could not send even one passage would answer from nothing while appearing to have read the mailbox.</summary>
    [Fact]
    public void Default_TheRetrievedCharacterCeiling_LeavesRoomForSeveralWholeLookups()
    {
        // Act
        var oneLookup = EmailKnowledgeBounds.Default.MaximumPassages
            * EmailKnowledgeBounds.Default.MaximumCharactersPerPassage;

        // Assert
        Assert.True(MailAnsweringRunBounds.Default.MaximumRetrievedCharacters > oneLookup);
    }

    [Theory]
    [InlineData(0, 8, 80_000)]
    [InlineData(-1, 8, 80_000)]
    [InlineData(20_000, 0, 80_000)]
    [InlineData(20_000, -1, 80_000)]
    [InlineData(20_000, 8, 0)]
    [InlineData(20_000, 8, -1)]
    public void Create_ABoundNoRunCouldCompleteUnder_IsRefused(
        int retrievedCharacters,
        int providerCalls,
        long tokens)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MailAnsweringRunBounds.Create(retrievedCharacters, providerCalls, tokens));
    }

    /// <summary>One call is the floor rather than a recommendation: the model answers from whatever that call produces.</summary>
    [Fact]
    public void Create_ASingleCallRun_IsAccepted()
    {
        // Act
        var bounds = MailAnsweringRunBounds.Create(1, 1, 1);

        // Assert
        Assert.Equal(1, bounds.MaximumProviderCalls);
    }

    /// <summary>The rendering reaches a log, so it states the numbers and nothing that was measured against them.</summary>
    [Fact]
    public void ToString_TheBounds_ReportsEveryCeiling()
    {
        // Act
        var rendered = MailAnsweringRunBounds.Create(20_000, 8, 80_000).ToString();

        // Assert
        Assert.Equal("at most 20000 retrieved characters over 8 calls costing at most 80000 tokens", rendered);
    }
}
