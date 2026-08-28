// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Mail;

namespace MailFathom.Client.UnitTests.Backend.Mail;

/// <summary>When a reader is warned before a link is followed, and what the pane names as the place it goes.</summary>
/// <remarks>
/// Three separate things warrant the warning and each is sufficient on its own, which is what this pins: a verdict this
/// build cannot read must not be quieter than one it can, and a host written in two spellings is a finding whatever the
/// verdict said about the text.
/// </remarks>
public sealed class MailBodyLinkTests
{
    /// <summary>Each of the three findings warns on its own, and an ordinary link warns about nothing.</summary>
    [Theory]
    [InlineData(MailBodyLinkDeception.None, null, false)]
    [InlineData(MailBodyLinkDeception.NotApplicable, null, false)]
    [InlineData(MailBodyLinkDeception.DisplayedHostDiffers, null, true)]
    [InlineData(MailBodyLinkDeception.Unrecognized, null, true)]
    [InlineData(MailBodyLinkDeception.None, "xn--80ak6aa92e.test", true)]
    [InlineData(MailBodyLinkDeception.NotApplicable, "xn--80ak6aa92e.test", true)]
    public void IsWorthWarningAbout_AVerdictAndASpelling_WarnsOnEitherFinding(
        MailBodyLinkDeception deception,
        string? asciiHost,
        bool expected)
    {
        // Arrange
        var link = new MailBodyLink("https://example.test/a", "example.test", asciiHost, deception);

        // Act, Assert
        Assert.Equal(expected, link.IsWorthWarningAbout);
    }

    /// <summary>The host is what a reader judges, so it is what the pane names where there is one.</summary>
    [Fact]
    public void Place_ALinkWithAHost_NamesTheHostRatherThanTheWholeTarget()
    {
        // Arrange
        var link = new MailBodyLink(
            "https://example.test/a/very/long/path",
            "example.test",
            AsciiHost: null,
            MailBodyLinkDeception.None);

        // Act, Assert
        Assert.Equal("example.test", link.Place);
    }

    /// <summary>A target with no host is named in full, because its address is the useful thing to show.</summary>
    [Fact]
    public void Place_ALinkWithNoHost_NamesTheWholeTarget()
    {
        // Arrange
        var link = new MailBodyLink(
            "mailto:someone@example.test",
            Host: null,
            AsciiHost: null,
            MailBodyLinkDeception.NotApplicable);

        // Act, Assert
        Assert.Equal("mailto:someone@example.test", link.Place);
    }
}
