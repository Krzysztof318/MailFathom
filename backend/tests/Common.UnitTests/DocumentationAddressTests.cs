// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Xunit;

namespace MailFathom.Common.UnitTests;

/// <summary>
/// Covers where a version's documentation is said to be. Four surfaces print what this returns and none of them can
/// check it, so an address composed wrongly reaches a reader as a page that does not exist rather than as a failure
/// anything reports.
/// </summary>
public sealed class DocumentationAddressTests
{
    [Fact]
    public void ForVersion_AReleaseVersion_NamesThatVersionsOwnDirectoryOnTheSite()
    {
        // Act
        var address = DocumentationAddress.ForVersion("0.6.0");

        // Assert
        Assert.Equal("https://krzysztof318.github.io/MailFathom/v0.6.0/", address);
    }

    /// <summary>
    /// A nightly is named after the release it will become, which the site publishes nothing for until that release
    /// exists. What it carries is the default branch, so that is what its reader is sent to.
    /// </summary>
    [Theory]
    [InlineData("0.7.0-nightly.41")]
    [InlineData("0.7.0-nightly.41-3f1c9ab")]
    [InlineData("0.0.0-unversioned")]
    public void ForVersion_APrerelease_NamesTheDefaultBranchDirectory(string version)
    {
        // Act
        var address = DocumentationAddress.ForVersion(version);

        // Assert
        Assert.Equal("https://krzysztof318.github.io/MailFathom/latest/", address);
    }

    /// <summary>The revision an SDK stamps after SemVer's plus sign says which build this is, never which pages describe it.</summary>
    [Theory]
    [InlineData("0.6.0+3f1c9abcdef", "https://krzysztof318.github.io/MailFathom/v0.6.0/")]
    [InlineData("0.7.0-nightly.41+3f1c9abcdef", "https://krzysztof318.github.io/MailFathom/latest/")]
    public void ForVersion_AVersionCarryingBuildMetadata_ReadsAsTheVersionItIsABuildOf(string version, string expected)
    {
        // Act
        var address = DocumentationAddress.ForVersion(version);

        // Assert
        Assert.Equal(expected, address);
    }

    /// <summary>
    /// One caller reads this version off a deployment across the network and prints what comes back, so nothing a
    /// deployment sends may reach the address verbatim. Composing it from the parsed numbers is what guarantees that,
    /// and a version spelled unusually is what shows the composition happened.
    /// </summary>
    [Theory]
    [InlineData("0.06.0")]
    [InlineData("  0.6.0  ")]
    public void ForVersion_AVersionSpelledUnusually_IsComposedFromItsNumbersRatherThanItsText(string version)
    {
        // Act
        var address = DocumentationAddress.ForVersion(version);

        // Assert
        Assert.Equal("https://krzysztof318.github.io/MailFathom/v0.6.0/", address);
    }

    /// <summary>
    /// An absence of evidence rather than evidence of a break, exactly as the version agreement treats the same value:
    /// a build nobody can identify gets no address, and every caller says nothing rather than offering one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("unknown-build")]
    [InlineData("0.6")]
    [InlineData("0.6.0.1")]
    [InlineData("v0.6.0")]
    [InlineData("../../elsewhere")]
    public void ForVersion_AVersionItCannotRead_NamesNoAddress(string? version)
    {
        // Act
        var address = DocumentationAddress.ForVersion(version);

        // Assert
        Assert.Null(address);
    }
}
