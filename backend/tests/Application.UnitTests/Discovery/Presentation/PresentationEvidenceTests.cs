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

    private static PresentationText Text(string text) => PresentationPlanExample.Text(text);

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

    /// <summary>Staleness is about a source, so a block that names none has nothing to be stale about.</summary>
    [Fact]
    public void Constructor_StaleWithNoCitation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Stale,
            [],
            PresentationFreshness.StaleSince(ObservedAt)));
    }

    /// <summary>The verdict and the freshness are two readings of one fact, so a plan never carries them disagreeing.</summary>
    [Fact]
    public void Constructor_StaleOverACurrentCopy_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Stale,
            [First],
            PresentationFreshness.CurrentAt(ObservedAt)));
    }

    /// <summary>The same disagreement in the other direction: backed mail read from a copy known to be behind is stale.</summary>
    [Fact]
    public void Constructor_SupportedOverACopyKnownToBeBehind_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Supported,
            [First],
            PresentationFreshness.StaleSince(ObservedAt)));
    }

    [Fact]
    public void Constructor_StaleOverACopyKnownToBeBehind_KeepsTheSource()
    {
        // Act
        var evidence = new PresentationEvidence(
            PresentationSupport.Stale,
            [First],
            PresentationFreshness.StaleSince(ObservedAt));

        // Assert
        Assert.Equal([First], evidence.Citations);
    }

    /// <summary>Reporting that sources disagree without saying what either says leaves a reader nothing to act on.</summary>
    [Fact]
    public void Constructor_ConflictingWithOneSide_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationEvidence.Conflicting(
            [First, Second],
            PresentationFreshness.CurrentAt(ObservedAt),
            [new ConflictingClaim(Text("£40,000"), [First])]));
    }

    /// <summary>A side naming a source the block does not rest on is a citation the plan never resolves.</summary>
    [Fact]
    public void Constructor_ASideNamingASourceTheBlockDoesNotRestOn_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationEvidence.Conflicting(
            [First, Second],
            PresentationFreshness.CurrentAt(ObservedAt),
            [
                new ConflictingClaim(Text("£40,000"), [First]),
                new ConflictingClaim(Text("£44,000"), [PresentationCitationId.Create("c9")]),
            ]));
    }

    /// <summary>A block that agrees with itself has no sides to present.</summary>
    [Fact]
    public void Constructor_SidesOnASupportedBlock_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PresentationEvidence(
            PresentationSupport.Supported,
            [First, Second],
            PresentationFreshness.CurrentAt(ObservedAt),
            [
                new ConflictingClaim(Text("£40,000"), [First]),
                new ConflictingClaim(Text("£44,000"), [Second]),
            ]));
    }

    /// <summary>Both figures reach the reader, which is the whole point of not resolving the disagreement.</summary>
    [Fact]
    public void Conflicting_TwoSourcesThatDisagree_CarriesBothSides()
    {
        // Act
        var evidence = PresentationEvidence.Conflicting(
            [First, Second],
            PresentationFreshness.CurrentAt(ObservedAt),
            [
                new ConflictingClaim(Text("£40,000"), [First]),
                new ConflictingClaim(Text("£44,000"), [Second]),
            ]);

        // Assert
        Assert.Equal(PresentationSupport.Conflicting, evidence.Support);
        Assert.Equal(
            ["£40,000", "£44,000"],
            evidence.ConflictingClaims.Select(side => side.Statement.Value));
    }

    /// <summary>A side nothing backs is a claim the correspondence does not make.</summary>
    [Fact]
    public void ConflictingClaim_ASideNamingNoSource_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new ConflictingClaim(Text("£40,000"), []));
    }

    /// <summary>A question asked so broadly that six answers disagree is a question to narrow, not a conflict to draw.</summary>
    [Fact]
    public void Constructor_MoreSidesThanTheBound_IsRefused()
    {
        // Arrange
        var citations = Enumerable
            .Range(0, PresentationEvidence.MaxConflictingClaims + 1)
            .Select(index => PresentationCitationId.Create($"c{index}"))
            .ToArray();
        var sides = citations
            .Select(citation => new ConflictingClaim(Text(citation.Value), [citation]))
            .ToArray();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => PresentationEvidence.Conflicting(
            citations,
            PresentationFreshness.CurrentAt(ObservedAt),
            sides));
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
