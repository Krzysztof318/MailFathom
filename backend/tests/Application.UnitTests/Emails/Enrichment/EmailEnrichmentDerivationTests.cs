// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Enrichment;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Enrichment;

/// <summary>Covers the two outcomes of one derivation, which a caller treats differently on purpose.</summary>
/// <remarks>
/// A settled answer of nothing and a withheld one both carry no marks, and everything downstream turns on telling them
/// apart: the first takes the message out of the queue permanently and the second leaves it for the next run.
/// </remarks>
public sealed class EmailEnrichmentDerivationTests
{
    [Fact]
    public void Settled_MarksOfDifferentAspects_IsAnAnswerToWriteDown()
    {
        // Arrange
        var sense = Mark(EmailEnrichmentAspect.Sense);
        var commitment = Mark(EmailEnrichmentAspect.Commitment);

        // Act
        var derivation = EmailEnrichmentDerivation.Settled([sense, commitment]);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Null(derivation.Withheld);
        Assert.Equal([sense, commitment], derivation.Marks);
    }

    /// <summary>Nothing to say is still an answer, which is why it is settled rather than withheld.</summary>
    [Fact]
    public void Settled_NoMarks_IsStillAnAnswerToWriteDown()
    {
        // Act
        var derivation = EmailEnrichmentDerivation.Settled([]);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Empty(derivation.Marks);
    }

    /// <summary>A message carries one reading of each kind, so a row of each aspect has exactly one candidate.</summary>
    [Fact]
    public void Settled_TwoMarksOfOneAspect_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => EmailEnrichmentDerivation.Settled(
            [Mark(EmailEnrichmentAspect.Sense), Mark(EmailEnrichmentAspect.Sense)]));

        // Assert
        Assert.Equal("marks", refusal.ParamName);
    }

    [Theory]
    [InlineData(EmailEnrichmentWithholding.NotActivated)]
    [InlineData(EmailEnrichmentWithholding.AllowanceExhausted)]
    [InlineData(EmailEnrichmentWithholding.ProviderUnavailable)]
    [InlineData(EmailEnrichmentWithholding.AnswerUnreadable)]
    public void Withholding_AConditionThatOutlivesOneMessage_IsNotAnAnswerToWriteDown(
        EmailEnrichmentWithholding withholding)
    {
        // Act
        var derivation = EmailEnrichmentDerivation.Withholding(withholding);

        // Assert
        Assert.False(derivation.IsSettled);
        Assert.Equal(withholding, derivation.Withheld);
        Assert.Empty(derivation.Marks);
    }

    [Fact]
    public void Withholding_AConditionThisSystemDoesNotStopOn_IsRefused()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmailEnrichmentDerivation.Withholding((EmailEnrichmentWithholding)61));
    }

    /// <summary>A derivation carries what the message asked for beside what it says about it.</summary>
    [Fact]
    public void Settled_ProposalsBesideTheMarks_CarriesBoth()
    {
        // Arrange
        var proposal = EmailTaskProposal.Create("Answer the supplier", new DateOnly(2026, 9, 21));

        // Act
        var derivation = EmailEnrichmentDerivation.Settled([Mark(EmailEnrichmentAspect.Sense)], [proposal]);

        // Assert
        Assert.True(derivation.IsSettled);
        Assert.Equal([proposal], derivation.Tasks);
    }

    /// <summary>A derivation that read nothing to do carries an empty list rather than nothing at all.</summary>
    [Fact]
    public void Settled_NoProposalsStated_CarriesNone()
    {
        // Act
        var derivation = EmailEnrichmentDerivation.Settled([]);

        // Assert
        Assert.Empty(derivation.Tasks);
    }

    /// <summary>The bound is the record's rather than the reading's, so nothing can offer one message's worth of work.</summary>
    [Fact]
    public void Settled_MoreProposalsThanOneMessageProduces_IsRefused()
    {
        // Arrange
        IReadOnlyList<EmailTaskProposal> proposals =
        [
            .. Enumerable
                .Range(0, EmailTaskProposal.MaximumPerEmail + 1)
                .Select(static _ => EmailTaskProposal.Create("Answer the supplier", dueOn: null)),
        ];

        // Act & Assert
        Assert.Throws<ArgumentException>(() => EmailEnrichmentDerivation.Settled([], proposals));
    }

    /// <summary>A withheld derivation carries nothing of either kind, because nothing was derived.</summary>
    [Fact]
    public void Withholding_AnyCondition_CarriesNoProposals()
    {
        // Act
        var derivation = EmailEnrichmentDerivation.Withholding(EmailEnrichmentWithholding.ProviderUnavailable);

        // Assert
        Assert.Empty(derivation.Tasks);
    }

    private static EmailEnrichmentMark Mark(EmailEnrichmentAspect aspect) =>
        EmailEnrichmentMark.Create(
            aspect,
            "a racking quotation",
            "the passage attaches one",
            [EmailChunkId.Create(Guid.CreateVersion7())],
            EmailEnrichmentProvenance.FromAgent("mailfathom-email-enrichment"));
}
