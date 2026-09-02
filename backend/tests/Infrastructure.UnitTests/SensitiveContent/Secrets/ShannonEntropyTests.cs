// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.SensitiveContent.Secrets;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.Secrets;

/// <summary>Covers the measurement the entropy heuristic reaches its verdict from.</summary>
public sealed class ShannonEntropyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("aaaaaaaaaaaaaaaa")]
    public void BitsPerCharacter_ARunThatVariesInNothing_MeasuresZero(string text)
    {
        // Act, Assert
        Assert.Equal(0, ShannonEntropy.BitsPerCharacter(text));
    }

    /// <summary>An alphabet of two used evenly is one bit a character, which is what makes the scale readable.</summary>
    [Fact]
    public void BitsPerCharacter_TwoValuesUsedEvenly_MeasuresOneBit()
    {
        // Act, Assert
        Assert.Equal(1, ShannonEntropy.BitsPerCharacter("abababab"));
    }

    /// <summary>The floor exists to separate these two, so the measurement has to put them on opposite sides of it.</summary>
    [Fact]
    public void BitsPerCharacter_ACredentialRunsFarAboveOrdinaryProse()
    {
        // Arrange
        var prose = "the quick brown fox jumps over the lazy dog again and again";

        // Act
        var credential = ShannonEntropy.BitsPerCharacter("Zq7ZkR3vXp8LmT2wYc5NbJ6hQ4sD9fG1aE0uIoPrWxV");
        var written = ShannonEntropy.BitsPerCharacter(prose);

        // Assert
        Assert.True(credential > written, $"a credential measured {credential} and prose measured {written}");
        Assert.InRange(credential, 5, 6);
    }
}
