// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Host.Configuration.Endpoints;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers which reverse-proxy settings an operator is allowed to start the host on, and what an unnamed proxy resolves to.</summary>
/// <remarks>
/// A forwarded scheme and host are worth what the connection carrying them is worth, so what the trust resolves to is
/// the contract here: an entry that is neither an address nor a network is refused, and a section that names nothing
/// trusts every peer rather than nobody. The second is the posture the startup warning exists for, and asserting it
/// keeps the default from drifting silently.
/// </remarks>
public sealed class ReverseProxyOptionsTests
{
    /// <summary>An unconfigured section is a supported posture rather than a mistake, so it starts the host.</summary>
    [Fact]
    public void FindConfigurationErrors_ASectionNamingNoProxy_FindsNothing()
    {
        // Arrange
        var settings = new ReverseProxyOptions();

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// The default posture, asserted as trust rather than as an empty list, because that is what the middleware ends up
    /// running on. An operator who configures nothing has every peer believed, and the refusal of an access token that
    /// arrived without transport encryption reads a scheme any of them can set.
    /// </summary>
    [Fact]
    public void ToTrustedProxyNetworks_ASectionNamingNoProxy_TrustsEveryAddressOfBothFamilies()
    {
        // Arrange
        var settings = new ReverseProxyOptions();

        // Act
        var networks = settings.ToTrustedProxyNetworks();

        // Assert
        Assert.Equal(
            [IPNetwork.Parse("0.0.0.0/0"), IPNetwork.Parse("::/0")],
            networks);
        Assert.Empty(settings.ToTrustedProxyAddresses());
    }

    [Fact]
    public void ToTrustedProxyRangesCoveringEveryAddress_ASectionNamingNoProxy_ReportsBothFamilies()
    {
        // Arrange
        var settings = new ReverseProxyOptions();

        // Act
        var ranges = settings.ToTrustedProxyRangesCoveringEveryAddress();

        // Assert
        Assert.Equal(
            [IPNetwork.Parse("0.0.0.0/0"), IPNetwork.Parse("::/0")],
            ranges);
    }

    /// <summary>Naming one proxy replaces the default outright, rather than being added to it.</summary>
    [Fact]
    public void ToTrustedProxyNetworks_AProxyNamed_TrustsThatProxyAlone()
    {
        // Arrange
        var settings = ProxiesTrusted("10.4.0.0/16");

        // Act
        var networks = settings.ToTrustedProxyNetworks();

        // Assert
        Assert.Equal([IPNetwork.Parse("10.4.0.0/16")], networks);
        Assert.Empty(settings.ToTrustedProxyRangesCoveringEveryAddress());
    }

    [Fact]
    public void NamesAProxy_ASectionNamingNoProxy_ReportsThatNothingStandsInFront()
    {
        // Arrange
        var settings = new ReverseProxyOptions();

        // Act
        // Assert
        Assert.False(settings.NamesAProxy);
        Assert.True(ProxiesTrusted("10.0.0.5").NamesAProxy);
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
        var settings = ProxiesTrusted(trustedProxy);

        // Act
        var errors = settings.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>
    /// A prefix covering every address is accepted, because it is the same posture the default resolves to and writing
    /// it out is how an operator states that they meant it. What it costs is announced rather than refused.
    /// </summary>
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void FindConfigurationErrors_APrefixCoveringEveryAddress_IsAcceptedRatherThanRefused(string trustedProxy)
    {
        // Arrange
        var settings = ProxiesTrusted(trustedProxy);

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
        var settings = ProxiesTrusted(trustedProxy);

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
        var settings = ProxiesTrusted(trustedProxy);

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
        var settings = ProxiesTrusted(trustedProxy);

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
        var settings = ProxiesTrusted("10.0.0.5", "ingress.example.test", "10.0.0.5/24");

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
        var settings = ProxiesTrusted("10.0.0.5");
        settings.MaximumForwardedHops = maximumForwardedHops;

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
        var settings = new ReverseProxyOptions();

        foreach (var trustedProxy in trustedProxies)
        {
            settings.TrustedProxies.Add(trustedProxy);
        }

        return settings;
    }
}
