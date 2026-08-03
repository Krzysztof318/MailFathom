// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Security;
using MailFathom.Infrastructure.Secrets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what a surface's registration decides, and what it keeps separate from every other surface.</summary>
/// <remarks>
/// The isolation assertions are the reason this code was made to take a surface at all. Two surfaces sharing a scheme
/// name would mean one endpoint's credential silently satisfying the other's policy, which no test of either surface on
/// its own would notice.
/// </remarks>
public sealed class TransportSecurityExtensionsTests
{
    /// <summary>
    /// An access token is a reusable credential, so presenting one over plain HTTP hands it to anybody watching the
    /// network. The refusal is silent, which is what makes the answer the same challenge an unauthenticated request
    /// receives rather than a statement about what the request carried.
    /// </summary>
    [Fact]
    public async Task RefuseATokenThatArrivedWithoutTransportEncryption_APlaintextRequest_AuthenticatesNobody()
    {
        // Arrange
        var context = MessageReceivedOver("http");

        // Act
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.NotNull(context.Result);
        Assert.True(context.Result.None);
    }

    [Fact]
    public async Task RefuseATokenThatArrivedWithoutTransportEncryption_AnEncryptedRequest_LeavesTheTokenToBeValidated()
    {
        // Arrange
        var context = MessageReceivedOver("https");

        // Act
        await TransportSecurityExtensions.RefuseATokenThatArrivedWithoutTransportEncryption(context);

        // Assert
        Assert.Null(context.Result);
    }

    [Fact]
    public void AddTransportAuthentication_TheSurfacesRoutingScheme_IsTheDefaultAnAnonymousRequestReaches()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        AddApiKeyAuthentication(services, TransportSurface.Mcp);

        // Assert
        using var composed = services.BuildServiceProvider();
        var authentication = composed.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

        Assert.Equal(TransportSurface.Mcp.RoutingSchemeName, authentication.DefaultScheme);
    }

    /// <summary>
    /// The policy names one routing scheme, and that is the whole of what keeps two surfaces apart. Naming the other
    /// surface's scheme as well — or naming none, which lets the application default answer — would let a credential
    /// issued for one endpoint satisfy the other's requirement without any check noticing.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_TheSurfacesPolicy_ConsultsThatSurfacesRoutingSchemeAlone()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        AddApiKeyAuthentication(services, TransportSurface.Mcp);

        // Assert
        using var composed = services.BuildServiceProvider();
        var policy = composed
            .GetRequiredService<IOptions<AuthorizationOptions>>()
            .Value
            .GetPolicy(TransportSurface.Mcp.AccessPolicyName);

        Assert.NotNull(policy);
        Assert.Equal([TransportSurface.Mcp.RoutingSchemeName], policy.AuthenticationSchemes);
    }

    /// <summary>
    /// Every name a surface registers is composed from the surface, so no two surfaces can collide on one. A shared
    /// name would merge two policies into whichever registration ran last, and the endpoint that lost would be
    /// protected by settings its operator never wrote.
    /// </summary>
    [Fact]
    public void TransportSurface_TwoSurfaces_ShareNoSchemeOrPolicyName()
    {
        // Arrange: the second surface is built the way a further one would be, through the same public shape.
        var mcp = TransportSurface.Mcp;

        // Act
        string[] mcpNames =
        [
            mcp.RoutingSchemeName,
            mcp.ApiKeySchemeName,
            mcp.AccessPolicyName,
            mcp.OAuthSchemeNameFor("workforce"),
        ];

        // Assert: every name carries the surface, so a surface named otherwise produces a disjoint set.
        Assert.All(mcpNames, name => Assert.Contains($":{mcp.Name}:", name, StringComparison.Ordinal));
        Assert.Equal(mcpNames.Length, mcpNames.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AddTransportAuthentication_TheStructDefaultAsASurface_IsRefusedRatherThanRegisteringUnnamedSchemes()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act, Assert
        Assert.Throws<ArgumentException>(() => AddApiKeyAuthentication(services, default));
    }

    /// <summary>
    /// Each surface's registration sets the application's one default scheme, so registering two leaves the later one
    /// holding it. This is the hazard the host pins around: `UseAuthentication` populates `HttpContext.User` with the
    /// default scheme, and the MCP rate limiter partitions on that user — so a default belonging to the other surface
    /// would collapse every authenticated MCP client into the shared anonymous bucket without failing anything.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_TwoSurfaces_LeavesTheLaterRegistrationHoldingTheApplicationDefault()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        AddApiKeyAuthentication(services, TransportSurface.Mcp);
        AddApiKeyAuthentication(services, TransportSurface.Admin);

        // Assert
        using var composed = services.BuildServiceProvider();

        Assert.Equal(
            TransportSurface.Admin.RoutingSchemeName,
            composed.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
    }

    /// <summary>
    /// And the correction the host applies: stating the default explicitly wins over whichever registration ran last,
    /// so enabling the administrative endpoint cannot change which scheme an MCP request is authenticated against.
    /// </summary>
    [Fact]
    public void AddTransportAuthentication_TheDefaultSchemeStatedAfterwards_OverridesTheRegistrationOrder()
    {
        // Arrange
        var services = new ServiceCollection();
        AddApiKeyAuthentication(services, TransportSurface.Mcp);
        AddApiKeyAuthentication(services, TransportSurface.Admin);

        // Act: what the composition root does once both endpoints have registered.
        services.Configure<AuthenticationOptions>(
            authenticationOptions => authenticationOptions.DefaultScheme = TransportSurface.Mcp.RoutingSchemeName);

        // Assert
        using var composed = services.BuildServiceProvider();

        Assert.Equal(
            TransportSurface.Mcp.RoutingSchemeName,
            composed.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
    }

    private static void AddApiKeyAuthentication(IServiceCollection services, TransportSurface surface) =>
        services.AddTransportAuthentication(
            surface,
            TransportAuthenticationMethods.ApiKey,
            [new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:not-a-real-key" }],
            new OAuthValidationOptions(),
            surface.IsSpecified ? surface.ApiKeySchemeName : "unused");

    private static MessageReceivedContext MessageReceivedOver(string scheme)
    {
        var request = new DefaultHttpContext();
        request.Request.Scheme = scheme;

        return new MessageReceivedContext(
            request,
            new AuthenticationScheme(
                TransportSurface.Mcp.OAuthSchemeNameFor("workforce"),
                displayName: null,
                typeof(JwtBearerHandler)),
            new JwtBearerOptions());
    }
}
