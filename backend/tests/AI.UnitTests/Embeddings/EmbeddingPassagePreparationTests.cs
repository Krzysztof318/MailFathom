// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.AI.UnitTests.Embeddings;

/// <summary>Covers what a model is shown, and that a spend ceiling counts exactly that.</summary>
public sealed class EmbeddingPassagePreparationTests
{
    [Fact]
    public void Prepare_APassageInsideTheLimit_SendsItWithTheInstructionInFront()
    {
        // Arrange
        var preparation = EmbeddingInputPreparation.Create(
            inputCharacterLimit: 100,
            passageInstruction: "passage: ",
            normalizesVector: true);

        // Act
        var prepared = EmbeddingPassagePreparation.Prepare("the west stairwell", preparation);

        // Assert
        Assert.Equal("passage: the west stairwell", prepared);
    }

    [Fact]
    public void Prepare_APassageBeyondTheLimit_KeepsItsBeginning()
    {
        // Arrange
        var preparation = EmbeddingInputPreparation.Create(
            inputCharacterLimit: 8,
            passageInstruction: null,
            normalizesVector: true);

        // Act
        var prepared = EmbeddingPassagePreparation.Prepare("the west stairwell", preparation);

        // Assert
        Assert.Equal("the west", prepared);
    }

    /// <summary>
    /// The spend ceiling counts what a provider is sent, and it counts it without building the text. That is only safe
    /// while the two agree exactly, so the agreement is asserted rather than assumed — a preparation rule changed in one
    /// of the two places would otherwise charge a budget for characters nobody sent, or send some it never charged for.
    /// </summary>
    [Theory]
    [InlineData("", null, 100)]
    [InlineData("short", null, 100)]
    [InlineData("a passage that is longer than the limit allows", null, 12)]
    [InlineData("short", "passage: ", 100)]
    [InlineData("a passage that is longer than the limit allows", "passage: ", 12)]
    [InlineData("exactly at the limit", null, 20)]
    public void CountBilledCharacters_AnyPassage_AgreesWithWhatPreparationWouldSend(
        string passage,
        string? passageInstruction,
        int inputCharacterLimit)
    {
        // Arrange
        var preparation = EmbeddingInputPreparation.Create(
            inputCharacterLimit,
            passageInstruction,
            normalizesVector: true);

        // Act
        var counted = preparation.CountBilledCharacters(passage);

        // Assert
        Assert.Equal(EmbeddingPassagePreparation.Prepare(passage, preparation).Length, counted);
    }

    [Fact]
    public void CountBilledCharacters_AMissingPassage_IsRefused()
    {
        // Arrange
        var preparation = EmbeddingInputPreparation.Create(
            inputCharacterLimit: 100,
            passageInstruction: null,
            normalizesVector: true);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => preparation.CountBilledCharacters(null!));
    }
}
