// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.Generation.AiContent;
using MailFathom.SyntheticMail.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation;

/// <summary>What a corpus is when its content comes from a source rather than from the seed.</summary>
/// <remarks>
/// The seed still decides the envelope — author, thread, date, language, topic, attachment — and these are the
/// assertions that keep that division honest: two runs agreeing on the seed agree on the envelope, and the source's
/// answer is the only thing between them.
/// </remarks>
public sealed class SyntheticEmailGeneratorAiModeTests
{
    private static readonly DateTimeOffset LatestSentAt = new(2026, 8, 8, 23, 59, 59, TimeSpan.Zero);

    private static readonly AiEmailContent Answer = new(
        "Quarterly figures",
        "Hello,\n\nThe figures are attached.\n\nRegards\nAnna");

    [Fact]
    public async Task GenerateAsync_TwoRunsOfOneSeed_AskTheSourceTheSameQuestionsInTheSameOrder()
    {
        // Arrange
        var first = new ScriptedAiEmailContentSource(Answer);
        var second = new ScriptedAiEmailContentSource(Answer);

        // Act
        await SyntheticEmailGenerator.GenerateAsync(Plan(["en", "pl"], [SyntheticMailTopic.Business, SyntheticMailTopic.Travel]), first, CancellationToken.None);
        await SyntheticEmailGenerator.GenerateAsync(Plan(["en", "pl"], [SyntheticMailTopic.Business, SyntheticMailTopic.Travel]), second, CancellationToken.None);

        // Assert
        Assert.Equal(first.Requests.Select(RequestFingerprint), second.Requests.Select(RequestFingerprint));
    }

    [Fact]
    public async Task GenerateAsync_TwoLanguages_AreBothWrittenWhenTheBatchIsLargeEnough()
    {
        // Arrange, Act
        var source = await Generate(["en", "pl"], [SyntheticMailTopic.Business], count: 40);

        // Assert
        Assert.Contains("en", source.Requests.Select(request => request.LanguageCode));
        Assert.Contains("pl", source.Requests.Select(request => request.LanguageCode));
        Assert.Equal(40, source.Requests.Count);
    }

    [Fact]
    public async Task GenerateAsync_TwoTopics_AreBothWrittenWhenTheBatchIsLargeEnough()
    {
        // Arrange, Act
        var source = await Generate(
            ["en"],
            [SyntheticMailTopic.Invoices, SyntheticMailTopic.TechnicalSupport],
            count: 40);

        // Assert
        Assert.Contains(SyntheticMailTopic.Invoices, source.Requests.Select(request => request.Topic));
        Assert.Contains(SyntheticMailTopic.TechnicalSupport, source.Requests.Select(request => request.Topic));
    }

    [Fact]
    public async Task GenerateAsync_EveryMessage_CarriesTheOriginTheSeedDrewForIt()
    {
        // Arrange, Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(["pl"], [SyntheticMailTopic.Travel]),
            new ScriptedAiEmailContentSource(Answer),
            CancellationToken.None);

