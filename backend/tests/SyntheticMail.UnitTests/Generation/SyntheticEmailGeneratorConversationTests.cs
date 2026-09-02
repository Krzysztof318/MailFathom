// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.Generation.AiContent;
using MailFathom.SyntheticMail.UnitTests.TestDoubles;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation;

/// <summary>What the seed decides about an exchange: how many threads, how long, who writes each turn, and in what order.</summary>
/// <remarks>
/// The identifiers a delivered exchange threads on come from a server, so nothing here asserts them. What is the
/// seed's — the shape of every thread and the content of every message in it — is asserted exactly as the flat
/// corpus's is.
/// </remarks>
public sealed class SyntheticEmailGeneratorConversationTests
{
    private static readonly DateTimeOffset LatestSentAt = new(2026, 8, 8, 23, 59, 59, TimeSpan.Zero);

    private static readonly SyntheticParticipant Mailbox = new("Developer", "developer@example.com");

    private static readonly AiEmailContent Answer = new(
        "Quarterly figures",
        "Hello,\n\nThe figures are attached.\n\nRegards",
        "<html><body><h1>Quarterly figures</h1><p>The figures are attached.</p></body></html>");

    [Fact]
    public void GenerateConversations_ASeededPlan_ProducesExactlyTheMessagesItAskedFor()
    {
        // Arrange
        var plan = Plan(seed: 42, count: 40);

        // Act
        var conversations = SyntheticEmailGenerator.GenerateConversations(plan, Mailbox);

        // Assert
        Assert.Equal(40, conversations.Sum(conversation => conversation.Messages.Count));
    }

    [Fact]
    public void GenerateConversations_ASeededPlan_MakesEveryThreadAnExchangeRatherThanAMessage()
    {
        // Arrange
        var plan = Plan(seed: 7, count: 61);

        // Act
        var conversations = SyntheticEmailGenerator.GenerateConversations(plan, Mailbox);

        // Assert
        // An odd count is the case a naive split gets wrong: it leaves one turn over, and a thread of one message is
        // exactly what a flat corpus already produced.
        Assert.All(conversations, conversation => Assert.True(conversation.Messages.Count >= 2));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(61)]
    [InlineData(100)]
    public void GenerateConversations_AnyCount_KeepsEveryThreadInsideTheStatedTurnRange(int count)
    {
        // Arrange
        // Seven is the count that catches the ceiling: a draw of six leaves exactly one over, and absorbing that
        // leftover rather than giving a turn back would produce a seven-turn thread the documentation says cannot
        // exist. The other counts reach the same correction from batches that keep drawing afterwards.
        var plans = Enumerable.Range(0, 40).Select(seed => Plan(seed, count));

        // Act
        var conversations = plans.SelectMany(plan => SyntheticEmailGenerator.GenerateConversations(plan, Mailbox));

        // Assert
        Assert.All(conversations, conversation => Assert.InRange(conversation.Messages.Count, 2, 6));
    }

    [Fact]
    public void GenerateConversations_ASeededPlan_AlternatesTheTwoSidesFromTheCorrespondentOnwards()
    {
        // Arrange
        var plan = Plan(seed: 11, count: 30);

        // Act
        var conversations = SyntheticEmailGenerator.GenerateConversations(plan, Mailbox);

        // Assert
        var authors = conversations
            .Select(conversation => conversation.Messages.Select(message => message.Author.Address).ToArray())
            .ToArray();

        var expected = conversations
            .Select(conversation => Enumerable
                .Range(0, conversation.Messages.Count)
                .Select(turn => turn % 2 == 0 ? conversation.Correspondent.Address : Mailbox.Address)
                .ToArray())
            .ToArray();

        Assert.Equal(expected, authors);
    }

    [Fact]
    public void GenerateConversations_ASeededPlan_KeepsAThreadToItsTwoParticipants()
    {
        // Arrange
        var plan = Plan(seed: 3, count: 24);

        // Act
        var conversations = SyntheticEmailGenerator.GenerateConversations(plan, Mailbox);

        // Assert
        // A third address on a two-party exchange is one the envelope never carries, so it would read in a client as
        // somebody who was copied and never written to.
        Assert.All(conversations, conversation =>
            Assert.All(conversation.Messages, message => Assert.Empty(message.CarbonCopies)));
    }

