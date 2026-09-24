// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Search;

/// <summary>Covers what the free-text query accepts, what it refuses, and what it never reveals.</summary>
public sealed class EmailSearchQueryTextTests
{
    [Fact]
    public void Create_Text_IsTrimmedRatherThanTakenLiterally()
    {
        // Act
        var queryText = EmailSearchQueryText.Create("  quarterly invoice  ");

        // Assert
        Assert.Equal("quarterly invoice", queryText.Value);
    }

    /// <summary>Nothing here interprets the text, so a caller's operators reach the full-text parser unchanged.</summary>
    [Theory]
    [InlineData("\"quarterly invoice\"")]
    [InlineData("invoice -draft")]
    [InlineData("invoice OR receipt")]
    [InlineData("'; DROP TABLE stored_emails; --")]
    [InlineData("100% & (rebate | refund):*")]
    public void Create_TextCarryingOperatorsOrMetacharacters_KeepsItUnchanged(string text)
    {
        // Act
        var queryText = EmailSearchQueryText.Create(text);

        // Assert
        Assert.Equal(text, queryText.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankText_IsRejected(string? text)
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() => EmailSearchQueryText.Create(text));

        // Assert
        Assert.Equal("search query", failure.FilterName);
    }

    [Fact]
    public void Create_TextLongerThanTheLimit_IsRejected()
    {
        // Arrange
        var overlyLongText = new string('a', EmailSearchQueryText.MaximumLength + 1);

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            EmailSearchQueryText.Create(overlyLongText));

        // Assert
        Assert.Equal("search query", failure.FilterName);
    }

    /// <summary>PostgreSQL text holds no zero byte, so a control character is refused rather than sent to a parameter.</summary>
    [Theory]
    [InlineData((char)0x00)]
    [InlineData((char)0x07)]
    [InlineData((char)0x1f)]
    public void Create_TextCarryingAControlCharacter_IsRejected(char controlCharacter)
    {
        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            EmailSearchQueryText.Create($"quarterly{controlCharacter}invoice"));

        // Assert
        Assert.Equal("search query", failure.FilterName);
    }

    /// <summary>Trimming already removes the whitespace controls, and nobody searches for one.</summary>
    [Fact]
    public void Create_TextWrappedInWhitespaceControls_IsAccepted()
    {
        // Act
        var queryText = EmailSearchQueryText.Create("\tinvoice\r\n");

        // Assert
        Assert.Equal("invoice", queryText.Value);
    }

    [Fact]
    public void Create_AnyText_RequiresEveryWordAsWritten()
    {
        // Act
        var queryText = EmailSearchQueryText.Create("invoice -draft");

        // Assert
        Assert.Equal(EmailSearchWordMatching.EveryWord, queryText.WordMatching);
        Assert.Equal("invoice -draft", queryText.MatchedText);
        Assert.Null(queryText.ExcludedText);
    }

    /// <summary>The lexical half of a fusion stays precise, because the semantic half already reaches what every word misses.</summary>
    [Fact]
    public void MatchedUnder_HybridRetrieval_RequiresEveryWordAsWritten()
    {
        // Act
        var queryText = EmailSearchQueryText.Create("kestrel \"quay move\" -draft").MatchedUnder(EmailSearchRetrievalMode.Hybrid);

        // Assert
        Assert.Equal(EmailSearchWordMatching.EveryWord, queryText.WordMatching);
        Assert.Equal("kestrel \"quay move\" -draft", queryText.MatchedText);
        Assert.Null(queryText.ExcludedText);
    }

    /// <summary>A phrase stays one term and an exclusion stays an exclusion, while every other word becomes enough on its own.</summary>
    [Theory]
    [InlineData("quarterly invoice", "quarterly or invoice", null)]
    [InlineData("\"Suite 310\"  INV-4827", "\"Suite 310\" or INV-4827", null)]
    [InlineData("Wrzosowa OR Wrzosową or Wrzosowej", "Wrzosowa or Wrzosową or Wrzosowej", null)]
    [InlineData("invoice -draft", "invoice", "draft")]
    [InlineData("kestrel -\"quay move\" -draft parking", "kestrel or parking", "\"quay move\" or draft")]
    [InlineData("invoice \"unclosed phrase", "invoice or \"unclosed phrase", null)]
    [InlineData("invoice - \"\" -", "invoice", null)]
    public void MatchedUnder_LexicalRetrieval_MatchesAnyWordOrPhraseAndExcludesSeparately(
        string text,
        string expectedMatchedText,
        string? expectedExcludedText)
    {
        // Act
        var queryText = EmailSearchQueryText.Create(text).MatchedUnder(EmailSearchRetrievalMode.Lexical);

        // Assert
        Assert.Equal(EmailSearchWordMatching.AnyWord, queryText.WordMatching);
        Assert.Equal(expectedMatchedText, queryText.MatchedText);
        Assert.Equal(expectedExcludedText, queryText.ExcludedText);
        Assert.Equal(text, queryText.Value);
    }

    /// <summary>A query that only excludes has nothing to match any of, so it is matched as written rather than as nothing.</summary>
    [Fact]
    public void MatchedUnder_LexicalRetrievalOfAQueryThatOnlyExcludes_MatchesItAsWritten()
    {
        // Act
        var queryText = EmailSearchQueryText.Create("-draft OR").MatchedUnder(EmailSearchRetrievalMode.Lexical);

        // Assert
        Assert.Equal("-draft OR", queryText.MatchedText);
        Assert.Null(queryText.ExcludedText);
    }

    /// <summary>What somebody searches their own mailbox for is personal data, so nothing that prints the value shows it.</summary>
    [Fact]
    public void ToString_AnyQuery_RevealsNoneOfTheText()
    {
        // Act
        var described = EmailSearchQueryText.Create("severance agreement").ToString();

        // Assert
        Assert.DoesNotContain("severance", described, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("19", described, StringComparison.Ordinal);
    }

    /// <summary>The failure names the filter and its limit, never the text that was refused.</summary>
    [Fact]
    public void Create_RejectedText_IsNotRepeatedInTheFailureMessage()
    {
        // Arrange
        var secretText = new string('s', EmailSearchQueryText.MaximumLength + 1);

        // Act
        var failure = Assert.Throws<MailboxQueryFilterInvalidException>(() =>
            EmailSearchQueryText.Create(secretText));

        // Assert
        Assert.DoesNotContain(secretText, failure.Message, StringComparison.Ordinal);
    }
}
