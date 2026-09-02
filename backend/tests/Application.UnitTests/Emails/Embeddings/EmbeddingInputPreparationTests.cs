// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings;

/// <summary>Covers what a declaration may say about how a passage reaches the model, since each of it changes the vector.</summary>
public sealed class EmbeddingInputPreparationTests
{
    /// <summary>A preparation is exactly what was declared, with nothing normalized on the way in.</summary>
    [Fact]
    public void Create_ADeclaredPreparation_KeepsEveryValueAsGiven()
    {
        // Act
        var preparation = EmbeddingInputPreparation.Create(8000, "Passage:", normalizesVector: true);

        // Assert
        Assert.Equal(8000, preparation.InputCharacterLimit);
        Assert.Equal("Passage:", preparation.PassageInstruction);
        Assert.True(preparation.NormalizesVector);
    }

    /// <summary>Most models require no prefix, so an absent instruction is the ordinary case rather than a missing value.</summary>
    [Fact]
    public void Create_AModelRequiringNoPrefix_RecordsNoInstruction()
    {
        // Act
        var preparation = EmbeddingInputPreparation.Create(8000, passageInstruction: null, normalizesVector: false);

        // Assert
        Assert.Null(preparation.PassageInstruction);
        Assert.False(preparation.NormalizesVector);
    }

    /// <summary>A limit of nothing would cut every passage away and send an empty request for each of them.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_AnInputLimitThatIsNotPositive_IsRefused(int inputCharacterLimit)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmbeddingInputPreparation.Create(inputCharacterLimit, passageInstruction: null, normalizesVector: true));
    }

    /// <summary>
    /// A prefix of spaces is a misconfigured declaration rather than an instruction, and accepting it would register a
    /// second profile for a space identical to one already registered.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankInstruction_IsRefused(string blank)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            EmbeddingInputPreparation.Create(8000, blank, normalizesVector: true));
    }

    /// <summary>An instruction longer than its column would be refused by the database at the write instead of at the declaration.</summary>
    [Fact]
    public void Create_AnInstructionLongerThanItsColumn_IsRefused()
    {
        // Arrange
        var instruction = new string('i', EmbeddingInputPreparation.MaximumPassageInstructionLength + 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            EmbeddingInputPreparation.Create(8000, instruction, normalizesVector: true));
    }
}
