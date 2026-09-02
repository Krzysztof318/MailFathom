// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Host.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>Covers the address clients reach this deployment at, and what it is refused for saying.</summary>
/// <remarks>
/// It is a security decision rather than a label: what is composed beneath it is fetched by something holding a
/// capability, so an address that cannot carry one — the wrong scheme, clear text off this machine, a path this process
/// does not serve — fails startup instead of producing links that resolve to nothing or to somebody else.
/// </remarks>
public sealed class DeploymentOptionsTests
{
    /// <summary>A deployment that declares nothing is a supported deployment, and composes no address.</summary>
    [Fact]
    public void Validate_UnconfiguredSection_ReportsNothingAndComposesNoAddress()
    {
        // Arrange
        var options = new DeploymentOptions();

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
        Assert.Null(options.PublicBaseAddress);
        Assert.Null(options.ComposeAddressFor("/attachments"));
    }

    /// <summary>The composed address is the declared one plus the route, ending in a slash so what follows is a further segment.</summary>
    [Fact]
    public void ComposeAddressFor_DeclaredAddress_PutsTheRouteBeneathIt()
    {
        // Arrange
        var options = new DeploymentOptions { PublicBaseAddress = new Uri("https://mail.example.test") };

        // Act
        var prefix = options.ComposeAddressFor("/attachments");

        // Assert
        Assert.Equal("https://mail.example.test/attachments/", prefix?.AbsoluteUri);
        Assert.Equal("https://mail.example.test/attachments/abc.def", new Uri(prefix!, "abc.def").AbsoluteUri);
    }

    /// <summary>An address that cannot carry a capability is refused, each fault naming itself.</summary>
    [Theory]
    [InlineData("ftp://mail.example.test", "neither http nor https")]
    [InlineData("http://mail.example.test", "clear text")]
    [InlineData("https://mail.example.test/behind/a/proxy", "carries the path")]
    [InlineData("https://mail.example.test/?tenant=1", "query or a fragment")]
    [InlineData("https://mail.example.test/#top", "query or a fragment")]
    public void Validate_AddressThatCannotCarryACapability_FailsStartup(string address, string expectedReason)
    {
        // Arrange
        var options = new DeploymentOptions { PublicBaseAddress = new Uri(address) };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Contains(
            results,
            result => result.ErrorMessage?.Contains(expectedReason, StringComparison.Ordinal) == true);
        Assert.All(
            results,
            result => Assert.Equal([nameof(DeploymentOptions.PublicBaseAddress)], result.MemberNames));
    }

    /// <summary>Clear text to this machine is what a development run serves, so it is a posture rather than an exposure.</summary>
    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void Validate_ClearTextToALoopbackHost_IsAccepted(string address)
    {
        // Arrange
        var options = new DeploymentOptions { PublicBaseAddress = new Uri(address) };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Empty(results);
    }

    /// <summary>A deployment that wrote two mistakes reads both, rather than fixing one to discover the next.</summary>
    [Fact]
    public void Validate_SeveralFaultsInOneAddress_ReportsEveryOneOfThem()
    {
        // Arrange
        var options = new DeploymentOptions
        {
            PublicBaseAddress = new Uri("http://mail.example.test/behind/a/proxy?tenant=1"),
        };

        // Act
        var results = Validate(options);

        // Assert
        Assert.Equal(3, results.Length);
    }

    private static ValidationResult[] Validate(DeploymentOptions options) =>
        [.. options.Validate(new ValidationContext(options))];
}
