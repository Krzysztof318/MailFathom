// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Persistence;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers what a deployment may say about attachment download links, and what it is refused for saying.</summary>
/// <remarks>
/// Both settings are security decisions rather than tuning. The address decides where a capability this deployment
/// signed is sent, and the lifetime is the whole of what makes a leaked one worth little, so an unusable value fails
/// startup instead of being clamped into something plausible.
/// </remarks>
public sealed class AttachmentDownloadOptionsTests
{
    /// <summary>A deployment that configures nothing issues no links and is not a misconfiguration.</summary>
    [Fact]
    public void FindConfigurationErrors_UnconfiguredBlock_ReportsNothingAndDeclaresNoAddress()
    {
        // Arrange
        var options = new AttachmentDownloadOptions();

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
        Assert.Null(options.PublicBaseAddress);
        Assert.Equal(TimeSpan.FromMinutes(10), options.LinkLifetime);
        Assert.Null(options.ComposeDownloadAddressPrefix("/attachments"));
    }

    /// <summary>The composed prefix is the declared address plus the route, ending in a slash so a capability is a further segment.</summary>
    [Fact]
    public void ComposeDownloadAddressPrefix_DeclaredAddress_PutsTheRouteBeneathIt()
    {
        // Arrange
        var options = new AttachmentDownloadOptions
        {
            PublicBaseAddress = new Uri("https://mail.example.test"),
        };

        // Act
        var prefix = options.ComposeDownloadAddressPrefix("/attachments");

        // Assert
        Assert.Equal("https://mail.example.test/attachments/", prefix?.AbsoluteUri);
        Assert.Equal(
            "https://mail.example.test/attachments/abc.def",
            new Uri(prefix!, "abc.def").AbsoluteUri);
    }

    /// <summary>
    /// A window outside the permitted range fails startup rather than being clamped, because both ends of it are
    /// answers only the product can give: below the floor nothing could be redeemed, and above the ceiling a URL stops
    /// being a capability and becomes a credential nobody can revoke.
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(1801)]
    [InlineData(86400)]
    [InlineData(0)]
    [InlineData(-60)]
    public void FindConfigurationErrors_LifetimeOutsideThePermittedRange_FailsStartupNamingTheSetting(int seconds)
    {
        // Arrange
        var options = new AttachmentDownloadOptions { LinkLifetime = TimeSpan.FromSeconds(seconds) };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var error = Assert.Single(errors);
        Assert.Contains("EmailContent:AttachmentDownloads:LinkLifetime", error, StringComparison.Ordinal);
    }

    /// <summary>Both ends of the range are permitted values rather than exclusive bounds.</summary>
    [Theory]
    [InlineData(60)]
    [InlineData(600)]
    [InlineData(1800)]
    public void FindConfigurationErrors_LifetimeWithinThePermittedRange_IsAccepted(int seconds)
    {
        // Arrange
        var options = new AttachmentDownloadOptions { LinkLifetime = TimeSpan.FromSeconds(seconds) };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>An address that cannot address this deployment's download route is refused, each fault naming itself.</summary>
    [Theory]
    [InlineData("ftp://mail.example.test", "neither http nor https")]
    [InlineData("http://mail.example.test", "clear text")]
    [InlineData("https://mail.example.test/behind/a/proxy", "carries the path")]
    [InlineData("https://mail.example.test/?tenant=1", "query or a fragment")]
    [InlineData("https://mail.example.test/#top", "query or a fragment")]
    public void FindConfigurationErrors_AddressThatCannotAddressTheRoute_FailsStartup(
        string address,
        string expectedReason)
    {
        // Arrange
        var options = new AttachmentDownloadOptions { PublicBaseAddress = new Uri(address) };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Contains(
            errors,
            error => error.Contains(expectedReason, StringComparison.Ordinal));
    }

    /// <summary>Clear text to this machine is what a development run serves, so it is a posture rather than an exposure.</summary>
    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    public void FindConfigurationErrors_ClearTextToALoopbackHost_IsAccepted(string address)
    {
        // Arrange
        var options = new AttachmentDownloadOptions { PublicBaseAddress = new Uri(address) };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A deployment that wrote two mistakes reads both, rather than fixing one to discover the next.</summary>
    [Fact]
    public void FindConfigurationErrors_SeveralFaultySettings_ReportsEveryOneOfThem()
    {
        // Arrange
        var options = new AttachmentDownloadOptions
        {
            PublicBaseAddress = new Uri("http://mail.example.test/behind/a/proxy"),
            LinkLifetime = TimeSpan.FromHours(4),
        };

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Equal(3, errors.Count);
    }
}
