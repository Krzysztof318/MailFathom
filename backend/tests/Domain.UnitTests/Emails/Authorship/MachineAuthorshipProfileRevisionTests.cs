// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authorship;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authorship;

/// <summary>Covers what makes two stored likelihoods comparable, and what makes one of them say nothing.</summary>
public sealed class MachineAuthorshipProfileRevisionTests
{
    /// <summary>A weighting names itself by what it decides, so writing the same table in another order changes nothing.</summary>
    [Fact]
    public void Of_SameWeightsWrittenInAnotherOrder_ProducesOneRevision()
    {
        // Arrange
        var written = new Dictionary<MachineAuthorshipSignals, double>
        {
            [MachineAuthorshipSignals.TagCharacters] = 0.9,
            [MachineAuthorshipSignals.HiddenCharacters] = 0.6,
        };
        var reordered = new Dictionary<MachineAuthorshipSignals, double>
        {
            [MachineAuthorshipSignals.HiddenCharacters] = 0.6,
            [MachineAuthorshipSignals.TagCharacters] = 0.9,
        };

        // Act
        var first = MachineAuthorshipProfileRevision.Of(written, [0.3, 0.65]);
        var second = MachineAuthorshipProfileRevision.Of(reordered, [0.3, 0.65]);

        // Assert
        Assert.Equal(first, second);
        Assert.True(first.NamesAProfile);
    }

    /// <summary>A moved weight is a different judgement, and a stored answer has to be readable as having come from the earlier one.</summary>
    [Fact]
    public void Of_AMovedWeight_ProducesADifferentRevision()
    {
        // Arrange
        var before = new Dictionary<MachineAuthorshipSignals, double>
        {
            [MachineAuthorshipSignals.TagCharacters] = 0.9,
        };
        var after = new Dictionary<MachineAuthorshipSignals, double>
        {
            [MachineAuthorshipSignals.TagCharacters] = 0.85,
        };

        // Act
        var first = MachineAuthorshipProfileRevision.Of(before, [0.3, 0.65]);
        var second = MachineAuthorshipProfileRevision.Of(after, [0.3, 0.65]);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>A moved band boundary changes what the same number is reported as, so it changes the revision too.</summary>
    [Fact]
    public void Of_AMovedBandBoundary_ProducesADifferentRevision()
    {
        // Arrange
        var weights = new Dictionary<MachineAuthorshipSignals, double>
        {
            [MachineAuthorshipSignals.TagCharacters] = 0.9,
        };

        // Act
        var first = MachineAuthorshipProfileRevision.Of(weights, [0.3, 0.65]);
        var second = MachineAuthorshipProfileRevision.Of(weights, [0.3, 0.7]);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>A profile that weighs nothing reads nothing, and is deliberately indistinguishable from having no profile.</summary>
    [Fact]
    public void Of_NoWeights_IsNone()
    {
        // Act
        var revision = MachineAuthorshipProfileRevision.Of(
            new Dictionary<MachineAuthorshipSignals, double>(),
            [0.3, 0.65]);

        // Assert
        Assert.Equal(MachineAuthorshipProfileRevision.None, revision);
        Assert.False(revision.NamesAProfile);
        Assert.Equal(string.Empty, revision.Value);
    }

    /// <summary>A column holding nothing says no profile judged the row, whichever empty form it holds.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromStoredValue_AnEmptyColumn_IsNone(string? stored)
    {
        // Act
        var revision = MachineAuthorshipProfileRevision.FromStoredValue(stored);

        // Assert
        Assert.Equal(MachineAuthorshipProfileRevision.None, revision);
    }

    /// <summary>A stored revision is opaque and is read back exactly as it was written.</summary>
    [Fact]
    public void FromStoredValue_ARecordedRevision_ReadsBackEqualToIt()
    {
        // Arrange
        var recorded = MachineAuthorshipProfile.Standard.Revision;

        // Act
        var read = MachineAuthorshipProfileRevision.FromStoredValue(recorded.Value);

        // Assert
        Assert.Equal(recorded, read);
    }

    /// <summary>A revision reads as its digest wherever text is asked of it, which is what a log line carries.</summary>
    [Fact]
    public void ToString_ARecordedRevision_IsTheDigestItself()
    {
        // Arrange
        var recorded = MachineAuthorshipProfile.Standard.Revision;

        // Act
        var written = recorded.ToString();

        // Assert
        Assert.Equal(recorded.Value, written);
    }

    /// <summary>The revision fits the column every row stores it in.</summary>
    [Fact]
    public void Revision_OfTheStandardProfile_FitsTheStoredLength()
    {
        // Act
        var revision = MachineAuthorshipProfile.Standard.Revision;

        // Assert
        Assert.Equal(MachineAuthorshipProfileRevision.Length, revision.Value.Length);
    }
}
