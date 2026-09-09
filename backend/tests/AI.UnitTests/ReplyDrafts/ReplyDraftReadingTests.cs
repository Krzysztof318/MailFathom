// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ReplyDrafts;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.AI.UnitTests.ReplyDrafts;

/// <summary>Covers what survives the reading of a model's answer to a drafting.</summary>
/// <remarks>
/// Everything here is about an answer this system did not write. What is asserted is that a usable reply is read, that
/// an unusable one produces nothing rather than an exception, that a claim the correspondence does not back survives
/// and is marked rather than disappearing into the body unannounced, and above all that nothing leaves the reading
/// citing a message or addressing a person the turn did not publish.
/// </remarks>
public sealed class ReplyDraftReadingTests
{
    private static readonly StoredEmailId First = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly StoredEmailId Second = StoredEmailId.Create(Guid.CreateVersion7());

    [Fact]
    public void Read_AnAnswerCarryingAReplyItsClaimsAndItsRecipients_ReadsAllThree()
    {
        // Arrange
        const string answer = """
            {
              "body": "Tuesday at ten works for us, at the price we agreed.",
              "claims": [
                { "text": "The price agreed is 4 200 zloty.", "messages": [1] },
                { "text": "Tuesday at ten is free.", "messages": [] }
              ],
              "recipients": [0]
            }
            """;

        // Act
        var draft = ReplyDraftReading.Read(answer, Messages(), Participants());

        // Assert
        Assert.True(draft.WasWritten);
        Assert.Equal("Tuesday at ten works for us, at the price we agreed.", draft.Body);
        Assert.Equal([Second], draft.Claims[0].Sources);
        Assert.True(draft.Claims[0].IsSupported);
        Assert.False(draft.Claims[1].IsSupported);
        Assert.Equal(["anna@example.test"], draft.ProposedRecipients.Select(static person => person.Address));
    }

    /// <summary>The half a fluent model omits: an assertion the mail does not carry is what a sender has to see.</summary>
    [Theory]
    [InlineData("""{ "body": "We accept.", "claims": [{ "text": "The deadline is the fifth." }] }""")]
    [InlineData("""{ "body": "We accept.", "claims": [{ "text": "The deadline is the fifth.", "messages": [] }] }""")]
    [InlineData("""{ "body": "We accept.", "claims": [{ "text": "The deadline is the fifth.", "messages": [9] }] }""")]
    public void Read_AClaimNoPublishedMessageBacks_KeepsItAndMarksItUnsupported(string answer)
    {
        // Act
        var claim = Assert.Single(ReplyDraftReading.Read(answer, Messages(), Participants()).Claims);

        // Assert
        Assert.Equal("The deadline is the fifth.", claim.Text);
        Assert.False(claim.IsSupported);
        Assert.Empty(claim.Sources);
    }

    /// <summary>A citation partly outside the turn keeps the part inside it rather than losing the claim's backing.</summary>
    [Fact]
    public void Read_AClaimCitingOnePublishedMessageAndOneNot_KeepsThePublishedOne()
    {
        // Arrange
        const string answer =
            """{ "body": "We accept.", "claims": [{ "text": "They quoted 4 200.", "messages": [9, 1, 1] }] }""";

        // Act
        var claim = Assert.Single(ReplyDraftReading.Read(answer, Messages(), Participants()).Claims);

        // Assert
        Assert.Equal([Second], claim.Sources);
    }

    /// <summary>The whole point of numbering the people: a proposal is a position this deployment published.</summary>
    [Theory]
    [InlineData("[-1]")]
    [InlineData("[7]")]
    [InlineData("[]")]
    public void Read_ARecipientTheTurnNeverPublished_ProposesNobody(string recipients)
    {
        // Arrange
        var answer = $$"""{ "body": "We accept.", "recipients": {{recipients}} }""";

        // Act
        var draft = ReplyDraftReading.Read(answer, Messages(), Participants());

        // Assert
        Assert.True(draft.WasWritten);
        Assert.Empty(draft.ProposedRecipients);
    }

