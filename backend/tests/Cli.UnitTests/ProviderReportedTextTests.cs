// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Credentials.SecretStores;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what survives of a message the process holding the keyring wrote.</summary>
/// <remarks>
/// The message reaches a terminal through three sentences the command prints — the failure a command exits with, what
/// <c>login</c> says about where the credential ended up, and what <c>logout</c> says it could not clear — and the
/// process that wrote it is whichever one claimed the Secret Service name on the session bus, which need not be the
/// desktop's own keyring. What these assert is the boundary: the diagnostic survives, and the ability to move a cursor,
/// break a line, or bury the rest of the output does not.
/// </remarks>
public sealed class ProviderReportedTextTests
{
    [Fact]
    public void Sanitize_AnOrdinaryMessage_KeepsItWordForWord()
    {
        // Arrange
        const string reported = "The name org.freedesktop.secrets was not provided by any .service files";

        // Act
        var reduced = ProviderReportedText.Sanitize(reported);

        // Assert
        Assert.Equal(reported, reduced);
    }

    /// <summary>A terminal acts on an escape sequence rather than printing it, so the escape itself is what must not survive.</summary>
    [Fact]
    public void Sanitize_TextCarryingAnEscapeSequence_KeepsTheTextAndNotTheEscape()
    {
        // Arrange, Act
        var reduced = ProviderReportedText.Sanitize("\u001B[2Jlocked");

        // Assert
        Assert.Equal("[2Jlocked", reduced);
    }

    /// <summary>A message that broke into lines would let everything after the first read as a record of its own.</summary>
    [Theory]
    [InlineData("locked\nSigned in as root")]
    [InlineData("locked\r\nSigned in as root")]
    [InlineData("locked\u2028Signed in as root")]
    [InlineData("locked \t Signed in as root")]
    public void Sanitize_TextCarryingALineBreak_ReducesToOneLine(string reported)
    {
        // Arrange, Act
        var reduced = ProviderReportedText.Sanitize(reported);

        // Assert
        Assert.Equal("locked Signed in as root", reduced);
    }

    /// <summary>Nothing upstream bounds the length, and the failure this is embedded in has to stay one thing an operator reads.</summary>
    [Fact]
    public void Sanitize_AMessageOfAnyLength_KeepsAtMostTwoHundredCharacters()
    {
        // Arrange
        var reported = new string('x', 5000);

        // Act
        var reduced = ProviderReportedText.Sanitize(reported);

        // Assert
        Assert.Equal(200, reduced?.Length);
    }

    /// <summary>The collapsing space is text too, so a message that reaches the bound through one has reached the bound.</summary>
    /// <remarks>
    /// A ceiling tested after the append rather than before is a ceiling the whitespace branch steps over: the length
    /// arrives at the bound as that space, every later character puts it one further past, and the equality never holds
    /// again — which leaves the whole of a provider's message on an operator's terminal rather than 200 characters of
    /// it.
    /// </remarks>
    [Fact]
    public void Sanitize_AMessageThatReachesTheBoundThroughASpace_StopsThere()
    {
        // Arrange
        var reported = new string('x', 199) + " " + new string('y', 100_000);

        // Act
        var reduced = ProviderReportedText.Sanitize(reported);

        // Assert
        Assert.Equal(new string('x', 199), reduced);
    }

    /// <summary>A format character rewrites what the operator reads rather than adding to it, which is worse than bulk.</summary>
    [Theory]
    [InlineData("lock\u202Eed", "locked")]
    [InlineData("\u2066locked\u2069", "locked")]
    [InlineData("loc\u200Eke\u200Fd", "locked")]
    [InlineData("loc\u00ADked", "locked")]
    public void Sanitize_TextCarryingAFormatCharacter_DropsItRatherThanCollapsingIt(string reported, string expected)
    {
        // Arrange, Act
        var reduced = ProviderReportedText.Sanitize(reported);

        // Assert
        Assert.Equal(expected, reduced);
    }

    /// <summary>Padding a message out with blanks would otherwise push the part worth reading past the bound.</summary>
    [Fact]
    public void Sanitize_BulkWhitespaceBeforeTheMessage_DoesNotSpendTheBoundOnIt()
    {
        // Arrange
        var reported = new string(' ', 5000) + "locked";

        // Act
        var reduced = ProviderReportedText.Sanitize(reported);

        // Assert
        Assert.Equal("locked", reduced);
    }

    /// <summary>A provider that said nothing usable reports no message at all, which is what leaves the call site's own wording to say so.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("\u00A0")]
    [InlineData("\u0000\u0007")]
    public void Sanitize_NothingUsable_ReportsNoMessageAtAll(string? reported)
    {
        // Arrange, Act
        var reduced = ProviderReportedText.Sanitize(reported);

        // Assert
        Assert.Null(reduced);
    }
}