    [Fact]
    public void GenerateConversations_ASeededPlan_DatesEveryReplyAfterWhatItAnswers()
    {
        // Arrange
        var plan = Plan(seed: 19, count: 36);

        // Act
        var conversations = SyntheticEmailGenerator.GenerateConversations(plan, Mailbox);

        // Assert
        var outOfOrder = conversations
            .SelectMany(conversation => conversation.Messages
                .Zip(conversation.Messages.Skip(1), (earlier, later) => (earlier, later)))
            .Where(pair => pair.later.SentAt <= pair.earlier.SentAt)
            .ToArray();

        Assert.Empty(outOfOrder);
    }

    [Fact]
    public void GenerateConversations_ASeededPlan_GivesEveryReplyTheSubjectOfTheThreadItIsIn()
    {
        // Arrange
        var plan = Plan(seed: 23, count: 30);

        // Act
        var conversations = SyntheticEmailGenerator.GenerateConversations(plan, Mailbox);

        // Assert
        Assert.All(conversations, conversation =>
            Assert.All(conversation.Messages.Skip(1), message =>
                Assert.Equal($"Re: {conversation.Messages[0].Subject}", message.Subject)));
    }

    [Fact]
    public void GenerateConversations_TwoRunsOfOneSeed_ProduceTheSameExchanges()
    {
        // Arrange
        var plan = Plan(seed: 512, count: 45);

        // Act
        var first = SyntheticEmailGenerator.GenerateConversations(plan, Mailbox);
        var second = SyntheticEmailGenerator.GenerateConversations(plan, Mailbox);

        // Assert
        Assert.Equal(
            first.Select(conversation => CorpusFingerprint.Of(conversation.Messages)),
            second.Select(conversation => CorpusFingerprint.Of(conversation.Messages)));
    }

    [Fact]
    public void GenerateConversations_APlanTooSmallToHoldOne_IsRefused()
    {
        // Arrange
        var plan = Plan(seed: 1, count: 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => SyntheticEmailGenerator.GenerateConversations(plan, Mailbox));
    }

    [Fact]
    public void GenerateConversations_APlanNamingLanguages_IsRefusedAsOneOnlyASourceCanWrite()
    {
        // Arrange
        var plan = Plan(seed: 1, count: 10) with
        {
            Languages = ["en"],
            Topics = [SyntheticMailTopic.Business],
        };

        // Act, Assert
        Assert.Throws<ArgumentException>(() => SyntheticEmailGenerator.GenerateConversations(plan, Mailbox));
    }

    [Fact]
    public async Task GenerateConversationsAsync_APlanNamingNoLanguages_ProducesWhatTheSeededOneDoes()
    {
        // Arrange
        var plan = Plan(seed: 77, count: 20);
        var source = new ScriptedAiEmailContentSource(Answer);

        // Act
        var conversations = await SyntheticEmailGenerator.GenerateConversationsAsync(
            plan,
            Mailbox,
            source,
            1,
            TestContext.Current.CancellationToken);

        // Assert
        // A plan naming no language is a plan the vocabulary writes, whichever entry point it was handed to, so the
        // source is never asked anything.
        Assert.Empty(source.Requests);
        Assert.Equal(
            SyntheticEmailGenerator.GenerateConversations(plan, Mailbox)
                .Select(conversation => CorpusFingerprint.Of(conversation.Messages)),
            conversations.Select(conversation => CorpusFingerprint.Of(conversation.Messages)));
    }

    [Fact]
    public async Task GenerateConversationsAsync_APlanNamingLanguages_AsksTheSourceWhatEachReplyIsAnswering()
    {
        // Arrange
        var plan = Plan(seed: 5, count: 12) with
        {
            Languages = ["en"],
            Topics = [SyntheticMailTopic.Business],
        };

        var source = new ScriptedAiEmailContentSource(Answer);

        // Act
        var conversations = await SyntheticEmailGenerator.GenerateConversationsAsync(
            plan,
            Mailbox,
            source,
            1,
            TestContext.Current.CancellationToken);

        // Assert
        // A reply written against the subject alone reads as a second message about the same topic, so every request
        // after a thread's first turn carries what it is answering and the first turn carries nothing.
        var openings = conversations
            .SelectMany(conversation => Enumerable
                .Range(0, conversation.Messages.Count)
                .Select(turn => turn == 0))
            .ToArray();

        Assert.Equal(openings, source.Requests.Select(request => request.ParentOpening is null).ToArray());
    }

    private static SyntheticCorpusPlan Plan(int seed, int count) => new(
        seed,
        count,
        LatestSentAt,
        SpanDays: 90,
        MaximumAttachmentBytes: 4096,
        SensitivePercentage: 20,
        Languages: [],
        Topics: []);
}