    [Fact]
    public void Read_ARecipientProposedTwice_ProposesThemOnce()
    {
        // Arrange
        const string answer = """{ "body": "We accept.", "recipients": [1, 1, 1] }""";

        // Act
        var draft = ReplyDraftReading.Read(answer, Messages(), Participants());

        // Assert
        Assert.Equal(["karolina@example.test"], draft.ProposedRecipients.Select(static person => person.Address));
    }

    /// <summary>A model annotating every sentence still leaves a list somebody reads before they send.</summary>
    [Fact]
    public void Read_MoreClaimsThanADraftCarries_KeepsTheLeadingOnes()
    {
        // Arrange
        var written = string.Join(
            ',',
            Enumerable
                .Range(0, ReplyDraft.MaximumClaims + 4)
                .Select(static ordinal => $$"""{ "text": "Claim {{ordinal}}.", "messages": [0] }"""));
        var answer = $$"""{ "body": "We accept.", "claims": [{{written}}] }""";

        // Act
        var draft = ReplyDraftReading.Read(answer, Messages(), Participants());

        // Assert
        Assert.Equal(ReplyDraft.MaximumClaims, draft.Claims.Count);
        Assert.Equal("Claim 0.", draft.Claims[0].Text);
    }

    /// <summary>A model told to answer with one object still fences it and writes a sentence around it.</summary>
    [Theory]
    [InlineData("""Here is the reply: ```json { "body": "We accept." } ``` I hope this helps.""")]
    [InlineData("""```{ "body": "We accept." }```""")]
    public void Read_AnObjectWrittenInsideProseOrAFence_IsStillRead(string answer)
    {
        // Act
        var draft = ReplyDraftReading.Read(answer, Messages(), Participants());

        // Assert
        Assert.Equal("We accept.", draft.Body);
    }

    /// <summary>There is no honest half of a reply, so an answer without one leaves the composer as it was.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I could not draft a reply.")]
    [InlineData("{ not json at all }")]
    [InlineData("{}")]
    [InlineData("""{ "body": "   ", "claims": [{ "text": "They quoted 4 200.", "messages": [0] }] }""")]
    public void Read_AnAnswerCarryingNoReply_DraftsNothing(string? answer)
    {
        // Act
        var draft = ReplyDraftReading.Read(answer, Messages(), Participants());

        // Assert
        Assert.False(draft.WasWritten);
        Assert.Empty(draft.Body);
        Assert.Empty(draft.Claims);
        Assert.Empty(draft.ProposedRecipients);
    }

    [Theory]
    [InlineData("""{ "body": "We accept.", "claims": [{ "messages": [0] }] }""")]
    [InlineData("""{ "body": "We accept.", "claims": [{ "text": "   ", "messages": [0] }] }""")]
    [InlineData("""{ "body": "We accept.", "claims": [null] }""")]
    public void Read_AClaimSayingNothing_IsDropped(string answer)
    {
        // Act
        var draft = ReplyDraftReading.Read(answer, Messages(), Participants());

        // Assert
        Assert.True(draft.WasWritten);
        Assert.Empty(draft.Claims);
    }

    private static IReadOnlyList<ReplyDraftMessage> Messages() =>
    [
        new(First, 0, "Anna", new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero), "Could you quote for the batch?"),
        new(Second, 1, "Karolina", new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero), "It is 4 200 zloty."),
    ];

    private static IReadOnlyList<ReplyDraftParticipant> Participants()
    {
        EmailAddress.TryCreate("Anna", "anna@example.test", out var anna);
        EmailAddress.TryCreate("Karolina", "karolina@example.test", out var karolina);

        return [new ReplyDraftParticipant(0, anna), new ReplyDraftParticipant(1, karolina)];
    }
}
