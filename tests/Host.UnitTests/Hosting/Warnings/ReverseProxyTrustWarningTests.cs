// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told when their configured proxy trust covers every address.</summary>
/// <remarks>
/// Its silence is as much a contract as its text. A warning that also fired for an ordinary range would be one more
/// line an operator learns to scroll past, and the posture it exists for would then travel with the noise.
/// </remarks>
public sealed class ReverseProxyTrustWarningTests
{
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public async Task StartAsync_ARangeCoveringEveryAddress_NamesWhatTheDeploymentGaveUp(string trustedProxy)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(TrustingProxies(trustedProxy), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("covers every address", record.Message, StringComparison.Ordinal);
        Assert.Contains("without transport encryption", record.Message, StringComparison.Ordinal);
        Assert.Equal(trustedProxy, Assert.Contains("TrustedRanges", record.Properties));
    }

    /// <summary>Both families named at once are one posture rather than two, so they are reported on one line.</summary>
    [Fact]
    public async Task StartAsync_EveryAddressOfBothFamilies_ReportsThemTogether()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(TrustingProxies("0.0.0.0/0", "::/0"), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("0.0.0.0/0, ::/0", Assert.Contains("TrustedRanges", record.Properties));
    }

    /// <summary>A judgement about how wide is too wide belongs to an operator who knows their network, not to a line in a log.</summary>
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("10.0.0.0/8")]
    [InlineData("2001:db8::/32")]
    public async Task StartAsync_ARangeNamingProxiesThisDeploymentCouldHaveMeant_SaysNothing(string trustedProxy)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(TrustingProxies(trustedProxy), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A section nobody enabled reads no forwarded header from anybody, however its list happens to read.</summary>
    [Fact]
    public async Task StartAsync_DisabledSection_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = new ReverseProxyOptions();
        settings.TrustedProxies.Add("0.0.0.0/0");
        var warning = WarningFor(settings, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task StopAsync_AnyPosture_CompletesWithoutSayingAnythingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(TrustingProxies("0.0.0.0/0"), logs);

        // Act
        await warning.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static ReverseProxyOptions TrustingProxies(params string[] trustedProxies)
    {
        var settings = new ReverseProxyOptions { Enabled = true };

        foreach (var trustedProxy in trustedProxies)
        {
            settings.TrustedProxies.Add(trustedProxy);
        }

        return settings;
    }

    private static ReverseProxyTrustWarning WarningFor(ReverseProxyOptions settings, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new ReverseProxyTrustWarning(
            Options.Create(settings),
            loggerFactory.CreateLogger<ReverseProxyTrustWarning>());
    }
}
