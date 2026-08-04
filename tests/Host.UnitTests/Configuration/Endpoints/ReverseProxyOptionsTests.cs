// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Host.Configuration.Endpoints;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers which reverse-proxy settings an operator is allowed to start the host on.</summary>
/// <remarks>
/// A forwarded scheme and host are worth what the connection carrying them is worth, so the refusals here are what
/// keep the mode from ever running on an unstated trust: enabling it without naming a proxy, and naming one that is
/// neither an address nor a network.
/// </remarks>
public sealed class ReverseProxyOptionsTests
{
    [Fact]
    public void FindConfigurationErrors_DisabledSection_FindsNothing()
    {
        // Arrange
        var settings = new ReverseProxyOptions();

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>An operator who named their proxy and left the mode off has a deployment that reads no forwarded header.</summary>
    [Fact]
    public void FindConfigurationErrors_ProxiesConfiguredWhileDisabled_RefusesTheUnreadSetting()
    {
        // Arrange
        var settings = new ReverseProxyOptions();
        settings.TrustedProxies.Add("10.0.0.5");

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains("ReverseProxy:TrustedProxies", error, StringComparison.Ordinal);
        Assert.Contains("Enabled", error, StringComparison.Ordinal);
    }

    /// <summary>The refusal that keeps the framework's loopback default, and a trust-everything workaround, both out of reach.</summary>
    [Fact]
    public void FindConfigurationErrors_EnabledWithNoProxyNamed_RefusesStartup()
    {
        // Arrange
        var settings = new ReverseProxyOptions { Enabled = true };

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains("ReverseProxy:TrustedProxies", error, StringComparison.Ordinal);
        Assert.Contains("CIDR", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("  10.0.0.5  ")]
    [InlineData("10.0.0.0/24")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:db8::/32")]
    public void FindConfigurationErrors_ProxyNamedAsAnAddressOrNetwork_FindsNothing(string trustedProxy)
    {
        // Arrange
        var settings = new ReverseProxyOptions { Enabled = true };
        settings.TrustedProxies.Add(trustedProxy);

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>A DNS name resolves to whatever answers today, and the peer is judged by the address its connection arrives from.</summary>
    [Theory]
    [InlineData("ingress.example.test")]
    [InlineData("10.0.0.999")]
    [InlineData("")]
    [InlineData("   ")]
    public void FindConfigurationErrors_ProxyNamingNoAddress_RefusesTheEntryByIndex(string trustedProxy)
    {
        // Arrange
        var settings = new ReverseProxyOptions { Enabled = true };
        settings.TrustedProxies.Add(trustedProxy);

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains("ReverseProxy:TrustedProxies:0", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.0.0.0/64")]
    [InlineData("10.0.0.0/")]
    [InlineData("ingress.example.test/24")]
    public void FindConfigurationErrors_MalformedNetwork_RefusesTheEntry(string trustedProxy)
    {
        // Arrange
        var settings = new ReverseProxyOptions { Enabled = true };
        settings.TrustedProxies.Add(trustedProxy);

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains("ReverseProxy:TrustedProxies:0", error, StringComparison.Ordinal);
        Assert.Contains("not a CIDR network", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The framework's parser masks host bits off without saying so, which would turn an operator's one proxy into
    /// every address on its subnet. The refusal names the range they would have got, so the choice stays theirs.
    /// </summary>
    [Theory]
    [InlineData("10.0.0.5/24", "10.0.0.0/24")]
    [InlineData("2001:db8::1/32", "2001:db8::/32")]
    public void FindConfigurationErrors_NetworkNamingAHostInsideIt_RefusesTheSilentWidening(
        string trustedProxy,
        string widenedNetwork)
    {
        // Arrange
        var settings = new ReverseProxyOptions { Enabled = true };
        settings.TrustedProxies.Add(trustedProxy);

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains("ReverseProxy:TrustedProxies:0", error, StringComparison.Ordinal);
        Assert.Contains(widenedNetwork, error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_SeveralFaultyEntries_ReportsEachByItsOwnIndex()
    {
        // Arrange
        var settings = new ReverseProxyOptions { Enabled = true };
        settings.TrustedProxies.Add("10.0.0.5");
        settings.TrustedProxies.Add("ingress.example.test");
        settings.TrustedProxies.Add("10.0.0.5/24");

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, error => error.Contains("TrustedProxies:1", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TrustedProxies:2", StringComparison.Ordinal));
    }

    [Fact]
    public void MaximumForwardedHops_UnconfiguredSection_BelievesOneProxy()
    {
        // Arrange
        // Act
        var settings = new ReverseProxyOptions();

        // Assert
        Assert.Equal(1, settings.MaximumForwardedHops);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FindConfigurationErrors_HopLimitBelowOne_RefusesStartup(int maximumForwardedHops)
    {
        // Arrange
        var settings = new ReverseProxyOptions { Enabled = true, MaximumForwardedHops = maximumForwardedHops };
        settings.TrustedProxies.Add("10.0.0.5");

        // Act
        var error = Assert.Single(settings.FindConfigurationErrors());

        // Assert
        Assert.Contains("ReverseProxy:MaximumForwardedHops", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToTrustedProxyAddresses_MixedEntries_TakesOnlyTheSingleAddresses()
    {
        // Arrange
        var settings = ProxiesTrusted("10.0.0.5", "10.1.0.0/16", " 2001:db8::1 ");

        // Act
        var addresses = settings.ToTrustedProxyAddresses();

        // Assert
        Assert.Equal(
            [IPAddress.Parse("10.0.0.5"), IPAddress.Parse("2001:db8::1")],
            addresses);
    }

    [Fact]
    public void ToTrustedProxyNetworks_MixedEntries_TakesOnlyTheNetworks()
    {
        // Arrange
        var settings = ProxiesTrusted("10.0.0.5", " 10.1.0.0/16 ", "2001:db8::/32");

        // Act
        var networks = settings.ToTrustedProxyNetworks();

        // Assert
        Assert.Equal(
            [IPNetwork.Parse("10.1.0.0/16"), IPNetwork.Parse("2001:db8::/32")],
            networks);
    }

    private static ReverseProxyOptions ProxiesTrusted(params string[] trustedProxies)
    {
        var settings = new ReverseProxyOptions { Enabled = true };

        foreach (var trustedProxy in trustedProxies)
        {
            settings.TrustedProxies.Add(trustedProxy);
        }

        return settings;
    }
}
