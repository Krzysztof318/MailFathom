// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Chunking;

/// <summary>Covers what a chunk's identity has to depend on for a stored vector to stay attributable to it.</summary>
public sealed class EmailChunkContentHashTests
{
    private const string Passage = "The roof above the west stairwell is leaking again after Tuesday's storm.";

    /// <summary>An unchanged message under unchanged rules must cost nothing, which is the digest agreeing with itself.</summary>
    [Fact]
    public void Compute_SameTextAndRules_ProducesTheSameDigest()
    {
        // Act
        var first = EmailChunkContentHash.Compute(EmailChunkingRules.Current, isDerivedFromLossyHtml: false, Passage);
        var second = EmailChunkContentHash.Compute(EmailChunkingRules.Current, isDerivedFromLossyHtml: false, Passage);

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>The obvious half of the contract: different text is a different passage.</summary>
    [Fact]
    public void Compute_DifferentText_ProducesADifferentDigest()
    {
        // Act
        var first = EmailChunkContentHash.Compute(EmailChunkingRules.Current, isDerivedFromLossyHtml: false, Passage);
        var second = EmailChunkContentHash.Compute(
            EmailChunkingRules.Current,
            isDerivedFromLossyHtml: false,
            Passage + " Again.");

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// The half the whole attribution argument rests on. A digest covering only the text would report chunks as
    /// unchanged after a boundary change and leave vectors hanging on rules they no longer describe, which is exactly
    /// what ADR 0006 places the rules in the chunk's identity to prevent.
    /// </summary>
    [Theory]
    [MemberData(nameof(RulesDifferingFromTheCurrentOnes))]
    public void Compute_ChangedRulesAndUnchangedText_ProducesADifferentDigest(EmailChunkingRules changedRules)
    {
        // Act
        var current = EmailChunkContentHash.Compute(
            EmailChunkingRules.Current,
            isDerivedFromLossyHtml: false,
            Passage);
        var changed = EmailChunkContentHash.Compute(changedRules, isDerivedFromLossyHtml: false, Passage);

        // Assert
        Assert.NotEqual(current, changed);
    }

    /// <summary>
    /// A passage read from markup is worth less to a later ranking than the same words read from a plain-text part, so
    /// the two are not one chunk under two names.
    /// </summary>
    [Fact]
    public void Compute_DifferentSourceProvenance_ProducesADifferentDigest()
    {
        // Act
        var fromPlainText = EmailChunkContentHash.Compute(
            EmailChunkingRules.Current,
            isDerivedFromLossyHtml: false,
            Passage);
        var fromHtml = EmailChunkContentHash.Compute(
            EmailChunkingRules.Current,
            isDerivedFromLossyHtml: true,
            Passage);

        // Assert
        Assert.NotEqual(fromPlainText, fromHtml);
    }

    /// <summary>
    /// Every field is length-prefixed so the encoding is one-to-one: moving a character from the end of one field to
    /// the start of the next must not leave the two hashing alike.
    /// </summary>
    [Fact]
    public void Compute_SeparatorLadderRearranged_ProducesADifferentDigest()
    {
        // Arrange
        var joined = RulesWithSeparators(["ab", "c"]);
        var split = RulesWithSeparators(["a", "bc"]);

        // Act
        var first = EmailChunkContentHash.Compute(joined, isDerivedFromLossyHtml: false, Passage);
        var second = EmailChunkContentHash.Compute(split, isDerivedFromLossyHtml: false, Passage);

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>The digest is a fixed-width value the schema stores and a query compares, in one spelling only.</summary>
    [Fact]
    public void Compute_AnyText_ProducesLowercaseHexadecimalOfTheDeclaredLength()
    {
        // Act
        var hash = EmailChunkContentHash.Compute(EmailChunkingRules.Current, isDerivedFromLossyHtml: false, Passage);

        // Assert
        Assert.Equal(EmailChunkContentHash.Length, hash.Value.Length);
        Assert.All(hash.Value, character => Assert.True(
            "0123456789abcdef".Contains(character, StringComparison.Ordinal),
            "A digest is written in lowercase hexadecimal only."));
    }

    /// <summary>A digest read back from a row is the one that was written, so it round-trips through its own value.</summary>
    [Fact]
    public void Create_ADigestThatWasComputed_ReadsBackAsTheSameValue()
    {
        // Arrange
        var computed = EmailChunkContentHash.Compute(
            EmailChunkingRules.Current,
            isDerivedFromLossyHtml: false,
            Passage);

        // Act
        var readBack = EmailChunkContentHash.Create(computed.Value);

        // Assert
        Assert.Equal(computed, readBack);
        Assert.Equal(computed.Value, readBack.ToString());
    }

    /// <summary>Anything that is not a digest would compare unequal to every real one without ever saying why.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("A0B1C2D3E4F50617A8B9CADBECFD0E1FA2B3C4D5E6F708192A3B4C5D6E7F8091")]
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000000")]
    public void Create_AValueThatIsNotADigest_IsRefused(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => EmailChunkContentHash.Create(value));
    }

    /// <summary>Nothing can be identified from arguments that are not there.</summary>
    [Fact]
    public void Compute_MissingArgument_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            EmailChunkContentHash.Compute(null!, isDerivedFromLossyHtml: false, Passage));
        Assert.Throws<ArgumentNullException>(() =>
            EmailChunkContentHash.Compute(EmailChunkingRules.Current, isDerivedFromLossyHtml: false, null!));
        Assert.Throws<ArgumentNullException>(() => EmailChunkContentHash.Create(null!));
    }

    public static TheoryData<EmailChunkingRules> RulesDifferingFromTheCurrentOnes()
    {
        var current = EmailChunkingRules.Current;

        return
        [
            Vary(ruleSetVersion: current.RuleSetVersion + 1),
            Vary(targetCharacterCount: current.TargetCharacterCount + 1),
            Vary(minimumCharacterCount: current.MinimumCharacterCount + 1),
            Vary(overlapCharacterCount: current.OverlapCharacterCount + 1),
            Vary(sourceForm: EmailChunkSourceForm.OriginalText),
            RulesWithSeparators(["\n"]),
        ];
    }

    private static EmailChunkingRules RulesWithSeparators(IReadOnlyList<string> separators) =>
        Vary(boundarySeparators: separators);

    private static EmailChunkingRules Vary(
        int? ruleSetVersion = null,
        int? targetCharacterCount = null,
        int? minimumCharacterCount = null,
        int? overlapCharacterCount = null,
        IReadOnlyList<string>? boundarySeparators = null,
        EmailChunkSourceForm? sourceForm = null)
    {
        var current = EmailChunkingRules.Current;

        return EmailChunkingRules.Create(
            ruleSetVersion ?? current.RuleSetVersion,
            targetCharacterCount ?? current.TargetCharacterCount,
            minimumCharacterCount ?? current.MinimumCharacterCount,
            overlapCharacterCount ?? current.OverlapCharacterCount,
            boundarySeparators ?? current.BoundarySeparators,
            sourceForm ?? current.SourceForm);
    }
}
