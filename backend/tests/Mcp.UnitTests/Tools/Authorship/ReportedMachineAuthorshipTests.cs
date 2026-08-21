// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authorship;
using MailFathom.Mcp.Tools.Authorship;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Authorship;

/// <summary>What the boundary publishes for how much an email's own text read as machine written.</summary>
/// <remarks>
/// The expected values are asserted as their member names rather than as the internal enumeration, because the wire
/// value is the camel-cased member name and a public test signature cannot carry an internal enum. Naming them here is
/// therefore both what the accessibility rules allow and what states the published contract.
/// </remarks>
public sealed class ReportedMachineAuthorshipTests
{
    /// <summary>Every band a stored row can hold reaches the wire under its own name.</summary>
    [Theory]
    [InlineData(MachineAuthorshipBand.Unlikely, "Unlikely")]
    [InlineData(MachineAuthorshipBand.Possible, "Possible")]
    [InlineData(MachineAuthorshipBand.Likely, "Likely")]
    public void From_AnAssessedBand_PublishesItUnderItsOwnName(MachineAuthorshipBand band, string expected)
    {
        // Arrange
        var stored = MachineAuthorshipAssessment.Assessed(
            band,
            likelihood: 0.5,
            MachineAuthorshipSignals.None,
            MachineAuthorshipProfile.Standard.Revision);

        // Act
        var published = ReportedMachineAuthorship.From(stored);

        // Assert
        Assert.Equal(expected, published.State.ToString());
        Assert.Equal(0.5, published.Likelihood);
    }

    /// <summary>A message nothing read publishes that fact rather than the lowest reading, which would be a claim.</summary>
    [Fact]
    public void From_AMessageNothingAssessed_PublishesNotAssessedAndAZeroLikelihood()
    {
        // Act
        var published = ReportedMachineAuthorship.From(MachineAuthorshipAssessment.NotAssessed);

        // Assert
        Assert.Equal("NotAssessed", published.State.ToString());
        Assert.Equal(0, published.Likelihood);
    }

    /// <summary>
    /// A band added to the domain without a published value is refused rather than published as the nearest one, so a
    /// client is never told a reading that nobody decided to publish.
    /// </summary>
    [Fact]
    public void From_ABandWithNoPublishedValue_IsRefused()
    {
        // Arrange
        var stored = MachineAuthorshipAssessment.Assessed(
            (MachineAuthorshipBand)99,
            likelihood: 0.5,
            MachineAuthorshipSignals.None,
            MachineAuthorshipProfile.Standard.Revision);

        // Act
        var refusal = Record.Exception(() => ReportedMachineAuthorship.From(stored));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }

    /// <summary>The number is republished exactly, because a caller comparing two readings compares these values.</summary>
    [Fact]
    public void From_AnAssessedLikelihood_IsPublishedUnchanged()
    {
        // Arrange
        var stored = MachineAuthorshipAssessment.Assessed(
            MachineAuthorshipBand.Likely,
            likelihood: 0.9,
            MachineAuthorshipSignals.TagCharacters,
            MachineAuthorshipProfile.Standard.Revision);

        // Act
        var published = ReportedMachineAuthorship.From(stored);

        // Assert
        Assert.Equal(0.9, published.Likelihood);
    }
}
