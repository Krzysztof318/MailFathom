// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Encodings.Web;
using System.Text.Unicode;
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
        "Hello,\n\nThe figures are attached.\n\nRegards\nAnna",
        "<html><body><h1>Quarterly figures</h1><p>The figures are attached.</p></body></html>");

    [Fact]
    public async Task GenerateAsync_TwoRunsOfOneSeed_AskTheSourceTheSameQuestionsInTheSameOrder()
    {
        // Arrange
        var first = new ScriptedAiEmailContentSource(Answer);
        var second = new ScriptedAiEmailContentSource(Answer);

        // Act
        await SyntheticEmailGenerator.GenerateAsync(Plan(["en", "pl"], [SyntheticMailTopic.Business, SyntheticMailTopic.Travel]), first, 1, CancellationToken.None);
        await SyntheticEmailGenerator.GenerateAsync(Plan(["en", "pl"], [SyntheticMailTopic.Business, SyntheticMailTopic.Travel]), second, 1, CancellationToken.None);

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
            1,
            CancellationToken.None);

        // Assert
        Assert.All(
            corpus,
            email =>
            {
                Assert.NotNull(email.AiOrigin);
                Assert.Equal("pl", email.AiOrigin.Language);
                Assert.Equal(SyntheticMailTopic.Travel, email.AiOrigin.Topic);
                Assert.Contains(email.AiOrigin.MarkupDialect, SyntheticMarkupDialect.All);
            });
    }

    [Fact]
    public async Task GenerateAsync_EveryMarkupDialect_IsReachedWhenTheBatchIsLargeEnough()
    {
        // Arrange, Act
        var source = await Generate(["en"], [SyntheticMailTopic.Business], count: 120);

        // Assert
        // No invocation names a dialect, so a corpus large enough has to meet all of them: a run that could only
        // reach some would leave whichever it missed untested by every corpus anybody generated.
        var asked = source.Requests.Select(request => request.MarkupDialect).Distinct().ToArray();

        Assert.Equal(SyntheticMarkupDialect.All.Count, asked.Length);
    }

    [Fact]
    public async Task GenerateAsync_TheMarkupAskedFor_IsTheOneTheMessageReports()
    {
        // Arrange
        var source = new ScriptedAiEmailContentSource(Answer);

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business], count: 40),
            source,
            1,
            CancellationToken.None);

        // Assert
        // The listing is what a message that reads badly is reproduced from, so the dialect it prints has to be the
        // one the source was actually asked for rather than a second draw beside it.
        Assert.Equal(
            source.Requests.Select(request => request.MarkupDialect),
            corpus.Select(message => message.AiOrigin!.MarkupDialect));
    }

    [Fact]
    public async Task GenerateAsync_ANewThread_CarriesTheSubjectTheSourceAnswered()
    {
        // Arrange
        var plan = Plan(["en"], [SyntheticMailTopic.Business], count: 1);

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(plan, new ScriptedAiEmailContentSource(Answer), 1, CancellationToken.None);

        // Assert
        Assert.Equal(Answer.Subject, corpus.Single().Subject);
        Assert.Null(corpus.Single().InReplyTo);
    }

    [Fact]
    public async Task GenerateAsync_AReply_KeepsTheThreadSubjectAndSaysWhatItAnswers()
    {
        // Arrange
        // One request per message, and this run waits for one answer at a time, so the requests arrive in message
        // order and the reply's is the one at the reply's index.
        var source = new ScriptedAiEmailContentSource(Answer);

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business], count: 40),
            source,
            1,
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
        var corpus = await SyntheticEmailGenerator.GenerateAsync(plan, new ScriptedAiEmailContentSource(Answer), 1, CancellationToken.None);

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
            1,
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
            1,
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
            1,
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
            1,
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
    public void Generate_APlanWithNamedTopics_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(
            () => SyntheticEmailGenerator.Generate(Plan([], [SyntheticMailTopic.Business])));
    }

    [Fact]
    public async Task GenerateAsync_APlanWithNamedTopicsAndNoLanguages_IsRefused()
    {
        // Arrange
        var source = new ScriptedAiEmailContentSource(Answer);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => SyntheticEmailGenerator.GenerateAsync(
            Plan([], [SyntheticMailTopic.Business]),
            source,
            1,
            CancellationToken.None));
        Assert.Empty(source.Requests);
    }

    [Fact]
    public async Task GenerateAsync_APlanNamingNoTopics_IsRefused()
    {
        // Arrange, Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], []),
            new ScriptedAiEmailContentSource(Answer),
            1,
            CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_APlanNamingAnUnspecifiedTopic_IsRefused()
    {
        // Arrange, Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [default]),
            new ScriptedAiEmailContentSource(Answer),
            1,
            CancellationToken.None));
    }

    private static async Task<ScriptedAiEmailContentSource> Generate(
        IReadOnlyList<string> languages,
        IReadOnlyList<SyntheticMailTopic> topics,
        int count = 20)
    {
        var source = new ScriptedAiEmailContentSource(Answer);

        await SyntheticEmailGenerator.GenerateAsync(Plan(languages, topics, count), source, 1, CancellationToken.None);

        return source;
    }

    [Fact]
    public async Task GenerateAsync_TheHtmlAlternative_IsTheMarkupTheSourceAnsweredRatherThanTheTextInTags()
    {
        // Arrange
        var plan = Plan(["en"], [SyntheticMailTopic.Business]);
        var source = new ScriptedAiEmailContentSource(Answer);

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(plan, source, 1, CancellationToken.None);

        // Assert
        // The deterministic generator wraps its own text in one paragraph per block, which exercises the extractor
        // against markup this repository wrote. What earns this mode its place is markup nobody here chose.
        Assert.All(corpus, email => Assert.Equal(Answer.Html, email.Body.Html));
    }

    [Fact]
    public async Task GenerateAsync_ADecoyInAnAnsweredDocument_IsPlantedInBothAlternatives()
    {
        // Arrange
        var plan = Plan(["en"], [SyntheticMailTopic.Business], sensitivePercentage: 100);
        var source = new ScriptedAiEmailContentSource(Answer);

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(plan, source, 1, CancellationToken.None);

        // Assert
        // Which alternative a reader extracts from is the extractor's choice, so a decoy in only one of them would
        // make a scan's result depend on a MIME shape the seed drew rather than on the scanner.
        Assert.All(corpus, email =>
        {
            Assert.NotNull(email.Body.Decoy);
            Assert.Contains(email.Body.Decoy!.Sentence, email.Body.PlainText, StringComparison.Ordinal);
            Assert.Contains(HtmlDecoy(email.Body.Decoy.Sentence), email.Body.Html, StringComparison.Ordinal);
            Assert.EndsWith("</body></html>", email.Body.Html, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task GenerateAsync_AReply_TellsTheSourceWhatItIsAnswering()
    {
        // Arrange
        var plan = Plan(["en"], [SyntheticMailTopic.Business], count: 40);
        var source = new ScriptedAiEmailContentSource(Answer);

        // Act
        await SyntheticEmailGenerator.GenerateAsync(plan, source, 1, CancellationToken.None);

        // Assert
        // A request that named the subject alone produced a second message about the same topic, which reads as a
        // thread only in the headers.
        Assert.Contains(source.Requests, request => request.ParentOpening is not null);
        Assert.All(
            source.Requests,
            request => Assert.Equal(request.ParentSubject is null, request.ParentOpening is null));
    }

    [Fact]
    public async Task GenerateAsync_AMessageCarryingATextFile_AsksTheSourceToWriteThatFileWithinTheSizeTheSeedDrew()
    {
        // Arrange, Act
        var source = await Generate(["en"], [SyntheticMailTopic.Business], count: 40);

        // Assert
        // The name and the bound are the seed's, so which messages enclose a file and how large each one is stay
        // reproducible even though the words in it are not.
        var asked = source.Requests.Where(request => request.AttachmentFileName is not null).ToArray();

        Assert.NotEmpty(asked);
        Assert.All(asked, request => Assert.True(
            request.AttachmentFileName!.EndsWith(".csv", StringComparison.Ordinal)
            || request.AttachmentFileName.EndsWith(".txt", StringComparison.Ordinal),
            request.AttachmentFileName));
        Assert.All(asked, request => Assert.InRange(request.AttachmentCharacterBound, 1, 64 * 1024));
        Assert.All(
            source.Requests.Where(request => request.AttachmentFileName is null),
            request => Assert.Equal(0, request.AttachmentCharacterBound));
    }

    [Fact]
    public async Task GenerateAsync_AnOpaqueAttachment_IsNeverAskedFor()
    {
        // Arrange, Act
        var source = await Generate(["en"], [SyntheticMailTopic.Business], count: 40);

        // Assert
        // An opaque part is opaque on purpose, and a model asked to write one would answer with text a reader then
        // finds inside something claiming not to be readable.
        Assert.DoesNotContain(".bin", string.Join(",", source.Requests.Select(request => request.AttachmentFileName)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_AnOpaqueAttachment_KeepsItsDrawnBytesWhateverTheSourceAnswered()
    {
        // Arrange
        // This source answers every request with a file, which the real one never does for an opaque part. That is
        // the point: the guarantee is enforced where the two are merged rather than only by the caller that asks.
        var source = new ScriptedAiEmailContentSource(Answer with { Attachment = "marker,depth\n7,4.2" });

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business], count: 40),
            source,
            1,
            CancellationToken.None);

        // Assert
        // A binary part exercises the extractor's refusal, so text written into one would be delivered as UTF-8 and
        // stop testing the thing it exists for.
        var opaque = corpus
            .Select(message => message.Attachment)
            .OfType<SyntheticEmailAttachment>()
            .Where(attachment => !attachment.IsText)
            .ToArray();

        Assert.NotEmpty(opaque);
        Assert.All(opaque, attachment => Assert.Null(attachment.Text));
    }

    [Fact]
    public async Task GenerateAsync_AnAnsweredFile_BecomesWhatTheMessageEncloses()
    {
        // Arrange
        var source = new ScriptedAiEmailContentSource(Answer with { Attachment = "marker,depth\n7,4.2" });

        // Act
        var corpus = await SyntheticEmailGenerator.GenerateAsync(
            Plan(["en"], [SyntheticMailTopic.Business], count: 40),
            source,
            1,
            CancellationToken.None);

        // Assert
        // The length moves with the file, because a listing reporting the size the seed drew while the part holds
        // what the model wrote is a corpus nobody can compare.
        var carried = corpus
            .Select(message => message.Attachment)
            .OfType<SyntheticEmailAttachment>()
            .Where(attachment => attachment.IsText)
            .ToArray();

        Assert.NotEmpty(carried);
        Assert.All(carried, attachment => Assert.Equal("marker,depth\n7,4.2", attachment.Text));
        Assert.All(carried, attachment => Assert.Equal(18, attachment.Length));
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

    /// <summary>The paragraph a planted decoy becomes in a document, encoded the way the generator writes one.</summary>
    /// <remarks>
    /// The encoder is rebuilt here rather than reached for, because it is a production detail and the assertion is
    /// about the decoy reaching the document at all: a sentence carrying an ampersand or an angle bracket is escaped
    /// on the way in, so comparing against the raw sentence would fail on exactly those and pass on the rest.
    /// </remarks>
    private static string HtmlDecoy(string sentence) =>
        $"<p>{HtmlEncoder.Create(new TextEncoderSettings(UnicodeRanges.All)).Encode(sentence)}</p>";

    private static string RequestFingerprint(AiEmailContentRequest request) =>
        $"{request.LanguageCode}|{request.Topic}|{request.MarkupDialect}|{request.AuthorName}|{request.ParentSubject ?? "-"}";
}
