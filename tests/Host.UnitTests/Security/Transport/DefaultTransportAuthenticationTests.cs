// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Encodings.Web;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Security.Transport;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Security.Transport;

/// <summary>Asserts what the application's default authentication scheme decides, and what it decides nothing about.</summary>
/// <remarks>
/// It stands in front of every request the process serves, so what it must not do is most of what it is judged on: an
/// administrative request, an anonymous route, and a request matching nothing have to leave it without an identity, or
/// the surface that authenticates behind the rate limiter would start authenticating in front of it.
/// </remarks>
public sealed class DefaultTransportAuthenticationTests
{
    /// <summary>
    /// The one request that is pre-authenticated, recognized from the requirement the route already carries rather than
    /// from a second list of paths. A list would be a second statement of which endpoints are protected, and the two
    /// would drift the first time a route moved.
    /// </summary>
    [Fact]
    public void PreAuthenticatingSchemeFor_AProtectedMcpEndpoint_NamesTheMcpRoutingScheme()
    {
        // Arrange
        var context = RequestFor(TransportSurface.Mcp.AccessPolicyName);

        // Act
        var scheme = DefaultTransportAuthentication.PreAuthenticatingSchemeFor(context);

        // Assert
        Assert.Equal(TransportSurface.Mcp.RoutingSchemeName, scheme);
    }

    /// <summary>
    /// The administrative surface is deliberately not one of them. Its credential is judged by the authorization
    /// middleware, which runs behind the rate limiter so that a wrong key has already spent capacity — and
    /// pre-authenticating here would move that judgement in front of the limiter and turn its one shared bucket into a
    /// bucket per key.
    /// </summary>
    [Fact]
    public void PreAuthenticatingSchemeFor_AProtectedAdministrativeEndpoint_NamesNoScheme()
    {
        // Arrange
        var context = RequestFor(TransportSurface.Admin.AccessPolicyName);

        // Act
        var scheme = DefaultTransportAuthentication.PreAuthenticatingSchemeFor(context);

        // Assert
        Assert.Null(scheme);
    }

    /// <summary>An endpoint requiring nothing is every anonymous route this host serves: the attachment download, the probes, and both metadata documents.</summary>
    [Fact]
    public void PreAuthenticatingSchemeFor_AnEndpointRequiringNoPolicy_NamesNoScheme()
    {
        // Arrange
        var context = RequestFor(accessPolicyName: null);

        // Act
        var scheme = DefaultTransportAuthentication.PreAuthenticatingSchemeFor(context);

        // Assert
        Assert.Null(scheme);
    }

    /// <summary>A request matching no route carries no metadata at all, which has to answer the same way rather than throw.</summary>
    [Fact]
    public void PreAuthenticatingSchemeFor_ARequestMatchingNoEndpoint_NamesNoScheme()
    {
        // Arrange
        var context = new DefaultHttpContext();

        // Act
        var scheme = DefaultTransportAuthentication.PreAuthenticatingSchemeFor(context);

        // Assert
        Assert.Null(scheme);
    }

    /// <summary>
    /// The scheme becomes the application's default however many surfaces registered before it, which is the whole
    /// point of taking that decision in one place: it cannot be lost to registration order the way a surface's was.
    /// </summary>
    [Fact]
    public void AddDefaultTransportAuthentication_AfterBothSurfaces_IsTheApplicationDefault()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        AddApiKeyAuthentication(services, TransportSurface.Mcp);
        AddApiKeyAuthentication(services, TransportSurface.Admin);

        // Act
        services.AddDefaultTransportAuthentication();

        // Assert
        using var composed = services.BuildServiceProvider();

        Assert.Equal(
            DefaultTransportAuthentication.SchemeName,
            composed.GetRequiredService<IOptions<AuthenticationOptions>>().Value.DefaultScheme);
    }

    /// <summary>The registration is what makes the framework forward a protected MCP request, and it is the option the framework reads rather than one this repository invents.</summary>
    [Fact]
    public void AddDefaultTransportAuthentication_TheRegisteredScheme_ForwardsAProtectedMcpRequestToTheMcpSurface()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddDefaultTransportAuthentication();

        // Assert
        using var composed = services.BuildServiceProvider();
        var schemeOptions = composed
            .GetRequiredService<IOptionsMonitor<AuthenticationSchemeOptions>>()
            .Get(DefaultTransportAuthentication.SchemeName);

        Assert.NotNull(schemeOptions.ForwardDefaultSelector);
        Assert.Equal(
            TransportSurface.Mcp.RoutingSchemeName,
            schemeOptions.ForwardDefaultSelector(RequestFor(TransportSurface.Mcp.AccessPolicyName)));
        Assert.Null(schemeOptions.ForwardDefaultSelector(RequestFor(TransportSurface.Admin.AccessPolicyName)));
    }

    /// <summary>Where nothing is forwarded, the handler answers, and the answer has to be no result rather than a failure: a request that presented nothing has nothing to refuse, and one whose credential belongs to a surface judging it later must reach that surface unjudged.</summary>
    [Fact]
    public async Task HandleAuthenticateAsync_AnyRequest_AuthenticatesNobodyWithoutFailing()
    {
        // Arrange
        var handler = new DefaultTransportAuthenticationHandler(
            new StaticOptionsMonitor(new AuthenticationSchemeOptions()),
            NullLoggerFactory.Instance,
            UrlEncoder.Default);

        var scheme = new AuthenticationScheme(
            DefaultTransportAuthentication.SchemeName,
            displayName: null,
            typeof(DefaultTransportAuthenticationHandler));

        await handler.InitializeAsync(scheme, RequestFor(TransportSurface.Admin.AccessPolicyName));

        // Act
        var result = await handler.AuthenticateAsync();

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.Failure is not null);
        Assert.True(result.None);
    }

    /// <summary>Composes a request whose selected endpoint carries one authorization requirement, or none.</summary>
    private static DefaultHttpContext RequestFor(string? accessPolicyName)
    {
        var context = new DefaultHttpContext();

        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            accessPolicyName is null
                ? new EndpointMetadataCollection()
                : new EndpointMetadataCollection(new AuthorizeAttribute(accessPolicyName)),
            "a route"));

        return context;
    }

    private static void AddApiKeyAuthentication(IServiceCollection services, TransportSurface surface) =>
        services.AddTransportAuthentication(
            surface,
            [
                new TransportAuthenticationOptions
                {
                    ApiKey = new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:not-a-real-key" },
                },
            ],
            surface.ApiKeySchemeName);

    /// <summary>Hands the handler the one options instance it is built with, which is all the framework's monitor does for a scheme nothing reconfigures.</summary>
    private sealed class StaticOptionsMonitor : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        internal StaticOptionsMonitor(AuthenticationSchemeOptions schemeOptions) => this.CurrentValue = schemeOptions;

        /// <inheritdoc />
        public AuthenticationSchemeOptions CurrentValue { get; }

        /// <inheritdoc />
        public AuthenticationSchemeOptions Get(string? name) => this.CurrentValue;

        /// <inheritdoc />
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }
}
