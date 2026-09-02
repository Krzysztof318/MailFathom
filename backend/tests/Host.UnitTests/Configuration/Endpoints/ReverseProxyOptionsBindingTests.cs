// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers that the reverse-proxy section binds from configuration the way composition reads it.</summary>
/// <remarks>
/// The section decides whose forwarded scheme and host this process believes, so a key that was ignored rather than
/// bound is a deployment trusting something other than what its operator wrote. Strict binding turns a misspelling
/// into a startup failure, and that is asserted here rather than assumed.
/// </remarks>
public sealed class ReverseProxyOptionsBindingTests
{
    [Fact]
    public void ReadFrom_AnEmptyConfiguration_NamesNoProxyAndBelievesOneHop()
    {
        // Arrange
        var configuration = ConfigurationFrom([]);

        // Act
        var options = ReverseProxyOptions.ReadFrom(configuration);

        // Assert
        Assert.Empty(options.TrustedProxies);
        Assert.False(options.NamesAProxy);
        Assert.Equal(1, options.MaximumForwardedHops);
    }

    [Fact]
    public void ReadFrom_AConfiguredSection_ReadsEveryDecisionCompositionActsOn()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["ReverseProxy:TrustedProxies:0"] = "10.4.0.0/16",
            ["ReverseProxy:TrustedProxies:1"] = "192.168.1.10",
            ["ReverseProxy:MaximumForwardedHops"] = "2",
        });

        // Act
        var options = ReverseProxyOptions.ReadFrom(configuration);

        // Assert
        Assert.Equal(["10.4.0.0/16", "192.168.1.10"], options.TrustedProxies);
        Assert.Equal(2, options.MaximumForwardedHops);
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>
    /// The configured list replaces the trust an unconfigured section resolves to rather than adding to it, which is
    /// why the default is not written into the property itself: the binder adds to an existing collection, so a
    /// pre-populated <c>0.0.0.0/0</c> would survive alongside the proxy an operator named and keep every peer trusted.
    /// </summary>
    [Fact]
    public void ReadFrom_AConfiguredSection_LeavesNoDefaultTrustBesideWhatWasNamed()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["ReverseProxy:TrustedProxies:0"] = "10.4.0.0/16",
        });

        // Act
        var options = ReverseProxyOptions.ReadFrom(configuration);

        // Assert
        Assert.Empty(options.ToTrustedProxyRangesCoveringEveryAddress());
    }

    /// <summary>
    /// A misspelled key that bound quietly would leave a deployment believing it had named its proxy while every peer
    /// was trusted, which reads as the endpoint working until somebody forges a scheme. The singular is the plausible
    /// mistake, because a deployment with one proxy in front is what this section is for.
    /// </summary>
    [Fact]
    public void ReadFrom_AMisspelledKey_FailsRatherThanBindingTheRest()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["ReverseProxy:TrustedProxy:0"] = "10.0.0.5",
        });

        // Act
        var readingTheSection = () => ReverseProxyOptions.ReadFrom(configuration);

        // Assert
        Assert.Throws<InvalidOperationException>(readingTheSection);
    }

    /// <summary>
    /// The section carried an <c>Enabled</c> key until the posture became unconditional. A deployment still setting it
    /// stops at startup rather than starting under a trust nobody chose, which is the whole reason the break is loud
    /// instead of ignored.
    /// </summary>
    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void ReadFrom_TheWithdrawnEnabledKey_FailsRatherThanIgnoringIt(string configuredValue)
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["ReverseProxy:Enabled"] = configuredValue,
            ["ReverseProxy:TrustedProxies:0"] = "10.0.0.5",
        });

        // Act
        var readingTheSection = () => ReverseProxyOptions.ReadFrom(configuration);

        // Assert
        Assert.Throws<InvalidOperationException>(readingTheSection);
    }

    private static IConfiguration ConfigurationFrom(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
