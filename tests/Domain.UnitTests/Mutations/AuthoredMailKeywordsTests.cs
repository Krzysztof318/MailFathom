// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

public sealed class AuthoredMailKeywordsTests
{
    /// <summary>An ordinary label is exactly what this type exists to carry, whichever registered form it takes.</summary>
    [Theory]
    [InlineData("$Junk")]
    [InlineData("$NotJunk")]
    [InlineData("$Forwarded")]
    [InlineData("Todo")]
    [InlineData("needs-reply")]
    [InlineData("a")]
    public void IsWritable_AKeywordAServerCanStore_IsAccepted(string keyword)
    {
        // Act
        var writable = AuthoredMailKeywords.IsWritable(keyword);

        // Assert
        Assert.True(writable);
    }

    /// <summary>
    /// The written form is what a <c>STORE</c> sends, so the grammar is judged against it rather than against the
    /// upper-cased form — which for a handful of characters lands inside US-ASCII and would pass a check the bytes on
    /// the wire would not.
    /// </summary>
    [Theory]
    [InlineData("ſ")]
    [InlineData("ſtarred")]
    public void IsWritable_AKeywordFoldingIntoAsciiFromOutsideIt_IsRefused(string keyword)
    {
        // Act
        var writable = AuthoredMailKeywords.IsWritable(keyword);

        // Assert
        Assert.True(keyword.ToUpperInvariant().All(character => character < (char)0x7F));
        Assert.False(writable);
    }

    /// <summary>
    /// A backslash is how a system flag is spelled, so refusing it is what keeps <c>\Answered</c> and <c>\Draft</c> out
    /// of a keyword list and therefore refused as this system says they are.
    /// </summary>
    [Theory]
    [InlineData("\\Answered")]
    [InlineData("\\Draft")]
    [InlineData("\\Seen")]
    [InlineData("\\Deleted")]
    public void IsWritable_ASystemFlagWrittenAsAKeyword_IsRefused(string flag)
    {
        // Act
        var writable = AuthoredMailKeywords.IsWritable(flag);

        // Assert
        Assert.False(writable);
    }

