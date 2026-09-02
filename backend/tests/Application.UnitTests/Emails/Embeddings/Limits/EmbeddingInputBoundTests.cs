// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Embeddings.Limits;

/// <summary>Covers the per-message ceiling's own contract; what it does to a cut is proved where the chunker is.</summary>
public sealed class EmbeddingInputBoundTests
{
    [Fact]
    public void Default_ADeploymentThatDeclaredNothing_CarriesTheShippedCeiling()
    {
        // Act, Assert
        Assert.Equal(
            EmbeddingInputBound.DefaultMaximumCharacterCount,
            EmbeddingInputBound.Default.MaximumCharacterCount);
    }

    [Fact]
    public void Create_APositiveCeiling_CarriesIt()
    {
        // Act
        var bound = EmbeddingInputBound.Create(12_345);

        // Assert
        Assert.Equal(12_345, bound.MaximumCharacterCount);
    }

    /// <summary>A ceiling that admits no character would embed nothing at all, which is a misconfiguration rather than a choice.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_ACeilingThatCouldCutNothing_IsRefused(int maximumCharacterCount)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EmbeddingInputBound.Create(maximumCharacterCount));
    }
}
