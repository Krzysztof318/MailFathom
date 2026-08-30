// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Commands;
using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.Generation.SensitiveDecoys;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Commands;

/// <summary>What a dry run's line actually says, which is the whole deliverable of a dry run.</summary>
/// <remarks>
/// The determinism tests elsewhere assert that one seed lists the same corpus twice, which is a property two identical
/// wrong lines satisfy just as well as two right ones. These assert the content: a listing that printed the author's
/// address where a copied participant belongs, or miscounted the copies, would survive every other test in this suite
/// and quietly make the `diff` a developer reads mean something else.
/// </remarks>
public sealed class CorpusListingTests
{
    private static readonly DateTimeOffset SentAt = new(2026, 3, 14, 9, 5, 7, TimeSpan.Zero);

    [Fact]
    public void Describe_AMessageCarryingAnAttachment_NamesEveryAxisInOrder()
    {
        // Arrange
        var email = Build(
            inReplyTo: "parent@synthetic.test",
            carbonCopies: 2,
            attachment: new SyntheticEmailAttachment("notes.csv", "text", "csv", 4096, ContentSeed: 7));

        // Act
        var line = CorpusListing.Describe(email);

        // Assert
        Assert.Equal(
            "2026-03-14T09:05:07+00:00 | <first@synthetic.test> | in-reply-to=parent@synthetic.test | TextAndHtmlAlternative | Utf8 | from=author@example.test | cc=2 | attachment=notes.csv (4096 bytes) | sensitive=none | Quarterly figures",
            line);
    }

    [Fact]
    public void Describe_AMessageOpeningAThreadWithNothingAttached_SaysSoRatherThanOmittingTheField()
    {
        // Arrange
        var email = Build(inReplyTo: null, carbonCopies: 0, attachment: null);

        // Act
        var line = CorpusListing.Describe(email);

        // Assert
        // Both absences print a placeholder rather than an empty field, so the columns of two listings stay aligned
        // and a `diff` points at the value that moved instead of at every line after it.
        Assert.Equal(
            "2026-03-14T09:05:07+00:00 | <first@synthetic.test> | in-reply-to=- | TextAndHtmlAlternative | Utf8 | from=author@example.test | cc=0 | attachment=none | sensitive=none | Quarterly figures",
            line);
    }

    [Fact]
    public void Describe_TheAuthor_IsTheAuthorRatherThanACopiedParticipant()
    {
        // Arrange
        var email = Build(inReplyTo: null, carbonCopies: 1, attachment: null);

        // Act
        var line = CorpusListing.Describe(email);

        // Assert
        Assert.Contains("from=author@example.test", line, StringComparison.Ordinal);
        Assert.DoesNotContain("copied-1@example.test", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(SyntheticBodyShape.PlainTextOnly), nameof(SyntheticCharacterSet.Ascii))]
    [InlineData(nameof(SyntheticBodyShape.HtmlOnly), nameof(SyntheticCharacterSet.Latin1))]
    public void Describe_TheBody_NamesTheShapeAndTheCharacterSetItWasGeneratedWith(
        string shapeName,
        string characterSetName)
    {
        // Arrange
        // The names cross the signature rather than the values: both enums are internal, and a public test method
        // cannot take a parameter of a type less accessible than itself.
        var shape = Enum.Parse<SyntheticBodyShape>(shapeName);
        var characterSet = Enum.Parse<SyntheticCharacterSet>(characterSetName);
        var email = Build(inReplyTo: null, carbonCopies: 0, attachment: null) with
        {
            Body = new SyntheticEmailBody(shape, "text", "<p>text</p>", characterSet, Decoy: null),
        };

        // Act
        var line = CorpusListing.Describe(email);

        // Assert
        Assert.Contains($"| {shape} | {characterSet} |", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_AMessageCarryingFabricatedSensitiveMaterial_NamesTheCategoryAndNeverTheValue()
    {
        // Arrange
        var decoy = SensitiveDecoyCatalog.Kinds
            .Single(kind => kind.Rule == "aws-access-token")
            .Plant(new Random(4), SensitiveDecoyPlacement.InATableCell);
        var email = Build(inReplyTo: null, carbonCopies: 0, attachment: null) with
        {
            Body = new SyntheticEmailBody(
                SyntheticBodyShape.PlainTextOnly,
                decoy.Sentence,
                $"<p>{decoy.Sentence}</p>",
                SyntheticCharacterSet.Ascii,
                decoy),
        };

        // Act
        var line = CorpusListing.Describe(email);

        // Assert
        Assert.Contains("| sensitive=Secrets:CloudAccessKey@InATableCell |", line, StringComparison.Ordinal);
        Assert.DoesNotContain("AKIA", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_AMessageGeneratedByAProvider_NamesTheLanguageAndTheTopicItWasGeneratedUnder()
    {
        // Arrange
        var email = Build(inReplyTo: null, carbonCopies: 0, attachment: null) with
        {
            AiOrigin = new SyntheticEmailAiOrigin("pl", SyntheticMailTopic.TechnicalSupport),
        };

        // Act
        var line = CorpusListing.Describe(email);

        // Assert
        Assert.Contains("| sensitive=none | language=pl topic=technical-support |", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_AMessageTheSeededVocabularyWrote_CarriesNoLanguageField()
    {
        // Arrange, Act
        var line = CorpusListing.Describe(Build(inReplyTo: null, carbonCopies: 0, attachment: null));

        // Assert
        Assert.DoesNotContain("language=", line, StringComparison.Ordinal);
        Assert.DoesNotContain("topic=", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ANullArgument_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() => CorpusListing.Describe(null!));

    private static SyntheticEmail Build(string? inReplyTo, int carbonCopies, SyntheticEmailAttachment? attachment) =>
        new(
            MessageId: "first@synthetic.test",
            InReplyTo: inReplyTo,
            References: inReplyTo is null ? [] : [inReplyTo],
            Author: new SyntheticParticipant("Author", "author@example.test"),
            CarbonCopies: [.. Enumerable
                .Range(1, carbonCopies)
                .Select(index => new SyntheticParticipant($"Copied {index}", $"copied-{index}@example.test"))],
            Subject: "Quarterly figures",
            SentAt: SentAt,
            Body: new SyntheticEmailBody(
                SyntheticBodyShape.TextAndHtmlAlternative,
                "text",
                "<p>text</p>",
                SyntheticCharacterSet.Utf8,
                Decoy: null),
            Attachment: attachment,
            AiOrigin: null);
}
