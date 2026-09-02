// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Accounts;

public sealed class MailAccountSelectorTests
{
    /// <summary>The text is what a caller wrote, so nothing but padding is taken off before anything matches it.</summary>
    [Theory]
    [InlineData("primary", "primary")]
    [InlineData("  primary  ", "primary")]
    [InlineData("Work mail", "Work mail")]
    public void Create_TextNamingAnAccount_KeepsItAsWrittenAndTrimsThePadding(string named, string expected)
    {
        // Arrange, Act
        var selector = MailAccountSelector.Create(named);

        // Assert
        Assert.Equal(expected, selector.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankText_ThrowsArgumentException(string named)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => MailAccountSelector.Create(named));
    }

    /// <summary>The text is echoed in the refusal a caller reads, so a newline in it would write arbitrary lines into that contract.</summary>
    [Theory]
    [InlineData("primary\nsecondary")]
    [InlineData("primary\tsecondary")]
    public void Create_TextCarryingAControlCharacter_ThrowsArgumentException(string named)
    {
        // Arrange, Act, Assert
        Assert.Throws<ArgumentException>(() => MailAccountSelector.Create(named));
    }

    [Fact]
    public void Create_TextBeyondTheLengthBound_ThrowsArgumentException()
    {
        // Arrange
        var beyondTheBound = new string('a', MailAccountSelector.MaximumLength + 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => MailAccountSelector.Create(beyondTheBound));
    }

    [Fact]
    public void Create_TextAtTheLengthBound_IsAccepted()
    {
        // Arrange
        var atTheBound = new string('a', MailAccountSelector.MaximumLength);

        // Act
        var selector = MailAccountSelector.Create(atTheBound);

        // Assert
        Assert.Equal(MailAccountSelector.MaximumLength, selector.Value.Length);
    }

    /// <summary>Code already holding an identity reaches a selector-shaped contract without round-tripping through raw text.</summary>
    [Fact]
    public void For_AnAccountIdentifier_NamesThatAccount()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");

        // Act
        var selector = MailAccountSelector.For(accountId);

        // Assert
        Assert.Equal(MailAccountSelector.Create("primary"), selector);
        Assert.Equal("primary", selector.ToString());
    }
}
