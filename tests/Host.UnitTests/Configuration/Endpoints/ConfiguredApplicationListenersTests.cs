// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers the addresses the application listener would bind, and how they survive a second listener being opened.</summary>
/// <remarks>
/// Kestrel ignores the URL-shaped addresses as soon as any listener is bound in code, so opening the probe listener
/// would silently take the application listener away from a deployment that states its port through
/// <c>ASPNETCORE_HTTP_PORTS</c>. Restating the same strings as Kestrel endpoints is what keeps the socket, and reading
/// their ports is what lets the probe port be refused before it collides with one.
/// </remarks>
public sealed class ConfiguredApplicationListenersTests
{
    [Fact]
    public void ResolveUrls_AConfiguredUrlList_ReadsEveryAddress()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["urls"] = "http://0.0.0.0:8080;https://0.0.0.0:8443",
        });

        // Act
        var urls = ConfiguredApplicationListeners.ResolveUrls(configuration);

        // Assert
        Assert.Equal(["http://0.0.0.0:8080", "https://0.0.0.0:8443"], urls);
    }

    [Fact]
    public void ResolveUrls_ConfiguredPorts_ExpandsThemOntoEveryInterface()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["http_ports"] = "8080;8081",
            ["https_ports"] = "8443",
        });

        // Act
        var urls = ConfiguredApplicationListeners.ResolveUrls(configuration);

        // Assert
        Assert.Equal(["http://*:8080", "http://*:8081", "https://*:8443"], urls);
    }

    /// <summary>
    /// An explicit URL list is what the host itself prefers over the port lists, and restating a different precedence
    /// would bind a socket the deployment had already replaced.
    /// </summary>
    [Fact]
    public void ResolveUrls_BothAUrlListAndPorts_KeepsTheUrlList()
    {
        // Arrange
        var configuration = ConfigurationFrom(new Dictionary<string, string?>
        {
            ["urls"] = "http://127.0.0.1:5000",
            ["http_ports"] = "8080",
        });

        // Act
        var urls = ConfiguredApplicationListeners.ResolveUrls(configuration);

        // Assert
        Assert.Equal(["http://127.0.0.1:5000"], urls);
    }

    /// <summary>
    /// The framework's own fallback adds an HTTPS address whenever a development certificate happens to be installed on
    /// the machine. MailFathom never serves a listener out of one, so what is restated is the clear-text half alone.
    /// </summary>
    [Fact]
    public void ResolveUrls_NothingConfigured_RestatesTheClearTextDefaultAlone()
    {
        // Arrange
        var configuration = ConfigurationFrom([]);

        // Act
        var urls = ConfiguredApplicationListeners.ResolveUrls(configuration);

        // Assert
        Assert.Equal(["http://localhost:5000"], urls);
    }

    [Fact]
    public void AsKestrelEndpointConfiguration_TheResolvedAddresses_HandsEachOneBackToKestrelsOwnParser()
    {
        // Arrange
        string[] urls = ["http://*:8080", "https://*:8443"];

        // Act
        var endpoints = ConfiguredApplicationListeners.AsKestrelEndpointConfiguration(urls);

        // Assert
        Assert.Equal(
            [
                KeyValuePair.Create<string, string?>("Kestrel:Endpoints:MailFathomApplication0:Url", "http://*:8080"),
                KeyValuePair.Create<string, string?>("Kestrel:Endpoints:MailFathomApplication1:Url", "https://*:8443"),
            ],
            endpoints);
    }

    [Fact]
    public void ListenerPorts_TheResolvedAddresses_ReadsThePortsAProbeListenerMustNotTake()
    {
        // Arrange
        string[] urls = ["http://*:8080", "https://0.0.0.0:8443", "http://[::]:8080"];

        // Act
        var ports = ConfiguredApplicationListeners.ListenerPorts(urls);

        // Assert
        Assert.Equal([8080, 8443], ports);
    }

    /// <summary>
    /// Kestrel parses the same value moments later and reports it against the key an operator wrote. A second message
    /// here would describe the same mistake in this product's words and hide which setting the framework refused.
    /// </summary>
    [Fact]
    public void ListenerPorts_AnAddressThatIsNotOne_ContributesNoPortRatherThanAFailure()
    {
        // Arrange
        string[] urls = ["not-an-address", "http://*:8080"];

        // Act
        var ports = ConfiguredApplicationListeners.ListenerPorts(urls);

        // Assert
        Assert.Equal([8080], ports);
    }

    private static IConfiguration ConfigurationFrom(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
