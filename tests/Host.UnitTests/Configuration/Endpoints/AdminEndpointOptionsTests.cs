// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers what a deployment gets for configuring the administrative endpoint, and what it is refused.</summary>
/// <remarks>
/// This section decides whether a network can administer the service, so the defaults matter as much as the validation:
/// what a deployment that writes nothing gets is the answer most deployments will live with.
/// </remarks>
public sealed class AdminEndpointOptionsTests
{
    [Fact]
    public void ReadFrom_ADeploymentThatConfiguresNothing_ServesNoAdministrativeSurface()
    {
        // Act
        var settings = AdminEndpointOptions.ReadFrom(new ConfigurationBuilder().Build());

        // Assert: off, and requiring no credential is then irrelevant because nothing is served.
        Assert.False(settings.Enabled);
        Assert.False(settings.RequiresAuthentication);
        Assert.Empty(settings.FindConfigurationErrors([]));
    }

    /// <summary>A misspelled key must fail rather than bind a default, or an operator reads their own configuration as though it took effect.</summary>
    [Fact]
    public void ReadFrom_AMisspelledKey_FailsRatherThanServingAPostureNobodySelected()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabeld"] = "true",
        });

        // Act, Assert
        Assert.ThrowsAny<InvalidOperationException>(() => AdminEndpointOptions.ReadFrom(configuration));
    }

    [Fact]
    public void ReadFrom_TheAuthenticationSet_BindsTheSameSpellingsTheMcpEndpointTakes()
    {
        // Arrange
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AdminEndpoint:Enabled"] = "true",
            ["AdminEndpoint:Authentication"] = "ApiKey, OAuth",
        });

        // Act
        var settings = AdminEndpointOptions.ReadFrom(configuration);

        // Assert
        Assert.True(settings.AllowsApiKey);
        Assert.True(settings.AllowsOAuth);
    }

    [Fact]
    public void FindConfigurationErrors_ApiKeyAuthenticationWithNoKey_IsRefused()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.Authentication = TransportAuthenticationMethods.ApiKey;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains("no key is configured", StringComparison.Ordinal));
    }

    /// <summary>Settings nothing reads are a deployment believing it is protected, which is worse than one that knows it is not.</summary>
    [Fact]
    public void FindConfigurationErrors_KeysConfiguredWhileApiKeyAuthenticationIsOff_IsRefused()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.ApiKeys.Add(new ConfiguredSecret { Name = "workstation", SecretReference = "systemd-credential:admin-key" });

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains("none of them is checked", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_AnAuthenticationValueNamingNoMethod_IsRefusedRatherThanOpeningTheEndpoint()
    {
        // Arrange: the binder accepts any number for an enum, and this one answers 'no' to every method check.
        var settings = EnabledEndpoint();
        settings.Authentication = (TransportAuthenticationMethods)4;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains("names no authentication method", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two endpoints on one port would leave whichever bound first deciding which credentials guarded the other's
    /// routes, and the operating system's own failure names a socket rather than the section that asked for it.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_APortAnotherListenerBinds_IsRefusedBeforeAnythingBinds()
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.Port = 8080;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([8080]),
            error => error.Contains("already bound by another listener", StringComparison.Ordinal));
    }

    [Fact]
    public void FindConfigurationErrors_ADisabledEndpointOnAClaimedPort_ReportsNothing()
    {
        // Arrange: nothing binds, so nothing collides.
        var settings = new AdminEndpointOptions { Port = 8080 };

        // Act, Assert
        Assert.Empty(settings.FindConfigurationErrors([8080]));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    public void FindConfigurationErrors_ABindAddressThatIsNotAnIpAddress_IsRefused(string bindAddress)
    {
        // Arrange
        var settings = EnabledEndpoint();
        settings.BindAddress = bindAddress;

        // Act, Assert
        Assert.Contains(
            settings.FindConfigurationErrors([]),
            error => error.Contains(nameof(AdminEndpointOptions.BindAddress), StringComparison.Ordinal));
    }

    /// <summary>The routes are the surface's own address, so the two sides of the wire have to agree on it exactly.</summary>
    [Fact]
    public void RoutePrefix_IsTheAddressTheCommandAppendsTo() =>
        Assert.Equal("/api/admin", AdminEndpointOptions.RoutePrefix);

    /// <summary>
    /// The two surfaces are kept apart by the names their schemes and policies carry. Sharing one would merge the two
    /// policies into whichever registration ran last, and the endpoint that lost would be guarded by settings its
    /// operator never wrote.
    /// </summary>
    [Fact]
    public void TransportSurface_TheAdministrativeSurface_SharesNoNameWithTheMcpOne()
    {
        // Act
        string[] adminNames =
        [
            TransportSurface.Admin.RoutingSchemeName,
            TransportSurface.Admin.ApiKeySchemeName,
            TransportSurface.Admin.AccessPolicyName,
            TransportSurface.Admin.OAuthSchemeNameFor("workforce"),
        ];

        string[] mcpNames =
        [
            TransportSurface.Mcp.RoutingSchemeName,
            TransportSurface.Mcp.ApiKeySchemeName,
            TransportSurface.Mcp.AccessPolicyName,
            TransportSurface.Mcp.OAuthSchemeNameFor("workforce"),
        ];

        // Assert
        Assert.Empty(adminNames.Intersect(mcpNames, StringComparer.Ordinal));
    }

    private static AdminEndpointOptions EnabledEndpoint() => new() { Enabled = true };

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
