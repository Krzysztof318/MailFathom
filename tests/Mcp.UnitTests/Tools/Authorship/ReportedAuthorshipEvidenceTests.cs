// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authorship;
using MailFathom.Mcp.Tools.Authorship;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Authorship;

/// <summary>What the single-email read publishes about the text behind an authorship likelihood.</summary>
/// <remarks>
/// The signals are asserted as their member names for the reason the sender verdict's are: the wire value is the
/// camel-cased member name, and a public test signature cannot carry an internal enum.
/// </remarks>
public sealed class ReportedAuthorshipEvidenceTests
{
    /// <summary>Every stored signal reaches the wire under its own name, strongest first.</summary>
    [Fact]
    public void From_EverySignalTheDomainDefines_PublishesThemAllStrongestFirst()
    {
        // Arrange
        var stored = MachineAuthorshipAssessment.Assessed(
            MachineAuthorshipBand.Likely,
            likelihood: 0.99,
            MachineAuthorshipSignals.HiddenCharacters
            | MachineAuthorshipSignals.TagCharacters
            | MachineAuthorshipSignals.BidirectionalOverrides
            | MachineAuthorshipSignals.VariationSelectorRun
            | MachineAuthorshipSignals.UnspacedEmDashes
            | MachineAuthorshipSignals.UniformTypography
            | MachineAuthorshipSignals.UnsolicitedListScaffolding
            | MachineAuthorshipSignals.FormulaicFraming,
            MachineAuthorshipProfile.Standard.Revision);

        // Act
        var published = ReportedAuthorshipEvidence.From(stored);

        // Assert
        Assert.Equal(
            [
                "TagCharacters",
                "VariationSelectorRun",
                "HiddenCharacters",
                "BidirectionalOverrides",
                "FormulaicFraming",
                "UnspacedEmDashes",
                "ListScaffolding",
                "UniformTypography",
            ],
            published.Signals.Select(signal => signal.ToString()));
    }

    /// <summary>A text that was read and carried nothing publishes an empty list beside the profile that read it.</summary>
    [Fact]
    public void From_AnAssessmentWithNoSignals_PublishesNoneAndNamesTheProfile()
    {
        // Arrange
        var revision = MachineAuthorshipProfile.Standard.Revision;
        var stored = MachineAuthorshipAssessment.Assessed(
            MachineAuthorshipBand.Unlikely,
            likelihood: 0,
            MachineAuthorshipSignals.None,
            revision);

        // Act
        var published = ReportedAuthorshipEvidence.From(stored);

        // Assert
        Assert.Empty(published.Signals);
        Assert.Equal(revision.Value, published.ProfileRevision);
    }

    /// <summary>A message nothing assessed names no profile, which is what says the number came from nowhere.</summary>
    [Fact]
    public void From_AMessageNothingAssessed_PublishesNoProfileRevision()
    {
        // Act
        var published = ReportedAuthorshipEvidence.From(MachineAuthorshipAssessment.NotAssessed);

        // Assert
        Assert.Empty(published.Signals);
        Assert.Null(published.ProfileRevision);
    }

    /// <summary>
    /// A signal added to the domain without a published value is refused rather than dropped, so the omission surfaces
    /// where it was made instead of leaving a client an evidence list that silently understates what was read.
    /// </summary>
    [Fact]
    public void From_ASignalWithNoPublishedValue_IsRefused()
    {
        // Arrange
        var stored = MachineAuthorshipAssessment.Assessed(
            MachineAuthorshipBand.Possible,
            likelihood: 0.5,
            (MachineAuthorshipSignals)512,
            MachineAuthorshipProfile.Standard.Revision);

        // Act
        var refusal = Record.Exception(() => ReportedAuthorshipEvidence.From(stored));

        // Assert
        Assert.IsType<ArgumentOutOfRangeException>(refusal);
    }

    /// <summary>Only the signals the text carried are published, so the list reads as a reason rather than a catalogue.</summary>
    [Fact]
    public void From_SomeSignals_PublishesOnlyThose()
    {
        // Arrange
        var stored = MachineAuthorshipAssessment.Assessed(
            MachineAuthorshipBand.Possible,
            likelihood: 0.6,
            MachineAuthorshipSignals.HiddenCharacters | MachineAuthorshipSignals.UnspacedEmDashes,
            MachineAuthorshipProfile.Standard.Revision);

        // Act
        var published = ReportedAuthorshipEvidence.From(stored);

        // Assert
        Assert.Equal(
            ["HiddenCharacters", "UnspacedEmDashes"],
            published.Signals.Select(signal => signal.ToString()));
    }
}
