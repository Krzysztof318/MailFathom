// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Persistence;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Persistence;

/// <summary>Covers how long a deployment may keep an attachment download link redeemable.</summary>
/// <remarks>
/// The lifetime is a security decision rather than tuning: it is the whole of what makes a leaked link worth little, so
/// an unusable value fails startup instead of being clamped into something plausible. Where a link points is a fact
/// about the deployment and is covered by <c>DeploymentOptionsTests</c>.
/// </remarks>
public sealed class AttachmentDownloadOptionsTests
{
    /// <summary>A deployment that configures nothing takes the working default and is not a misconfiguration.</summary>
    [Fact]
    public void FindConfigurationErrors_UnconfiguredBlock_ReportsNothingAndTakesTheDefaultWindow()
    {
        // Arrange
        var options = new AttachmentDownloadOptions();

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
        Assert.Equal(TimeSpan.FromMinutes(10), options.LinkLifetime);
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
}
