// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Citations;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers what a block has to say about the correspondence behind it.</summary>
public sealed class PresentationEvidenceTests
{
    private static readonly DateTimeOffset ObservedAt = PresentationPlanExample.ObservedAt;

    private static PresentationCitationId First => PresentationPlanExample.FirstCitation;

    private static PresentationCitationId Second => PresentationPlanExample.SecondCitation;

    /// <summary>The failure a citation contract is written to prevent: a claim asserting support and naming nothing.</summary>
    [Fact]
    public void Constructor_SupportedWithNoCitation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Supported,
            [],
            PresentationFreshness.CurrentAt(ObservedAt)));
    }

    /// <summary>A block naming a source is supported by it, whatever the producer called the state.</summary>
    [Fact]
    public void Constructor_UnsupportedWithACitation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Unsupported,
            [First],
            PresentationFreshness.Unknown));
    }

    /// <summary>A disagreement needs two sides.</summary>
    [Fact]
    public void Constructor_ConflictingWithOneCitation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Conflicting,
            [First],
            PresentationFreshness.CurrentAt(ObservedAt)));
    }

    [Fact]
    public void Constructor_ConflictingWithTwoCitations_KeepsBoth()
    {
        // Act
        var evidence = new PresentationEvidence(
            PresentationSupport.Conflicting,
            [First, Second],
            PresentationFreshness.CurrentAt(ObservedAt));

        // Assert
        Assert.Equal([First, Second], evidence.Citations);
    }

    [Fact]
    public void Constructor_TheSameCitationTwice_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Conflicting,
            [First, First],
            PresentationFreshness.CurrentAt(ObservedAt)));
    }

    [Fact]
    public void Constructor_AnUnspecifiedCitation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Supported,
            [default],
            PresentationFreshness.CurrentAt(ObservedAt)));
    }

    /// <summary>An answer resting on more messages than a person will read is retrieval that failed to narrow.</summary>
    [Fact]
    public void Constructor_MoreCitationsThanTheBound_IsRefused()
    {
        // Arrange
        var tooMany = Enumerable
            .Range(0, PresentationEvidence.MaxCitations + 1)
            .Select(index => PresentationCitationId.Create($"c{index}"))
            .ToArray();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Supported,
            tooMany,
            PresentationFreshness.CurrentAt(ObservedAt)));
    }

    [Fact]
    public void Unsupported_ABlockNothingBacks_NamesNoSource()
    {
        // Act
        var evidence = PresentationEvidence.Unsupported(PresentationFreshness.Unknown);

        // Assert
        Assert.Equal(PresentationSupport.Unsupported, evidence.Support);
        Assert.Empty(evidence.Citations);
    }

    /// <summary>The list is copied, so a caller that keeps mutating theirs cannot change what a plan already said.</summary>
    [Fact]
    public void Constructor_ACitationListTheCallerKeeps_CopiesIt()
    {
        // Arrange
        var mutable = new List<PresentationCitationId> { First };

        // Act
        var evidence = new PresentationEvidence(
            PresentationSupport.Supported,
            mutable,
            PresentationFreshness.CurrentAt(ObservedAt));
        mutable.Add(Second);

        // Assert
        Assert.Equal([First], evidence.Citations);
    }
}
