// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Infrastructure.Persistence.Enrichment;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Enrichment;

/// <summary>Covers what a stored mark is read back as, and what a row this build cannot read costs the page around it.</summary>
/// <remarks>
/// A row is read back long after it was written and the value objects judge it again on the way out, so the question
/// this class settles is what happens to the *other* rows when one fails that judgement. Reading them is a projection
/// over a whole page of messages, so a row raising rather than dropping fails every row the page was going to draw —
/// which reaches a reader as a mail list that will not load, with nothing saying which message did it.
/// </remarks>
public sealed class StoredEmailEnrichmentReaderTests
{
    private static readonly DateTimeOffset DueAt = new(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToMark_AStoredRowThisBuildStillAgreesWith_ReadsItBack()
    {
        // Act
        var mark = StoredEmailEnrichmentReader.ToMark(Row());

        // Assert
        Assert.NotNull(mark);
        Assert.Equal(EmailEnrichmentAspect.Sense, mark.Aspect);
        Assert.Equal("a racking quotation", mark.Text);
        Assert.Equal(EmailEnrichmentSource.Model, mark.Provenance.Source);
        Assert.Null(mark.DueAt);
    }

    /// <summary>Only a commitment carries a date, so a date stored beside any other aspect is left behind rather than refused.</summary>
    [Fact]
    public void ToMark_ADateStoredBesideAnAspectThatCarriesNone_DropsTheDateAndKeepsTheMark()
    {
        // Act
        var mark = StoredEmailEnrichmentReader.ToMark(Row() with { DueAt = DueAt });

        // Assert
        Assert.NotNull(mark);
        Assert.Null(mark.DueAt);
    }

    [Fact]
    public void ToMark_ACommitmentWithItsDate_KeepsBoth()
    {
        // Act
        var mark = StoredEmailEnrichmentReader.ToMark(
            Row() with { Aspect = EmailEnrichmentAspect.Commitment, DueAt = DueAt });

        // Assert
        Assert.NotNull(mark);
        Assert.Equal(DueAt, mark.DueAt);
    }

    /// <summary>A re-cut removed the passages the reading rested on, which is the ordinary way a row stops being a mark.</summary>
    [Fact]
    public void ToMark_ARowWhoseEvidenceIsGone_DropsIt()
    {
        // Act & Assert
        Assert.Null(StoredEmailEnrichmentReader.ToMark(Row() with { Evidence = [] }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToMark_ARowWithNothingLeftToSay_DropsIt(string text)
    {
        // Act & Assert
        Assert.Null(StoredEmailEnrichmentReader.ToMark(Row() with { Text = text }));
        Assert.Null(StoredEmailEnrichmentReader.ToMark(Row() with { Reason = text }));
    }

    /// <summary>
    /// A value this build's enum does not declare is a row written by something it does not agree with — a later
    /// version, or a hand edit. It takes its own row out of the page and nothing else with it.
    /// </summary>
    [Fact]
    public void ToMark_AnAspectNamingNoMember_DropsTheRowRatherThanFailingThePage()
    {
        // Act & Assert
        Assert.Null(StoredEmailEnrichmentReader.ToMark(Row() with { Aspect = (EmailEnrichmentAspect)97 }));
    }

    [Fact]
    public void ToMark_ASourceNamingNoMember_DropsTheRowRatherThanFailingThePage()
    {
        // Act & Assert
        Assert.Null(StoredEmailEnrichmentReader.ToMark(Row() with { Source = (EmailEnrichmentSource)61 }));
    }

    /// <summary>The other half of the provenance, which the value object refuses on rules of its own.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invoice\u0007due-date")]
    public void ToMark_AnOriginNoLongerWrittenTheWayOneIs_DropsTheRowRatherThanFailingThePage(string origin)
    {
        // Act & Assert
        Assert.Null(StoredEmailEnrichmentReader.ToMark(Row() with { Origin = origin }));
    }

    private static StoredEmailEnrichmentReader.MarkRow Row() => new(
        EmailEnrichmentAspect.Sense,
        "a racking quotation",
        "the passage attaches one",
        DueAt: null,
        EmailEnrichmentSource.Model,
        "mailfathom-email-enrichment",
        [Guid.CreateVersion7()]);
}
