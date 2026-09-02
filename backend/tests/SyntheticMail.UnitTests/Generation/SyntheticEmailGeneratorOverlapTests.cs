// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.UnitTests.TestDoubles;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation;

/// <summary>What overlapping the provider calls is allowed to change, which is how long a run takes and nothing else.</summary>
/// <remarks>
/// AI content mode spends almost all of its time waiting for one provider at a time, so a corpus of two hundred is an
/// hour of a developer's day. What makes spreading those calls safe is that the seed decides the whole corpus before
/// any of them goes out; these are the assertions that say so, and the one that says a run does not open a connection
/// per message while it is at it.
/// </remarks>
public sealed class SyntheticEmailGeneratorOverlapTests
{
    private static readonly DateTimeOffset LatestSentAt = new(2026, 6, 1, 23, 59, 59, TimeSpan.Zero);

    private static readonly SyntheticParticipant WatchedMailbox = new("Watched Mailbox", "watched@example.test");

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public async Task GenerateAsync_AnyDegreeOfConcurrency_ProducesTheCorpusTheSeedDescribes(int concurrency)
    {
        // Arrange
        var sequential = await SyntheticEmailGenerator.GenerateAsync(
            Plan(count: 60),
            new OverlappingAiEmailContentSource(),
            1,
            TestContext.Current.CancellationToken);

        // Act
        var overlapped = await SyntheticEmailGenerator.GenerateAsync(
            Plan(count: 60),
            new OverlappingAiEmailContentSource(),
            concurrency,
            TestContext.Current.CancellationToken);

        // Assert
        // Field by field rather than message count: what a wrong draw order moves is a date, a participant, or an
        // attachment's bytes, and every one of those survives a count.
        Assert.Equal(
            sequential.Select(CorpusFingerprint.Of),
            overlapped.Select(CorpusFingerprint.Of));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task GenerateConversationsAsync_AnyDegreeOfConcurrency_ProducesTheExchangesTheSeedDescribes(int concurrency)
    {
        // Arrange
        var sequential = await SyntheticEmailGenerator.GenerateConversationsAsync(
            Plan(count: 40),
            WatchedMailbox,
            new OverlappingAiEmailContentSource(),
            1,
            TestContext.Current.CancellationToken);

        // Act
        var overlapped = await SyntheticEmailGenerator.GenerateConversationsAsync(
            Plan(count: 40),
            WatchedMailbox,
            new OverlappingAiEmailContentSource(),
            concurrency,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            sequential.Select(exchange => CorpusFingerprint.Of(exchange.Messages)),
            overlapped.Select(exchange => CorpusFingerprint.Of(exchange.Messages)));
    }

    [Fact]
    public async Task GenerateAsync_ABatch_WaitsForNoMoreAnswersAtOnceThanItWasGiven()
    {
        // Arrange
        var source = new OverlappingAiEmailContentSource(holdsAnswers: true);

        // Act
        var generation = SyntheticEmailGenerator.GenerateAsync(
            Plan(count: 60),
            source,
            3,
            TestContext.Current.CancellationToken);

        await source.AskedAsync(3);
        source.Answer();

        await generation;

        // Assert
        // Three held answers are what the fourth call waits behind, so a run against a provider opens the number of
        // connections a developer asked for rather than one per message in the corpus.
        Assert.Equal(3, source.PeakInFlight);
    }

    [Fact]
    public async Task GenerateAsync_AReply_IsAskedForOnlyAfterTheMessageItAnswers()
    {
        // Arrange
        var source = new OverlappingAiEmailContentSource();

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(count: 60),
            source,
            16,
            TestContext.Current.CancellationToken);

        // Assert
        // A reply's prompt carries the subject the parent was answered with, so a prompt built before that answer
        // arrived would name a subject no message in the corpus has.
        var answered = source.Requests.Where(request => request.ParentSubject is not null).ToArray();

        Assert.NotEmpty(answered);
        Assert.All(
            answered,
            request => Assert.Contains(corpus, email => email.Subject == request.ParentSubject));
    }

    [Fact]
    public async Task GenerateAsync_AConcurrencyBelowOne_IsRefused()
    {
        // Arrange, Act, Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => SyntheticEmailGenerator.GenerateAsync(
            Plan(count: 4),
            new OverlappingAiEmailContentSource(),
            0,
            TestContext.Current.CancellationToken));
    }

    private static SyntheticCorpusPlan Plan(int count) => new(
        Seed: 20260902,
        count,
        LatestSentAt,
        SpanDays: 90,
        MaximumAttachmentBytes: 64 * 1024,
        SensitivePercentage: 30,
        ["en", "pl"],
        [SyntheticMailTopic.Business, SyntheticMailTopic.Travel]);
}
