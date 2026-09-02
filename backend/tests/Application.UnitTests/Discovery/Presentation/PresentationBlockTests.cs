// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Presentation;
using MailFathom.Application.Discovery.Presentation.Blocks;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Discovery.Presentation;

/// <summary>Covers the invariants each catalogued block holds about the data it presents.</summary>
public sealed class PresentationBlockTests
{
    /// <summary>A table whose rows disagree with its header is a comparison nobody can read and nobody can draw.</summary>
    [Fact]
    public void FactTableBlock_ARowWithTheWrongNumberOfCells_IsRefused()
    {
        // Arrange
        var row = new FactTableRow([new FactTableCell(PresentationPlanExample.Text("Northwind"), [])]);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new FactTableBlock(
            PresentationPlanExample.Supported(),
            [FactTableColumn.Party, FactTableColumn.Amount],
            [row]));
    }

    [Fact]
    public void FactTableBlock_TheSameColumnTwice_IsRefused()
    {
        // Arrange
        var row = new FactTableRow([
            new FactTableCell(PresentationPlanExample.Text("Northwind"), []),
            new FactTableCell(PresentationPlanExample.Text("Contoso"), []),
        ]);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new FactTableBlock(
            PresentationPlanExample.Supported(),
            [FactTableColumn.Party, FactTableColumn.Party],
            [row]));
    }

    /// <summary>Presenting a cell nobody wrote about like one somebody left blank asserts something the mail did not.</summary>
    [Fact]
    public void FactTableCell_NoValueButASource_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new FactTableCell(
            value: null,
            [PresentationPlanExample.FirstCitation]));
    }

    /// <summary>A thread state saying nothing about agreements, questions, or commitments is a heading over nothing.</summary>
    [Fact]
    public void ThreadStateBlock_NoAgreementQuestionOrCommitment_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new ThreadStateBlock(
            PresentationPlanExample.Supported(),
            [new ThreadParticipant(PresentationPlanExample.Text("Ada Bell"), address: null)],
            [],
            [],
            []));
    }

    /// <summary>Nothing here can recall a message that has left the deployment.</summary>
    [Fact]
    public void SuggestedActionBlock_SendingMailWithoutConfirmation_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new SuggestedActionBlock(
            PresentationPlanExample.Supported(),
            SuggestedActionKind.ReplyToThread,
            PresentationPlanExample.Text("They are waiting on an answer."),
            SuggestedActionImpact.SendsMail,
            requiresConfirmation: false));
    }

    [Fact]
    public void SuggestedActionBlock_AnActionThatChangesNothing_MayBeOfferedWithoutConfirmation()
    {
        // Act
        var block = new SuggestedActionBlock(
            PresentationPlanExample.Supported(),
            SuggestedActionKind.OpenThread,
            PresentationPlanExample.Text("The answer only quoted part of it."),
            SuggestedActionImpact.ReadsOnly,
            requiresConfirmation: false);

        // Assert
        Assert.False(block.RequiresConfirmation);
    }

    [Fact]
    public void DraftBlock_TheSameRecipientTwice_IsRefused()
    {
        // Arrange
        EmailAddress.TryCreate(displayName: null, "ada@northwind.example", out var recipient);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new DraftBlock(
            PresentationPlanExample.Supported(),
            [recipient, recipient],
            PresentationPlanExample.Text("Re: Revised figures"),
            PresentationPlanExample.Text("Confirming."),
            DraftDisposition.Composed));
    }

    [Fact]
    public void EvidenceEntry_ARelevanceOutsideItsRange_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new EvidenceEntry(
            PresentationPlanExample.FirstCitation,
            PresentationPlanExample.Text("we accept"),
            1.5d,
            PresentationFreshness.Unknown));
    }

    /// <summary>A block that presents an empty list draws a heading and nothing under it.</summary>
    [Fact]
    public void TimelineBlock_NoEntries_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new TimelineBlock(PresentationPlanExample.Supported(), []));
    }

    /// <summary>An entry that presents a source names it, or it presents nothing a reader can follow.</summary>
    [Fact]
    public void AttachmentEntry_AnUnspecifiedSource_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AttachmentEntry(
            source: default,
            PresentationPlanExample.Text("renewal.pdf"),
            mediaType: null,
            sizeOctets: 1L,
            AttachmentAvailability.Stored));
    }

    [Fact]
    public void EvidenceEntry_AnUnspecifiedSource_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new EvidenceEntry(
            source: default,
            PresentationPlanExample.Text("we accept"),
            0.5d,
            PresentationFreshness.Unknown));
    }

    [Fact]
    public void FactTableBlock_AColumnThatIsTheStructDefault_IsRefused()
    {
        // Arrange
        var row = new FactTableRow([new FactTableCell(PresentationPlanExample.Text("Northwind"), [])]);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => new FactTableBlock(
            PresentationPlanExample.Supported(),
            [default],
            [row]));
    }

    /// <summary>A text that is the reachable struct default is not text, and every block refuses it the same way.</summary>
    [Fact]
    public void AnswerBlock_ATextThatIsTheStructDefault_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new AnswerBlock(
            PresentationPlanExample.Supported(),
            text: default,
            PresentationConfidence.High));
    }

    /// <summary>An open question cites like anything else a thread state presents.</summary>
    [Fact]
    public void ReferencedCitations_AThreadStateWithAnOpenQuestion_IncludesWhatTheQuestionNames()
    {
        // Arrange
        var block = new ThreadStateBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            [new ThreadParticipant(PresentationPlanExample.Text("Ada Bell"), address: null)],
            [],
            [new ThreadStatement(PresentationPlanExample.Text("Is the schedule agreed?"), [PresentationPlanExample.SecondCitation])],
            []);

        // Act, Assert
        Assert.Equal([PresentationPlanExample.SecondCitation], block.ReferencedCitations);
    }

    [Fact]
    public void PeopleBlock_NoEntryList_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new PeopleBlock(PresentationPlanExample.Supported(), entries: null!));
    }

    /// <summary>A hole in a list is a block that draws a row of nothing, whatever the producer meant by it.</summary>
    [Fact]
    public void PeopleBlock_AnEntryThatIsNothing_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new PeopleBlock(PresentationPlanExample.Supported(), [null!]));
    }

    [Fact]
    public void TimelineEntry_AnUnspecifiedSource_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new TimelineEntry(
            PresentationPlanExample.ObservedAt,
            PresentationPlanExample.Text("Figure revised"),
            PresentationPlanExample.Text("Renewal"),
            [default]));
    }

    [Fact]
    public void TimelineEntry_TheSameSourceTwice_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => new TimelineEntry(
            PresentationPlanExample.ObservedAt,
            PresentationPlanExample.Text("Figure revised"),
            PresentationPlanExample.Text("Renewal"),
            [PresentationPlanExample.FirstCitation, PresentationPlanExample.FirstCitation]));
    }

    /// <summary>An entry may rest on nothing, which is how a row nobody wrote about sits beside rows that are backed.</summary>
    [Fact]
    public void TimelineEntry_NoSource_IsKept()
    {
        // Act
        var entry = new TimelineEntry(
            PresentationPlanExample.ObservedAt,
            PresentationPlanExample.Text("Figure revised"),
            PresentationPlanExample.Text("Renewal"),
            []);

        // Assert
        Assert.Empty(entry.Sources);
    }

    /// <summary>What a block cites is what its own entries cite as well, which is what the plan checks references against.</summary>
    [Fact]
    public void ReferencedCitations_ABlockWhoseEntriesCite_IncludesWhatTheEntriesName()
    {
        // Arrange
        var block = new TimelineBlock(
            PresentationEvidence.Unsupported(PresentationFreshness.Unknown),
            [
                new TimelineEntry(
                    PresentationPlanExample.ObservedAt,
                    PresentationPlanExample.Text("Figure revised"),
                    PresentationPlanExample.Text("Renewal"),
                    [PresentationPlanExample.SecondCitation]),
            ]);

        // Act, Assert
        Assert.Equal([PresentationPlanExample.SecondCitation], block.ReferencedCitations);
    }
}