        // Assert
        Assert.All(corpus, email => Assert.Equal(new SyntheticEmailAiOrigin("pl", SyntheticMailTopic.Travel), email.AiOrigin));
    }

    [Fact]
    public async Task GenerateAsync_ANewThread_CarriesTheSubjectTheSourceAnswered()
    {
        // Arrange
        var plan = Plan(["en"], [SyntheticMailTopic.Business], count: 1);

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(plan, new ScriptedAiEmailContentSource(Answer), CancellationToken.None);

        // Assert
        Assert.Equal(Answer.Subject, corpus.Single().Subject);
        Assert.Null(corpus.Single().InReplyTo);
    }

    [Fact]
    public async Task GenerateAsync_AReply_KeepsTheThreadSubjectAndSaysWhatItAnswers()
    {
        // Arrange
        // One request per message, in message order, so the reply's request is the one at the reply's index.
        var source = new ScriptedAiEmailContentSource(Answer);

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business], count: 40),
            source,
            CancellationToken.None);
        var reply = corpus.First(email => email.InReplyTo is not null);
        var replyIndex = corpus.ToList().IndexOf(reply);

        // Assert
        var parent = corpus.Single(email => email.MessageId == reply.InReplyTo);
        Assert.Equal($"Re: {parent.Subject}", reply.Subject);
        Assert.Equal(parent.Subject, source.Requests[replyIndex].ParentSubject);
    }

    [Fact]
    public async Task GenerateAsync_TheBody_IsTheSourceAnswerWithTheDecoyPlantedWhereTheSeedDecides()
    {
        // Arrange
        var plan = Plan(["en"], [SyntheticMailTopic.Business], sensitivePercentage: 100);

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(plan, new ScriptedAiEmailContentSource(Answer), CancellationToken.None);

        // Assert
        // Every message carries the answer's paragraphs and, beside them, the decoy the seed planted — recorded on
        // the body the way a scanner's finding records it, and present in the text where it was written.
        Assert.All(corpus, email =>
        {
            var text = email.Body.PlainText;

            Assert.StartsWith("Hello,", text, StringComparison.Ordinal);
            Assert.Contains("The figures are attached.", text, StringComparison.Ordinal);
            Assert.Contains("Regards\nAnna", text, StringComparison.Ordinal);
            Assert.NotNull(email.Body.Decoy);
            Assert.Contains(email.Body.Decoy!.Sentence, text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GenerateAsync_TheBody_IsWrittenUtf8WhetherOrNotTheLanguageNeededIt()
    {
        // Arrange, Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business]),
            new ScriptedAiEmailContentSource(Answer),
            CancellationToken.None);

        // Assert
        Assert.All(corpus, email => Assert.Equal(SyntheticCharacterSet.Utf8, email.Body.CharacterSet));
    }

    [Fact]
    public async Task GenerateAsync_TheEnvelope_StillThreadsDatesAndAttachmentsFromTheSeed()
    {
        // Arrange, Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business], count: 60),
            new ScriptedAiEmailContentSource(Answer),
            CancellationToken.None);

        // Assert
        // The same invariants the seeded corpus carries: a reply is later than what it answers, the dates advance
        // with the index, and every reply names the message it answers.
        Assert.All(corpus.Where(email => email.InReplyTo is not null), email =>
        {
            var parent = corpus.Single(parent => parent.MessageId == email.InReplyTo);

            Assert.True(email.SentAt > parent.SentAt);
            Assert.Contains(parent.MessageId, email.References);
        });

        Assert.Equal(corpus.OrderBy(email => email.SentAt).Select(email => email.MessageId), corpus.Select(email => email.MessageId));
    }

    [Fact]
    public async Task GenerateAsync_TheSourceFailing_FailsTheRunWithoutDeliveringAnything()
    {
        // Arrange
        var failure = new SyntheticMailFailure("the endpoint refused the API key");

        // Act, Assert
        await Assert.ThrowsAsync<SyntheticMailFailure>(() => SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business]),
            new ScriptedAiEmailContentSource(failure),
            CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_ACancellation_StopsTheRun()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business], count: 10),
            new ScriptedAiEmailContentSource(Answer),
            cancellation.Token));
    }

    [Fact]
    public void Generate_APlanWithNamedLanguages_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(
            () => SyntheticEmailGenerator.Generate(Plan(["en"], [SyntheticMailTopic.Business])));
    }

    [Fact]
    public async Task GenerateAsync_APlanNamingNoTopics_IsRefused()
    {
        // Arrange, Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], []),
            new ScriptedAiEmailContentSource(Answer),
            CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_APlanNamingAnUnspecifiedTopic_IsRefused()
    {
        // Arrange, Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [default]),
            new ScriptedAiEmailContentSource(Answer),
            CancellationToken.None));
    }

    private static async Task<ScriptedAiEmailContentSource> Generate(
        IReadOnlyList<string> languages,
        IReadOnlyList<SyntheticMailTopic> topics,
        int count = 20)
    {
        var source = new ScriptedAiEmailContentSource(Answer);

        await SyntheticEmailGenerator.GenerateAsync(Plan(languages, topics, count), source, CancellationToken.None);

        return source;
    }

    private static SyntheticCorpusPlan Plan(
        IReadOnlyList<string> languages,
        IReadOnlyList<SyntheticMailTopic> topics,
        int count = 20,
        int sensitivePercentage = 0) => new(
            Seed: 4711,
            count,
            LatestSentAt,
            SpanDays: 90,
            MaximumAttachmentBytes: 64 * 1024,
            sensitivePercentage,
            languages,
            topics);

    private static string RequestFingerprint(AiEmailContentRequest request) =>
        $"{request.LanguageCode}|{request.Topic}|{request.AuthorName}|{request.ParentSubject ?? "-"}";
}
