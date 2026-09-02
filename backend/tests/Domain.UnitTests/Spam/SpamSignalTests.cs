// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Spam;
using Xunit;

namespace MailFathom.Domain.UnitTests.Spam;

public sealed class SpamSignalTests
{
    private static readonly SpamSignalProvenance Header =
        SpamSignalProvenance.FromMessageHeader("Authentication-Results");

    [Fact]
    public void Create_AnObservationLongerThanTheBound_KeepsTheBeginningOfIt()
    {
        // Arrange
        var written = new string('x', SpamSignal.MaximumObservationLength + 100);

        // Act
        var signal = SpamSignal.Create(SpamSignalKind.ProviderSpamVerdict, "X-Spam-Status", written, Header);

        // Assert
        Assert.Equal(new string('x', SpamSignal.MaximumObservationLength), signal.Observation);
    }

    /// <summary>A folded header arrives with its line breaks, and two records of one observation must be one value.</summary>
    [Theory]
    [InlineData("Yes,\r\n  score=15.2", "Yes, score=15.2")]
    [InlineData("  pass   smtp.mailfrom=example.test  ", "pass smtp.mailfrom=example.test")]
    [InlineData("dkim=pass\theader.d=example.test", "dkim=pass header.d=example.test")]
    public void Create_AnObservationCarryingFoldingWhitespace_CollapsesItToSingleSpaces(
        string written,
        string expected)
    {
        // Arrange, Act
        var signal = SpamSignal.Create(SpamSignalKind.SenderAuthentication, "dkim", written, Header);

        // Assert
        Assert.Equal(expected, signal.Observation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void Create_AnObservationHoldingNothingReadable_IsAbsentRatherThanEmpty(string? written)
    {
        // Arrange, Act
        var signal = SpamSignal.Create(SpamSignalKind.JunkFolderPlacement, "JUNK", written, Header);

        // Assert
        Assert.Null(signal.Observation);
    }

    [Fact]
    public void Create_ANameLongerThanTheBound_IsRefusedRatherThanShortened()
    {
        // Arrange
        var written = new string('r', SpamSignal.MaximumNameLength + 1);

        // Act, Assert
        var failure = Assert.Throws<ArgumentException>(() =>
            SpamSignal.Create(SpamSignalKind.ScannerRule, written, observation: null, Header));

        Assert.Equal("name", failure.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_ANameHoldingNothing_IsRefused(string? name)
    {
        // Arrange, Act, Assert
        Assert.ThrowsAny<ArgumentException>(() =>
            SpamSignal.Create(SpamSignalKind.ScannerRule, name!, observation: null, Header));
    }

    [Fact]
    public void Create_ANameCarryingAControlCharacter_IsRefused()
    {
        // Arrange, Act, Assert
        var failure = Assert.Throws<ArgumentException>(() =>
            SpamSignal.Create(SpamSignalKind.ScannerRule, "RULE\u0007NAME", observation: null, Header));

        Assert.Equal("name", failure.ParamName);
    }

    [Fact]
    public void Create_AKindOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SpamSignal.Create((SpamSignalKind)99, "dkim", observation: null, Header));
    }

    [Fact]
    public void Create_NoProvenance_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            SpamSignal.Create(SpamSignalKind.SenderAuthentication, "dkim", observation: null, provenance: null!));
    }
}
