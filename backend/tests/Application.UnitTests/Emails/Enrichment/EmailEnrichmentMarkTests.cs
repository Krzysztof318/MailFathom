// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Enrichment;

/// <summary>Covers what a mark refuses to be recorded as, and what it shortens instead of refusing.</summary>
/// <remarks>
/// The distinction between the two is the subject: a mark nothing backs would read on a screen exactly like one a
/// passage supports, so it is refused, while an over-long sentence is a producer being wordy and shortening it keeps
/// the row rather than discarding a derivation somebody paid for.
/// </remarks>
public sealed class EmailEnrichmentMarkTests
{
    private static readonly EmailChunkId Passage = EmailChunkId.Create(Guid.CreateVersion7());

    private static readonly EmailEnrichmentProvenance Agent =
        EmailEnrichmentProvenance.FromAgent("mailfathom-email-enrichment");

    [Fact]
    public void Create_AReadingWithItsReasonAndEvidence_KeepsAllThree()
    {
        // Act
        var mark = EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Significance,
            "the last quote outstanding",
            "the passage names the deadline",
            [Passage],
            Agent);

        // Assert
        Assert.Equal(EmailEnrichmentAspect.Significance, mark.Aspect);
        Assert.Equal("the last quote outstanding", mark.Text);
        Assert.Equal("the passage names the deadline", mark.Reason);
        Assert.Equal([Passage], mark.Evidence);
        Assert.Equal(Agent, mark.Provenance);
        Assert.Null(mark.DueAt);
    }

    /// <summary>The invariant the record exists for: a mark always says where it came from.</summary>
    [Fact]
    public void Create_NoEvidence_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Sense,
            "a racking quotation",
            "the message reads like one",
            [],
            Agent));

        // Assert
        Assert.Equal("evidence", refusal.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankReading_IsRefused(string text)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Sense,
            text,
            "the message reads like one",
            [Passage],
            Agent));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ABlankReason_IsRefused(string reason)
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Sense,
            "a racking quotation",
            reason,
            [Passage],
            Agent));
    }

    [Fact]
    public void Create_AnAspectThisSystemDoesNotDerive_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailEnrichmentMark.Create(
            (EmailEnrichmentAspect)97,
            "a racking quotation",
            "the message reads like one",
            [Passage],
            Agent));
    }

    /// <summary>A date on a reading nothing reads a date from would leave a reader deciding what it meant.</summary>
    [Theory]
    [InlineData(EmailEnrichmentAspect.Sense)]
    [InlineData(EmailEnrichmentAspect.Significance)]
    public void Create_ADateOnAnAspectOtherThanACommitment_IsRefused(EmailEnrichmentAspect aspect)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => EmailEnrichmentMark.Create(
            aspect,
            "a racking quotation",
            "the message reads like one",
            [Passage],
            Agent,
            new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero)));

        // Assert
        Assert.Equal("dueAt", refusal.ParamName);
    }

    [Fact]
    public void Create_ACommitmentWithADate_KeepsIt()
    {
        // Arrange
        var dueAt = new DateTimeOffset(2026, 9, 11, 0, 0, 0, TimeSpan.Zero);

        // Act
        var mark = EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Commitment,
            "answer the supplier",
            "the passage asks for an answer",
            [Passage],
            Agent,
            dueAt);

        // Assert
        Assert.Equal(dueAt, mark.DueAt);
    }

    [Fact]
    public void Create_AnOverLongReading_ShortensItRatherThanRefusingIt()
    {
        // Arrange
        var wordy = new string('q', EmailEnrichmentMark.MaximumTextLength + 40);

        // Act
        var mark = EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Sense,
            wordy,
            wordy,
            [Passage],
            Agent);

        // Assert
        Assert.Equal(EmailEnrichmentMark.MaximumTextLength, mark.Text.Length);
        Assert.Equal(EmailEnrichmentMark.MaximumTextLength, mark.Reason.Length);
    }

    /// <summary>A sentence a producer wrapped across lines is bounded by what it says, not by how it was laid out.</summary>
    [Fact]
    public void Create_AReadingLaidOutAcrossLines_CollapsesItToOneSentence()
    {
        // Act
        var mark = EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Sense,
            "  a racking\n\tquotation  ",
            "the passage\r\nattaches one",
            [Passage],
            Agent);

        // Assert
        Assert.Equal("a racking quotation", mark.Text);
        Assert.Equal("the passage attaches one", mark.Reason);
    }

    /// <summary>Evidence past a handful is a producer citing the message rather than where its claim came from.</summary>
    [Fact]
    public void Create_MoreEvidenceThanAMarkMayHold_KeepsTheLeadingCitations()
    {
        // Arrange
        IReadOnlyList<EmailChunkId> cited =
            [.. Enumerable.Range(0, EmailEnrichmentMark.MaximumEvidenceCount + 3)
                .Select(_ => EmailChunkId.Create(Guid.CreateVersion7()))];

        // Act
        var mark = EmailEnrichmentMark.Create(
            EmailEnrichmentAspect.Sense,
            "a long thread",
            "it runs on",
            cited,
            Agent);

        // Assert
        Assert.Equal([.. cited.Take(EmailEnrichmentMark.MaximumEvidenceCount)], mark.Evidence);
    }
}
