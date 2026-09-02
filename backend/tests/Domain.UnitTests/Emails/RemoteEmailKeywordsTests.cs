// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails;

/// <summary>
/// Covers what turns the keywords a server listed into a value: one case, no duplicates, one order, and a bound on
/// what an untrusted answer may cost.
/// </summary>
public sealed class RemoteEmailKeywordsTests
{
    [Fact]
    public void Create_NothingReported_IsTheEmptySet()
    {
        // Act
        var keywords = RemoteEmailKeywords.Create(null);

        // Assert
        Assert.Same(RemoteEmailKeywords.None, keywords);
        Assert.Empty(keywords.Values);
    }

    /// <summary>
    /// RFC 9051 compares flag names without regard to case, so two spellings are one keyword. A set that kept both
    /// would answer a filter for one of them and miss mail carrying the other.
    /// </summary>
    [Fact]
    public void Create_OneKeywordWrittenTwoWays_KeepsItOnce()
    {
        // Act
        var keywords = RemoteEmailKeywords.Create(["$Junk", "$junk", "$JUNK"]);

        // Assert
        Assert.Equal(["$JUNK"], keywords.Values);
    }

    /// <summary>The order a server listed its flags in is not information, and holding it would rewrite the stored mirror whenever the server reordered its own answer.</summary>
    [Fact]
    public void Create_KeywordsInAnyOrder_ProducesOneValue()
    {
        // Act
        var listed = RemoteEmailKeywords.Create(["$Forwarded", "nonjunk", "$Junk"]);
        var reordered = RemoteEmailKeywords.Create(["$junk", "$forwarded", "NonJunk"]);

        // Assert
        Assert.Equal(["$FORWARDED", "$JUNK", "NONJUNK"], listed.Values);
        Assert.Equal(listed, reordered);
        Assert.Equal(listed.GetHashCode(), reordered.GetHashCode());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\u0001label")]
    public void Create_AValueNoStoredKeywordCouldBe_DropsIt(string keyword)
    {
        // Act
        var keywords = RemoteEmailKeywords.Create([keyword, "$Junk"]);

        // Assert
        Assert.Equal(["$JUNK"], keywords.Values);
    }

    /// <summary>A keyword longer than the bound is dropped rather than shortened: a prefix of a label is a label somebody else may legitimately use.</summary>
    [Fact]
    public void Create_AKeywordLongerThanTheBound_DropsItRatherThanTruncatingIt()
    {
        // Arrange
        var overLong = new string('a', RemoteEmailKeywords.MaximumKeywordLength + 1);

        // Act
        var keywords = RemoteEmailKeywords.Create([overLong, "$Junk"]);

        // Assert
        Assert.Equal(["$JUNK"], keywords.Values);
    }

    /// <summary>The bound is the longest keyword that is kept rather than the first one dropped, which is the half a length test written only against an over-long value leaves open.</summary>
    [Fact]
    public void Create_AKeywordExactlyAsLongAsTheBound_KeepsIt()
    {
        // Arrange
        var longestPermitted = new string('a', RemoteEmailKeywords.MaximumKeywordLength);

        // Act
        var keywords = RemoteEmailKeywords.Create([longestPermitted, "$Junk"]);

        // Assert
        Assert.Equal(["$JUNK", longestPermitted.ToUpperInvariant()], keywords.Values);
    }

    /// <summary>
    /// Nothing in the protocol bounds how many keywords a server reports, and the answer is stored on the email's own
    /// row. The excess is discarded rather than refused, because a reconciliation window exists to record what the
    /// server said and failing it over a flag would stop the window recording anything.
    /// </summary>
    [Fact]
    public void Create_MoreKeywordsThanTheBound_KeepsTheOrdinallySmallestOnes()
    {
        // Arrange
        var reported = Enumerable
            .Range(0, RemoteEmailKeywords.MaximumKeywords + 20)
            .Select(ordinal => $"label{ordinal:000}")
            .Reverse();

        // Act
        var keywords = RemoteEmailKeywords.Create(reported);

        // Assert
        Assert.Equal(RemoteEmailKeywords.MaximumKeywords, keywords.Values.Count);
        Assert.Equal("LABEL000", keywords.Values[0]);
        Assert.Equal($"LABEL{RemoteEmailKeywords.MaximumKeywords - 1:000}", keywords.Values[^1]);
    }

    /// <summary>Which keywords the bound gives up follows from the values rather than from the order one server answered in, so two runs against the same message keep the same ones.</summary>
    [Fact]
    public void Create_MoreKeywordsThanTheBoundInAnotherOrder_KeepsTheSameOnes()
    {
        // Arrange
        var reported = Enumerable
            .Range(0, RemoteEmailKeywords.MaximumKeywords + 20)
            .Select(ordinal => $"label{ordinal:000}")
            .ToArray();

        // Act
        var listed = RemoteEmailKeywords.Create(reported);
        var reversed = RemoteEmailKeywords.Create(reported.Reverse());

        // Assert
        Assert.Equal(listed, reversed);
    }

    [Theory]
    [InlineData("  $Junk  ", "$JUNK")]
    [InlineData("nonjunk", "NONJUNK")]
    public void Normalized_AWritableKeyword_ProducesTheComparisonForm(string written, string expected)
    {
        // Act
        var normalized = RemoteEmailKeywords.Normalized(written);

        // Assert
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("with\tcontrol")]
    public void Normalized_AValueNoStoredKeywordCouldBe_IsNotAKeyword(string? written)
    {
        // Act
        var normalized = RemoteEmailKeywords.Normalized(written);

        // Assert
        Assert.Null(normalized);
    }

    /// <summary>The compiler's own record equality compares the list by reference, so two sets built from one server answer would be unequal without the hand-written comparison.</summary>
    [Fact]
    public void Equality_TwoSetsBuiltFromTheSameAnswer_AreOneValue()
    {
        // Act
        var first = RemoteEmailKeywords.Create(["$Junk"]);
        var second = RemoteEmailKeywords.Create(["$Junk"]);

        // Assert
        Assert.Equal(first, second);
        Assert.NotEqual(first, RemoteEmailKeywords.Create(["$Junk", "$Forwarded"]));
        Assert.NotEqual(first, RemoteEmailKeywords.None);
    }
}
