// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Host.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what a TLS listener may claim as its name, which is the same question for every one of them.</summary>
/// <remarks>
/// The certificate is matched against DNS subject alternative names either way, so a name that could not appear in one
/// is refused during composition rather than becoming a failed load an operator reads as a provisioning mistake.
/// </remarks>
public sealed class ConfiguredDnsNameTests
{
    private const string SettingPath = "HealthEndpoints:Domain";

    [Theory]
    [InlineData("probe.example.test")]
    [InlineData("mail.example.com")]
    [InlineData("localhost")]
    [InlineData("  probe.example.test  ")]
    public void FindErrors_ADnsName_ReportsNothing(string configuredName)
    {
        // Act, Assert
        Assert.Empty(ConfiguredDnsName.FindErrors(configuredName, SettingPath));
    }

    /// <summary>Emptiness belongs to each caller, because a missing name means something different to each of them.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindErrors_NoNameAtAll_ReportsNothingAndLeavesItToTheCaller(string? configuredName)
    {
        // Act, Assert
        Assert.Empty(ConfiguredDnsName.FindErrors(configuredName, SettingPath));
    }

    /// <summary>
    /// An orchestrator dials a probe listener by address, so an IP address is the plausible mistake — and a
    /// certificate's DNS names never carry one, so it could never be matched.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("::1")]
    public void FindErrors_AnIpAddress_IsRefusedAndPointsAtTheBindAddress(string configuredName)
    {
        // Act
        var error = Assert.Single(ConfiguredDnsName.FindErrors(configuredName, SettingPath));

        // Assert
        Assert.StartsWith(SettingPath, error, StringComparison.Ordinal);
        Assert.Contains("BindAddress", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*.example.test")]
    [InlineData("not a name")]
    public void FindErrors_ANameThatIsNotDns_IsRefused(string configuredName)
    {
        // Act
        var error = Assert.Single(ConfiguredDnsName.FindErrors(configuredName, SettingPath));

        // Assert
        Assert.Contains("is not a DNS name", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindErrors_AnInternationalizedName_AsksForItsPunycodeForm()
    {
        // Act
        var error = Assert.Single(ConfiguredDnsName.FindErrors("poczta.example.test".Replace('o', 'ó'), SettingPath));

        // Assert
        Assert.Contains("punycode", error, StringComparison.Ordinal);
    }
}
