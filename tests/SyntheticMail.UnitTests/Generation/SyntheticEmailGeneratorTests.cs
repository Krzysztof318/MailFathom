// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Generation;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Generation;

/// <summary>What a seed promises about the corpus it produces.</summary>
public sealed class SyntheticEmailGeneratorTests
{
    private static readonly DateTimeOffset LatestSentAt = new(2026, 8, 8, 23, 59, 59, TimeSpan.Zero);

    [Fact]
    public void Generate_TheSamePlan_ProducesTheSameCorpus()
    {
        // Arrange
        var plan = Plan(seed: 4711, count: 40);

        // Act
        var first = SyntheticEmailGenerator.Generate(plan);
        var second = SyntheticEmailGenerator.Generate(plan);

        // Assert
        Assert.Equal(CorpusFingerprint.Of(first), CorpusFingerprint.Of(second));
    }

    [Fact]
    public void Generate_ADifferentSeed_ProducesADifferentCorpus()
    {
        // Arrange
        var plan = Plan(seed: 4711, count: 40);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);
        var other = SyntheticEmailGenerator.Generate(plan with { Seed = 4712 });

        // Assert
        Assert.NotEqual(CorpusFingerprint.Of(corpus), CorpusFingerprint.Of(other));
    }

    [Fact]
    public void Generate_Always_KeepsEveryFabricatedAddressUnderTheReservedDomain()
    {
        // Arrange
        var plan = Plan(seed: 7, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        var addresses = corpus
            .SelectMany(email => email.CarbonCopies.Append(email.Author))
            .Select(participant => participant.Address)
            .Distinct()
            .ToArray();

        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.EndsWith(
            SyntheticVocabulary.ReservedTopLevelDomain,
            address,
            StringComparison.Ordinal));
    }

    [Fact]
    public void Generate_Always_DatesEveryMessageInsideTheRequestedRange()
    {
        // Arrange
        var plan = Plan(seed: 13, count: 150);
        var earliest = plan.LatestSentAt.AddDays(-plan.SpanDays);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        Assert.All(corpus, email => Assert.InRange(email.SentAt, earliest, plan.LatestSentAt));
    }

    [Fact]
    public void Generate_AReply_IsDatedAfterTheMessageItAnswers()
    {
        // Arrange
        var plan = Plan(seed: 21, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        var byId = corpus.ToDictionary(email => email.MessageId, StringComparer.Ordinal);
        var replies = corpus.Where(email => email.InReplyTo is not null).ToArray();

        Assert.NotEmpty(replies);
        Assert.All(replies, reply => Assert.True(reply.SentAt > byId[reply.InReplyTo!].SentAt));
    }

    [Fact]
    public void Generate_AReply_CarriesTheWholeAncestryInItsReferences()
    {
        // Arrange
        var plan = Plan(seed: 21, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        var byId = corpus.ToDictionary(email => email.MessageId, StringComparer.Ordinal);
        var replies = corpus.Where(email => email.InReplyTo is not null).ToArray();

        Assert.NotEmpty(replies);
        Assert.All(replies, reply => Assert.Equal(
            [.. byId[reply.InReplyTo!].References, reply.InReplyTo!],
            reply.References));
    }

    [Fact]
    public void Generate_AnOpeningMessage_ReferencesNothing()
    {
        // Arrange
        var plan = Plan(seed: 21, count: 100);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        Assert.All(
            corpus.Where(email => email.InReplyTo is null),
            email => Assert.Empty(email.References));
    }

    [Fact]
    public void Generate_ALargeEnoughCorpus_CoversEveryBodyShapeAndCharacterSet()
    {
        // Arrange
        var plan = Plan(seed: 5, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        Assert.Equal(
            Enum.GetValues<SyntheticBodyShape>().Order(),
            corpus.Select(email => email.Body.Shape).Distinct().Order());
        Assert.Equal(
            Enum.GetValues<SyntheticCharacterSet>().Order(),
            corpus.Select(email => email.Body.CharacterSet).Distinct().Order());
    }

    [Fact]
    public void Generate_AnAsciiBody_WritesNothingOutsideAscii()
    {
        // Arrange
        var plan = Plan(seed: 31, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        // The charset and the wording are chosen together, so an encoder is never asked to substitute a character.
        var asciiBodies = corpus
            .Where(email => email.Body.CharacterSet == SyntheticCharacterSet.Ascii)
            .Select(email => email.Body.PlainText)
            .ToArray();

        Assert.NotEmpty(asciiBodies);
        Assert.All(asciiBodies, body => Assert.True(body.All(char.IsAscii)));
    }

    [Fact]
    public void Generate_ALatin1Body_WritesNothingOutsideLatin1()
    {
        // Arrange
        var plan = Plan(seed: 31, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        var latin1Bodies = corpus
            .Where(email => email.Body.CharacterSet == SyntheticCharacterSet.Latin1)
            .Select(email => email.Body.PlainText)
            .ToArray();

        Assert.NotEmpty(latin1Bodies);
        Assert.All(latin1Bodies, body => Assert.True(body.All(character => character <= 'ÿ')));
    }

    [Fact]
    public void Generate_ALargeEnoughCorpus_VariesTheSubjectAndBodyLength()
    {
        // Arrange
        var plan = Plan(seed: 8, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        Assert.True(corpus.Max(email => email.Subject.Length) > 3 * corpus.Min(email => email.Subject.Length));
        Assert.True(corpus.Max(email => email.Body.PlainText.Length) > 3 * corpus.Min(email => email.Body.PlainText.Length));
    }

    [Fact]
    public void Generate_ALargeEnoughCorpus_VariesHowManyParticipantsAMessageNames()
    {
        // Arrange
        var plan = Plan(seed: 8, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        Assert.Equal([0, 1, 2, 3], corpus.Select(email => email.CarbonCopies.Count).Distinct().Order());
        Assert.All(corpus, email => Assert.DoesNotContain(email.Author, email.CarbonCopies));
    }

    [Fact]
    public void Generate_ACarbonCopiedMessage_NamesEachParticipantOnce()
    {
        // Arrange
        var plan = Plan(seed: 8, count: 200);

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        Assert.All(corpus, email => Assert.Equal(email.CarbonCopies.Count, email.CarbonCopies.Distinct().Count()));
    }

    [Fact]
    public void Generate_NoAttachmentCeiling_CarriesNoAttachment()
    {
        // Arrange
        var plan = Plan(seed: 3, count: 100) with { MaximumAttachmentBytes = 0 };

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        Assert.All(corpus, email => Assert.Null(email.Attachment));
    }

    [Fact]
    public void Generate_AnAttachmentCeiling_CarriesSomeAttachmentsAndNoneBeyondIt()
    {
        // Arrange
        var plan = Plan(seed: 3, count: 100) with { MaximumAttachmentBytes = 4096 };

        // Act
        var corpus = SyntheticEmailGenerator.Generate(plan);

        // Assert
        var attachments = corpus.Select(email => email.Attachment).OfType<SyntheticEmailAttachment>().ToArray();

        Assert.NotEmpty(attachments);
        Assert.NotEqual(corpus.Count, attachments.Length);
        Assert.All(attachments, attachment => Assert.InRange(attachment.Length, 1, 4096));
    }

    [Fact]
    public void Generate_AnAttachment_MaterializesExactlyItsStatedLengthAndTheSameBytesEveryTime()
    {
        // Arrange
        var plan = Plan(seed: 3, count: 60) with { MaximumAttachmentBytes = 512 };

        // Act
        var attachment = SyntheticEmailGenerator
            .Generate(plan)
            .Select(email => email.Attachment)
            .OfType<SyntheticEmailAttachment>()
            .First();

        // Assert
        Assert.Equal(attachment.Length, attachment.MaterializeContent().Length);
        Assert.Equal(attachment.MaterializeContent().ToArray(), attachment.MaterializeContent().ToArray());
    }

    [Fact]
    public void Generate_ANullPlan_IsRefused()
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentNullException>(() => SyntheticEmailGenerator.Generate(null!));
    }

    private static SyntheticCorpusPlan Plan(int seed, int count) =>
        new(seed, count, LatestSentAt, SpanDays: 90, MaximumAttachmentBytes: 64 * 1024);
}