    /// <summary>A keyword is an IMAP atom, so anything the grammar excludes reaches the server as a malformed command.</summary>
    [Theory]
    [InlineData("two words")]
    [InlineData("(parenthesis")]
    [InlineData("brace{")]
    [InlineData("percent%")]
    [InlineData("star*")]
    [InlineData("quote\"")]
    [InlineData("bracket]")]
    [InlineData("bell\u0007")]
    [InlineData("del\u007F")]
    [InlineData("zażółć")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsWritable_ANonAtom_IsRefused(string? keyword)
    {
        // Act
        var writable = AuthoredMailKeywords.IsWritable(keyword);

        // Assert
        Assert.False(writable);
    }

    /// <summary>The bound a stored keyword is read under is the bound an authored one is written under; one answer, one place.</summary>
    [Fact]
    public void IsWritable_AKeywordPastTheLengthBound_IsRefused()
    {
        // Arrange
        var overlong = new string('a', RemoteEmailKeywords.MaximumKeywordLength + 1);

        // Act
        var writable = AuthoredMailKeywords.IsWritable(overlong);

        // Assert
        Assert.False(writable);
        Assert.True(AuthoredMailKeywords.IsWritable(overlong[..RemoteEmailKeywords.MaximumKeywordLength]));
    }

    /// <summary>
    /// The written form is what a <c>STORE</c> puts on somebody's message, so the operator's own spelling survives —
    /// which is the whole difference from the folded form the read side compares in.
    /// </summary>
    [Fact]
    public void Create_KeywordsAsWritten_KeepsTheSpellingRatherThanTheComparisonForm()
    {
        // Act
        var keywords = AuthoredMailKeywords.Create(["NeedsReply", "$Todo"]);

        // Assert
        // Ordered by the folded form so that one set has one rendering, which puts '$' ahead of a letter.
        Assert.Equal(["$Todo", "NeedsReply"], keywords.Values);
    }

    /// <summary>Flag names are case-insensitive, so two spellings of one keyword are one keyword and the first written wins.</summary>
    [Fact]
    public void Create_OneKeywordSpelledTwoWays_KeepsOneAndTakesTheFirstSpelling()
    {
        // Act
        var keywords = AuthoredMailKeywords.Create(["$Todo", "$TODO", "$todo"]);

        // Assert
        Assert.Equal(["$Todo"], keywords.Values);
    }

    /// <summary>The order is the comparison order rather than the written one, so reordering a list is not an edit.</summary>
    [Fact]
    public void Create_TheSameKeywordsInAnotherOrder_ProducesTheSameSequence()
    {
        // Act
        var written = AuthoredMailKeywords.Create(["zeta", "alpha", "Mid"]);
        var reordered = AuthoredMailKeywords.Create(["Mid", "zeta", "alpha"]);

        // Assert
        Assert.Equal(written.Values, reordered.Values);
        Assert.Equal(["alpha", "Mid", "zeta"], written.Values);
    }

    /// <summary>Naming none is how a replacement asks for every keyword to be cleared, so it is a value rather than a refusal.</summary>
    [Fact]
    public void Create_NoKeywords_IsTheEmptySet()
    {
        // Act
        var keywords = AuthoredMailKeywords.Create([]);

        // Assert
        Assert.True(keywords.IsEmpty);
        Assert.Same(AuthoredMailKeywords.None, keywords);
    }

    /// <summary>Dropping a mistyped keyword would leave a rule silently doing less than it says, so authoring refuses it.</summary>
    [Fact]
    public void Create_AKeywordNoServerCanStore_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => AuthoredMailKeywords.Create(["$Todo", "two words"]));

        // Assert
        Assert.Equal("keywords", refusal.ParamName);
    }

    /// <summary>Configuration reads through the try form, so an operator's mistake is reported against their key rather than raised.</summary>
    [Fact]
    public void TryCreate_AKeywordNoServerCanStore_IsRefusedWithoutRaising()
    {
        // Act
        var read = AuthoredMailKeywords.TryCreate(["\\Answered"], out var keywords);

        // Assert
        Assert.False(read);
        Assert.Same(AuthoredMailKeywords.None, keywords);
    }

    /// <summary>One email keeps a bounded number of keywords, so a change naming more than that could never be honored.</summary>
    [Fact]
    public void TryCreate_MoreKeywordsThanOneEmailKeeps_IsRefused()
    {
        // Arrange
        var atTheBound = Enumerable
            .Range(0, RemoteEmailKeywords.MaximumKeywords)
            .Select(position => $"label{position}")
            .ToArray();
        string[] pastTheBound = [.. atTheBound, "onemore"];

        // Act
        var readAtTheBound = AuthoredMailKeywords.TryCreate(atTheBound, out var kept);
        var readPastTheBound = AuthoredMailKeywords.TryCreate(pastTheBound, out _);

        // Assert
        Assert.True(readAtTheBound);
        Assert.Equal(RemoteEmailKeywords.MaximumKeywords, kept.Values.Count);
        Assert.False(readPastTheBound);
    }

    /// <summary>Two sets naming one keyword two ways are one set, because the folded form is what decides sameness.</summary>
    [Fact]
    public void Equals_SetsDifferingOnlyInSpelling_AreTheSameSet()
    {
        // Act
        var written = AuthoredMailKeywords.Create(["$Todo"]);
        var spelledDifferently = AuthoredMailKeywords.Create(["$TODO"]);
        var another = AuthoredMailKeywords.Create(["$Done"]);

        // Assert
        Assert.Equal(written, spelledDifferently);
        Assert.Equal(written.GetHashCode(), spelledDifferently.GetHashCode());
        Assert.NotEqual(written, another);
    }
}
