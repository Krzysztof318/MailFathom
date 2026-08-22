// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what admits a caller to the client routes, which is the group rather than any route.</summary>
/// <remarks>
/// <para>
/// The surface's protection is structural, exactly as the administrative one's is: `MapClientApi` maps every route into
/// one group and the composition root attaches the requirement to that group. A route mapped outside it, or mapped
/// after the requirement was attached, would serve a mailbox to anybody who can reach the address. So what is asserted
/// here is that the routes the mapping produces are all inside the group, whichever routes those come to be — which is
/// the assertion that keeps holding as the mail-reading routes arrive.
/// </para>
/// <para>
/// This builds the route group and reads the metadata the endpoints carry. It starts no server and issues no request,
/// which is the boundary <c>backend/tests/AGENTS.md</c> draws: whether the middleware then enforces that metadata is
/// the framework's contract rather than this repository's.
/// </para>
/// </remarks>
public sealed class ClientApiEndpointsTests
{
    /// <summary>The regression this exists for: a route added to the file but mapped outside the group would compile, answer, and be unauthenticated.</summary>
    [Fact]
    public void MapClientApi_WithTheGroupRequirementAttached_LeavesNoRouteWithoutIt()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi().RequireAuthorization(TransportSurface.Client.AccessPolicyName);

        // Assert
        var mapped = endpoints.Materialize();
        Assert.NotEmpty(mapped);
        Assert.All(
            mapped,
            endpoint => Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                requirement => requirement.Policy == TransportSurface.Client.AccessPolicyName));
    }

    /// <summary>
    /// The control for the test above. Without it, an assertion that every route carries the requirement would pass
    /// just as happily against a reader that never finds the metadata at all.
    /// </summary>
    [Fact]
    public void MapClientApi_WithNoGroupRequirementAttached_LeavesEveryRouteWithoutOne()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        var mapped = endpoints.Materialize();
        Assert.NotEmpty(mapped);
        Assert.All(mapped, endpoint => Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }

    /// <summary>The CORS policy reaches the routes as endpoint metadata, which is what makes a page's preflight answerable at all.</summary>
    [Fact]
    public void MapClientApi_WithTheCorsPolicyRequired_CarriesItOnEveryRoute()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi().RequireCors(ClientTransportSecurityExtensions.CorsPolicyName);

        // Assert
        var mapped = endpoints.Materialize();
        Assert.NotEmpty(mapped);
        Assert.All(
            mapped,
            endpoint => Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IEnableCorsAttribute>(),
                policy => policy.PolicyName == ClientTransportSecurityExtensions.CorsPolicyName));
    }

    /// <summary>Every route is served beneath the prefix a client appends to the address it was configured with, and today there is exactly one.</summary>
    [Fact]
    public void MapClientApi_Always_ServesEveryRouteBeneathTheClientPrefix()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        var routes = endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Select(endpoint => $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            [$"{ClientEndpointOptions.RoutePrefix}{ClientApiEndpoints.SessionRoute}"],
            routes);
    }

    /// <summary>A read is all this surface publishes, so anything else arriving on it is a route somebody added without deciding what it costs.</summary>
    [Fact]
    public void MapClientApi_Always_PublishesReadsAndNothingElse()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        Assert.All(
            endpoints.Materialize(),
            endpoint => Assert.Equal(
                ["GET"],
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods));
    }

    private static TestEndpointRouteBuilder BuildRouteBuilder()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();
        services.AddScoped(_ => Substitute.For<IAuthorizedPrincipalSource>());

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }
}
