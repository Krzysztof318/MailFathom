// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authorship;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authorship;

/// <summary>Covers what an authorship reading may record, and what separates one from the absence of a reading.</summary>
public sealed class MachineAuthorshipAssessmentTests
{
    /// <summary>The absence of a reading is one value, so a reading that ran may not claim to be it.</summary>
    [Fact]
    public void Assessed_NotAssessedBand_IsRefused()
    {
        // Arrange
        var revision = MachineAuthorshipProfile.Standard.Revision;

        // Act
        void Assessed() => MachineAuthorshipAssessment.Assessed(
            MachineAuthorshipBand.NotAssessed,
            likelihood: 0,
            MachineAuthorshipSignals.None,
            revision);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(Assessed);
    }

    /// <summary>A likelihood outside the scale would compare unpredictably against every threshold that reads it.</summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Assessed_LikelihoodOutsideTheScale_IsRefused(double likelihood)
    {
        // Arrange
        var revision = MachineAuthorshipProfile.Standard.Revision;

        // Act
        void Assessed() => MachineAuthorshipAssessment.Assessed(
            MachineAuthorshipBand.Possible,
            likelihood,
            MachineAuthorshipSignals.None,
            revision);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(Assessed);
    }

    /// <summary>Both ends of the scale are readings a profile can legitimately reach.</summary>
    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    public void Assessed_LikelihoodAtEitherEndOfTheScale_IsAccepted(double likelihood)
    {
        // Arrange
        var revision = MachineAuthorshipProfile.Standard.Revision;

        // Act
        var assessment = MachineAuthorshipAssessment.Assessed(
            MachineAuthorshipBand.Likely,
            likelihood,
            MachineAuthorshipSignals.TagCharacters,
            revision);

        // Assert
        Assert.Equal(likelihood, assessment.Likelihood);
        Assert.True(assessment.WasAssessed);
    }

    /// <summary>A message nothing read carries no profile, which is what separates it from one read and found ordinary.</summary>
    [Fact]
    public void NotAssessed_CarriesNoProfileAndNoSignals()
    {
        // Act
        var assessment = MachineAuthorshipAssessment.NotAssessed;

        // Assert
        Assert.Equal(MachineAuthorshipBand.NotAssessed, assessment.Band);
        Assert.Equal(MachineAuthorshipSignals.None, assessment.Signals);
        Assert.Equal(0, assessment.Likelihood);
        Assert.Equal(MachineAuthorshipProfileRevision.None, assessment.ProfileRevision);
        Assert.False(assessment.WasAssessed);
    }
}
