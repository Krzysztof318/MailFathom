// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers the refusal of every way to name a listener outside the section that owns one.</summary>
/// <remarks>
/// The failure each rule here prevents is the same and is silent: an operator states a port, the process starts, and
/// the surface answers somewhere else. Kestrel ignores the URL-shaped addresses as soon as a listener is bound in code,
/// which every MailFathom surface now does, and it binds a configured endpoint beside them on a socket no section
/// describes. Both are refused at startup so the mistake arrives as a message naming the setting that replaces it.
/// </remarks>
public sealed class ExternalListenerConfigurationTests
{
    [Fact]
    public void FindConfigurationErrors_ADeploymentNamingNoListener_ReportsNothing() =>
        Assert.Empty(ExternalListenerConfiguration.FindConfigurationErrors(Configuration([])));

    /// <summary>The configuration keys are the host's own; the message names the variable an operator actually wrote.</summary>
    [Theory]
    [InlineData("urls", "ASPNETCORE_URLS")]
    [InlineData("http_ports", "ASPNETCORE_HTTP_PORTS")]
    [InlineData("https_ports", "ASPNETCORE_HTTPS_PORTS")]
    public void FindConfigurationErrors_AUrlShapedAddress_IsRefusedAndNamesItsVariable(
        string configurationKey,
        string variable)
    {
        // Act
        var error = Assert.Single(ExternalListenerConfiguration.FindConfigurationErrors(
            Configuration(new Dictionary<string, string?> { [configurationKey] = "8080" })));

        // Assert
        Assert.StartsWith(variable, error, StringComparison.Ordinal);
        Assert.Contains($"{McpEndpointOptions.SectionName}:Port", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host strips the prefix from an environment variable, so a file naming the variable itself produces a key no
    /// prefix-stripping provider ever sees. Checking only the short key would accept a listener named in a mounted
    /// ConfigMap and ignore it, which is the failure this type exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("ASPNETCORE_URLS")]
    [InlineData("ASPNETCORE_HTTP_PORTS")]
    [InlineData("ASPNETCORE_HTTPS_PORTS")]
    public void FindConfigurationErrors_AUrlShapedAddressWrittenUnderItsVariableName_IsRefused(string variable)
    {
        // Act
        var error = Assert.Single(ExternalListenerConfiguration.FindConfigurationErrors(
            Configuration(new Dictionary<string, string?> { [variable] = "8080" })));

        // Assert
        Assert.StartsWith(variable, error, StringComparison.Ordinal);
    }

    /// <summary>An environment variable populates both keys at once, and the operator set one thing.</summary>
    [Fact]
    public void FindConfigurationErrors_AUrlShapedAddressUnderBothOfItsKeys_IsReportedOnce()
    {
        // Act
        var error = Assert.Single(ExternalListenerConfiguration.FindConfigurationErrors(
            Configuration(new Dictionary<string, string?>
            {
                ["urls"] = "http://0.0.0.0:8080",
                ["ASPNETCORE_URLS"] = "http://0.0.0.0:8080",
            })));

        // Assert
        Assert.StartsWith("ASPNETCORE_URLS", error, StringComparison.Ordinal);
    }

    /// <summary>Every one of them is reported, so an operator moving a deployment reads the whole list rather than one variable per restart.</summary>
    [Fact]
    public void FindConfigurationErrors_SeveralUrlShapedAddresses_AreAllReported() =>
        Assert.Equal(
            2,
            ExternalListenerConfiguration.FindConfigurationErrors(Configuration(new Dictionary<string, string?>
            {
                ["urls"] = "http://localhost:5000",
                ["http_ports"] = "8080",
            })).Count);

    /// <summary>An empty value states no address, so it is nothing to refuse.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void FindConfigurationErrors_AUrlShapedAddressLeftEmpty_ReportsNothing(string configuredValue) =>
        Assert.Empty(ExternalListenerConfiguration.FindConfigurationErrors(
            Configuration(new Dictionary<string, string?> { ["urls"] = configuredValue })));

    [Fact]
    public void FindConfigurationErrors_AConfiguredKestrelEndpoint_IsRefused()
    {
        // Act
        var error = Assert.Single(ExternalListenerConfiguration.FindConfigurationErrors(
            Configuration(new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Public:Url"] = "http://0.0.0.0:8080",
            })));

        // Assert
        Assert.StartsWith(
            $"{ExternalListenerConfiguration.KestrelEndpointsSectionName}:Public",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>An endpoint carrying no URL binds nothing, which is what makes one carrying only defaults harmless.</summary>
    [Fact]
    public void FindConfigurationErrors_AKestrelEndpointWithoutAUrl_ReportsNothing() =>
        Assert.Empty(ExternalListenerConfiguration.FindConfigurationErrors(
            Configuration(new Dictionary<string, string?>
            {
                ["Kestrel:Endpoints:Public:Protocols"] = "Http1AndHttp2",
            })));

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
