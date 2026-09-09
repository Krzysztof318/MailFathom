// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Mailboxes;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Mailboxes;

/// <summary>Covers what a list narrowed by a reading a derivation already made validates and normalizes.</summary>
public sealed class EmailMarkSelectionTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 7, 6, 6, 0, 0, TimeSpan.Zero);

    /// <summary>A request that narrowed by no reading narrows by nothing, rather than by a criterion admitting everything.</summary>
    [Fact]
    public void Create_NothingNamed_NarrowsByNoReading()
    {
        // Act
        var selection = EmailMarkSelection.Create(aspect: null, dueOnOrAfter: null, dueBefore: null);

        // Assert
        Assert.Null(selection);
    }

    [Theory]
    [InlineData(EmailEnrichmentAspect.Sense)]
    [InlineData(EmailEnrichmentAspect.Significance)]
    [InlineData(EmailEnrichmentAspect.Commitment)]
    public void Create_OneReading_KeepsTheReadingTheListIsNarrowedTo(EmailEnrichmentAspect aspect)
    {
        // Act
        var selection = EmailMarkSelection.Create(aspect, dueOnOrAfter: null, dueBefore: null);

        // Assert
        Assert.Equal(aspect, selection?.Aspect);
    }

    /// <summary>A due range whose end falls on or before its start selects nothing, and is refused rather than answered with an empty page.</summary>
    [Fact]
    public void Create_DueRangeThatSelectsNothing_IsRejected()
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() => EmailMarkSelection.Create(
            EmailEnrichmentAspect.Commitment,
            MondayMorning.AddDays(7),
            MondayMorning));

        // Assert
        Assert.Equal("commitment due range", failure.FilterName);
    }

    /// <summary>A caller naming a value no derivation produces is refused, rather than narrowing to mail nothing can carry.</summary>
    [Fact]
    public void Create_ReadingThatNamesNoAspect_IsRejected()
    {
        // Act
        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => EmailMarkSelection.Create(
            (EmailEnrichmentAspect)7,
            dueOnOrAfter: null,
            dueBefore: null));

        // Assert
        Assert.Equal("aspect", failure.ParamName);
    }

    /// <summary>Two requests naming one instant are one walk, whichever offset each of them wrote it at.</summary>
    [Fact]
    public void CanonicalText_DueBoundsAtDifferentOffsets_NameTheSameWalk()
    {
        // Act
        var inUtc = EmailMarkSelection.Create(
            EmailEnrichmentAspect.Commitment,
            MondayMorning,
            MondayMorning.AddDays(7));

        var written = EmailMarkSelection.Create(
            EmailEnrichmentAspect.Commitment,
            MondayMorning.ToOffset(TimeSpan.FromHours(2)),
            MondayMorning.AddDays(7).ToOffset(TimeSpan.FromHours(-5)));

        // Assert
        Assert.Equal(inUtc?.CanonicalText, written?.CanonicalText);
    }

    /// <summary>The bounds and the reading are each written, so no two of the standing views share a cursor.</summary>
    [Fact]
    public void CanonicalText_SelectionsDifferingInOneCriterion_AreNotOneWalk()
    {
        // Act
        var commitments = EmailMarkSelection.Create(EmailEnrichmentAspect.Commitment, null, null);
        var significance = EmailMarkSelection.Create(EmailEnrichmentAspect.Significance, null, null);
        var dueThisWeek = EmailMarkSelection.Create(
            EmailEnrichmentAspect.Commitment,
            MondayMorning,
            MondayMorning.AddDays(7));

        // Assert
        Assert.Equal(
            3,
            new[] { commitments?.CanonicalText, significance?.CanonicalText, dueThisWeek?.CanonicalText }
                .Distinct()
                .Count());
    }
}
