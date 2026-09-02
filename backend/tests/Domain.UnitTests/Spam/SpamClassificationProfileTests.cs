// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Domain.UnitTests.Spam;

/// <summary>Covers what the identity is derived from, and what it deliberately leaves out.</summary>
public sealed class SpamClassificationProfileTests
{
    [Fact]
    public void Create_TheSameSettings_DerivesTheSameIdentity()
    {
        // Arrange, Act
        var first = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5);
        var second = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(SpamClassificationProfile.LengthInCharacters, first.Value.Length);
    }

    [Fact]
    public void Create_TheScannerSwitchedOn_DerivesAnotherIdentity()
    {
        // Arrange, Act
        var deterministic = SpamClassificationProfile.Create(usesScanner: false, scannerThreshold: null);
        var scanned = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: null);

        // Assert
        Assert.NotEqual(deterministic, scanned);
    }

    [Fact]
    public void Create_AThresholdMoved_DerivesAnotherIdentity()
    {
        // Arrange, Act
        var lenient = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 8);
        var strict = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5);

        // Assert
        Assert.NotEqual(lenient, strict);
    }

    /// <summary>What a scanner's own threshold is, is not known before it is asked, so the two cannot be claimed equal.</summary>
    [Fact]
    public void Create_TheScannersOwnThresholdAndOneConfiguredToTheSameNumber_AreDifferentTerms()
    {
        // Arrange, Act
        var scannersOwn = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: null);
        var configured = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5);

        // Assert
        Assert.NotEqual(scannersOwn, configured);
    }

    [Fact]
    public void Create_AThresholdThatIsNotAFiniteNumber_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentOutOfRangeException>(
            () => SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: double.NaN));

        Assert.Equal("scannerThreshold", failure.ParamName);
    }

    [Fact]
    public void Restore_AnIdentityThisTypeDerived_ComparesEqualToIt()
    {
        // Arrange
        var derived = SpamClassificationProfile.Create(usesScanner: true, scannerThreshold: 5);

        // Act
        var restored = SpamClassificationProfile.Restore(derived.Value);

        // Assert
        Assert.Equal(derived, restored);
        Assert.True(restored.IsSpecified);
    }

    [Theory]
    [InlineData("ABCDEF012345")]
    [InlineData("abcdef01234")]
    [InlineData("abcdef0123456")]
    [InlineData("abcdefg12345")]
    public void Restore_AValueThisTypeCouldNotHaveProduced_IsRefused(string value)
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() => SpamClassificationProfile.Restore(value));

        Assert.Equal("value", failure.ParamName);
    }

    /// <summary>The struct default names no terms, which is what a record written before the profile existed reads as.</summary>
    [Fact]
    public void IsSpecified_TheStructDefault_NamesNoTerms()
    {
        // Arrange
        var unspecified = default(SpamClassificationProfile);

        // Act, Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Value);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }
}
