// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
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

    /// <summary>Every route is served beneath the prefix a client appends to the address it was configured with.</summary>
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
            [
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailAccountsEndpoint.MailAccountsRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientApiEndpoints.SessionRoute}",
            ],
            routes);
    }

    /// <summary>
    /// The regression this exists for: a route added without deciding what reaching it requires. The decision is
    /// metadata the route carries, so forgetting it compiles and answers — and the filter over the group refuses such a
    /// route rather than serving it, which on a surface that returns mail is the difference that matters most.
    /// </summary>
    [Fact]
    public void MapClientApi_Always_LeavesNoRouteWithoutAPublishedDecision()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        var mapped = endpoints.Materialize();
        Assert.NotEmpty(mapped);
        Assert.All(mapped, endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<RoutePermission>()));
    }

    /// <summary>
    /// The allocation itself, pinned. Which permission a route is published under is a decision about what an operator
    /// can provision separately, so moving one is a change to the deployment contract rather than a refactoring.
    /// </summary>
    [Fact]
    public void MapClientApi_Always_PublishesEachRouteUnderThePermissionItWasAllocated()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();
        var prefix = ClientEndpointOptions.RoutePrefix;

        // Act
        endpoints.MapClientApi();

        // Assert
        Assert.Equal(
            [
                $"GET {prefix}{ClientMailAccountsEndpoint.MailAccountsRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientApiEndpoints.SessionRoute} -> none",
            ],
            PublishedAllocation(endpoints).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// This surface's grants are drawn from the mailbox half, so a route published under an administrative permission
    /// would be one no client credential could ever reach.
    /// </summary>
    [Fact]
    public void MapClientApi_Always_PublishesEveryRouteUnderTheMailboxHalfOfTheSet()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        Assert.All(
            endpoints.Materialize(),
            endpoint => Assert.All(
                endpoint.Metadata.GetOrderedMetadata<RoutePermission>()
                    .Where(published => published.Permission.IsSpecified),
                published => Assert.Equal(ProtectedSurface.Mail, published.Permission.Surface)));
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

    /// <summary>Reads back what each mapped route decided, as one line per verb and path.</summary>
    private static IEnumerable<string> PublishedAllocation(TestEndpointRouteBuilder endpoints) => endpoints
        .Materialize()
        .OfType<RouteEndpoint>()
        .SelectMany(
            endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods,
            (endpoint, method) => $"{method} /{endpoint.RoutePattern.RawText?.TrimStart('/')} -> {Describe(endpoint)}");

    /// <summary>Names what a route decided, with the route that decided on none saying so.</summary>
    private static string Describe(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<RoutePermission>() is { Permission.IsSpecified: true } published
            ? published.Permission.Name
            : "none";

    /// <summary>Builds the routing seam the mapping extends, with the routing services the group needs and nothing else.</summary>
    /// <remarks>
    /// The accounts route's use case is not registered, because that handler states <c>[FromServices]</c> and is
    /// therefore placed at the request rather than while the endpoint is built. Nothing here issues a request.
    /// </remarks>
    private static TestEndpointRouteBuilder BuildRouteBuilder()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();
        services.AddScoped(_ => Substitute.For<IAuthorizedPrincipalSource>());

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }
}
