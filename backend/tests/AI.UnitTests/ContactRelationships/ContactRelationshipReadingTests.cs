// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ContactRelationships;
using MailFathom.Application.Contacts.Correspondence;
using MailFathom.Application.Contacts.Relationship;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.AI.UnitTests.ContactRelationships;

/// <summary>Covers what survives an answer about a correspondence: which citations resolve, which statements fall away, and what makes a card no card.</summary>
public sealed class ContactRelationshipReadingTests
{
    private static readonly DateTimeOffset FirstJuly = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

    private static readonly StoredEmailId FirstMessage = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId DocumentMessage = StoredEmailId.Create(Guid.CreateVersion7());

    /// <summary>An answer of the shape the instruction asks for becomes the card a contact is headed by.</summary>
    [Fact]
    public void Read_AnAnswerUnderEveryHeading_ProducesTheNoteTheNextActionAndEveryObservation()
    {
        // Arrange
        const string answer = """
            {
              "note": { "text": "They lead the addendum renegotiation.", "sources": [0] },
              "nextAction": { "text": "Answer the indexation cap.", "sources": [0] },
              "activePeriod": { "text": "mornings", "sources": [0] },
              "openItem": { "text": "the cap decision", "sources": [0] },
              "cases": { "text": "the 2027 addendum", "sources": [0] }
            }
            """;

        // Act
        var card = ContactRelationshipReading.Read(answer, Correspondence());

        // Assert
        Assert.Equal("They lead the addendum renegotiation.", card.Note?.Text);
        Assert.Equal("Answer the indexation cap.", card.NextAction?.Text);
        Assert.Equal(
            [
                ContactRelationshipAspect.ActivePeriod,
                ContactRelationshipAspect.OpenItem,
                ContactRelationshipAspect.Case,
            ],
            card.Observations.Select(observation => observation.Aspect));
    }

    /// <summary>A conversation is cited as the message it was last carried by, so a reader follows it through the citation every other source resolves through.</summary>
    [Fact]
    public void Read_AStatementCitingAConversation_ResolvesItToThatConversationsOwnMessage()
    {
        // Act
        var card = ContactRelationshipReading.Read(NoteCiting(0), Correspondence());

        // Assert
        var source = Assert.Single(card.Note!.Sources);

        Assert.Equal(FirstMessage, source.StoredEmailId);
        Assert.Null(source.AttachmentPosition);
    }

    /// <summary>The documents are numbered on from the conversations, so a number past the last conversation resolves to the file it names.</summary>
    [Fact]
    public void Read_AStatementCitingADocument_ResolvesItToTheMessageAndThePositionTheFileSitsAt()
    {
        // Act
        var card = ContactRelationshipReading.Read(NoteCiting(2), Correspondence());

        // Assert
        var source = Assert.Single(card.Note!.Sources);

        Assert.Equal(DocumentMessage, source.StoredEmailId);
        Assert.Equal(3, source.AttachmentPosition);
    }

    /// <summary>A number the turn never published names nothing, and a statement left with no source is one nobody could check.</summary>
    [Fact]
    public void Read_AStatementCitingANumberTheTurnNeverPublished_DropsTheStatement()
    {
        // Act
        var card = ContactRelationshipReading.Read(NoteCiting(9), Correspondence());

        // Assert
        Assert.False(card.WasDerived);
    }

    /// <summary>The note is what the rest of the card is drawn around, so an answer without one is no card however much survived beside it.</summary>
    [Fact]
    public void Read_AnAnswerWithNoNote_ProducesNoCardAtAll()
    {
        // Arrange
        const string answer = """
            { "openItem": { "text": "the cap decision", "sources": [0] } }
            """;

        // Act
        var card = ContactRelationshipReading.Read(answer, Correspondence());

        // Assert
        Assert.False(card.WasDerived);
        Assert.Empty(card.Observations);
    }

    /// <summary>An observation nothing backs falls away on its own, leaving the card it was written beside.</summary>
    [Fact]
    public void Read_AnObservationWithNoSource_DropsItAndKeepsTheCard()
    {
        // Arrange
        const string answer = """
            {
              "note": { "text": "They lead the addendum renegotiation.", "sources": [0] },
              "openItem": { "text": "the cap decision", "sources": [] }
            }
            """;

        // Act
        var card = ContactRelationshipReading.Read(answer, Correspondence());

        // Assert
        Assert.True(card.WasDerived);
        Assert.Empty(card.Observations);
    }

    /// <summary>A model that fences its answer or writes a sentence around it has still answered, and throwing that away would cost a usable card.</summary>
    [Fact]
    public void Read_AnAnswerFencedAndSurroundedByProse_ReadsTheObjectInsideIt()
    {
        // Arrange
        var answer = $"Here is the card you asked for:\n```json\n{NoteCiting(0)}\n```\nLet me know if that helps.";

        // Act
        var card = ContactRelationshipReading.Read(answer, Correspondence());

        // Assert
        Assert.True(card.WasDerived);
    }

    /// <summary>A provider that answered nothing, or nothing this build can read, leaves the contact drawn without a card.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("I could not work that out.")]
    [InlineData("{ \"note\": ")]
    public void Read_AnAnswerThisBuildCannotRead_ProducesNoCard(string? answer)
    {
        // Act
        var card = ContactRelationshipReading.Read(answer, Correspondence());

        // Assert
        Assert.False(card.WasDerived);
    }

    /// <summary>Two conversations and one document, which is the numbering the turn publishes: conversations from zero, documents on from the end of them.</summary>
    private static ContactCorrespondence Correspondence() => new(
        [
            new CorrespondingThread(EmailThreadId.Create(Guid.CreateVersion7()), FirstMessage, "the addendum", FirstJuly),
            new CorrespondingThread(
                EmailThreadId.Create(Guid.CreateVersion7()),
                StoredEmailId.Create(Guid.CreateVersion7()),
                "the schedule",
                FirstJuly),
        ],
        [new CorrespondingDocument(DocumentMessage, 3, "addendum.pdf", "application/pdf", FirstJuly)]);

    private static string NoteCiting(int position) =>
        $$"""{ "note": { "text": "They lead the addendum renegotiation.", "sources": [{{position}}] } }""";
}
