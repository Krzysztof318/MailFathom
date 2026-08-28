// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Observability;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
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
/// Most of this builds the route group and reads the metadata the endpoints carry. It starts no server, which is the
/// boundary <c>backend/tests/AGENTS.md</c> draws: whether the middleware ahead of the group then enforces what the
/// metadata says is the framework's contract rather than this repository's. The group's own filter is not that — it is
/// code in this repository, reading metadata this repository wrote — so the one test that invokes a route's request
/// delegate directly is what proves the filter is attached at all.
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
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailTimelineEndpoint.MailTimelineRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailSearchEndpoint.MailSearchRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailFoldersEndpoint.MailFoldersRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailMessageEndpoint.MailMessageRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailAttachmentEndpoint.MailAttachmentRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailBodyEndpoint.MailBodyRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOwnerRecordEndpoint.RecordRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOwnerRecordEndpoint.RecordRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOwnerRecordEndpoint.MailAccountsRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOwnerRecordEndpoint.MailAccountRemovalRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientApiEndpoints.SessionRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailThreadEndpoint.MailThreadRoute}",
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
                $"GET {prefix}{ClientMailTimelineEndpoint.MailTimelineRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailSearchEndpoint.MailSearchRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailFoldersEndpoint.MailFoldersRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailMessageEndpoint.MailMessageRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailAttachmentEndpoint.MailAttachmentRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailBodyEndpoint.MailBodyRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientOwnerRecordEndpoint.RecordRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientApiEndpoints.SessionRoute} -> none",
                $"GET {prefix}{ClientMailThreadEndpoint.MailThreadRoute} -> {MailFathomPermission.MailRead.Name}",
                $"POST {prefix}{ClientOwnerRecordEndpoint.RecordRoute} -> {MailFathomPermission.MailAccountsWrite.Name}",
                $"POST {prefix}{ClientOwnerRecordEndpoint.MailAccountsRoute} -> {MailFathomPermission.MailAccountsWrite.Name}",
                $"POST {prefix}{ClientOwnerRecordEndpoint.MailAccountRemovalRoute} -> {MailFathomPermission.MailAccountsWrite.Name}",
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

    /// <summary>
    /// A read, or a write to the caller's own record and nothing else. Everything this surface serves about mail is a
    /// <c>GET</c>, and the only thing a client changes here is what this deployment reads for the person signed in — so
    /// a write arriving under any other grant is a route somebody added without deciding what it costs.
    /// </summary>
    [Fact]
    public void MapClientApi_Always_PublishesReadsAndWritesToTheCallersOwnRecordAlone()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        Assert.All(
            endpoints.Materialize(),
            endpoint => Assert.All(
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [],
                method => Assert.True(
                    method == "GET"
                    || (method == "POST" && WritesTheCallersOwnRecord(endpoint)),
                    $"{method} {endpoint} is published as neither a read nor a write to the caller's own record.")));
    }

    /// <summary>Reports whether a route is one of the caller's own record's writes, by the grant it was published under.</summary>
    /// <remarks>The grant rather than the path, because what makes such a write admissible on an owner-facing surface is that it is separately provisioned: a path renamed keeps the claim, and a route quietly published under the read grant breaks it.</remarks>
    private static bool WritesTheCallersOwnRecord(Endpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<RoutePermission>()
            .Any(published => published.Permission == MailFathomPermission.MailAccountsWrite);

    /// <summary>
    /// The decision published on a route is worth nothing unless something reads it, and only a request establishes
    /// that the group's filter does. This is the one test here that makes one.
    /// </summary>
    [Fact]
    public async Task MapClientApi_ARouteReachedByACallerItsPermissionDoesNotAdmit_IsRefusedByTheGroupsFilter()
    {
        // Arrange
        var endpoints = BuildRouteBuilder(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailAsk));
        endpoints.MapClientApi();

        var accountsRoute = endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText?.EndsWith(
                ClientMailAccountsEndpoint.MailAccountsRoute,
                StringComparison.Ordinal) == true);

        var request = new DefaultHttpContext { RequestServices = endpoints.ServiceProvider };
        request.SetEndpoint(accountsRoute);

        // Act
        await accountsRoute.RequestDelegate!(request);

        // Assert
        Assert.Equal(StatusCodes.Status403Forbidden, request.Response.StatusCode);
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
    /// <param name="authorization">What the group's filter asks about the caller, defaulting to the one no test issues a request under.</param>
    /// <remarks>
    /// The mail-reading routes' use cases are placed at the request rather than while the endpoint is built, because
    /// those handlers state <c>[FromServices]</c>; minimal APIs bind a handler's arguments ahead of the filter chain, so
    /// the one test that issues a request needs them resolvable even though the refusal means they are never called.
    /// They are composed from substitutes for that reason — what they would answer is
    /// <c>MailAccountFreshnessReaderTests</c>'s and <c>MailFolderDirectoryReaderTests</c>'s subject, and a route refused
    /// before the handler runs asks them nothing.
    /// </remarks>
    private static TestEndpointRouteBuilder BuildRouteBuilder(AccessAuthorization? authorization = null)
    {
        var granted = authorization ?? AccessAuthorizations.ForPrincipal(principal: null);

        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();
        services.AddScoped(_ => Substitute.For<IAuthorizedPrincipalSource>());
        services.AddScoped(_ => granted);
        services.AddSingleton(Substitute.For<IAuthorizationRefusalTelemetry>());
        services.AddScoped(_ => UnreachedFreshnessReader(granted));
        services.AddScoped(_ => new MailFolderDirectoryReader(
            UnreachedFreshnessReader(granted),
            UnreachedScopeResolver(),
            Substitute.For<IStoredMailFolderReader>(),
            Substitute.For<IMailFolderMappingReader>()));

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    /// <summary>Composes the mailbox reading a refused request never reaches, from substitutes that answer nothing.</summary>
    private static MailAccountFreshnessReader UnreachedFreshnessReader(AccessAuthorization granted) =>
        new(
            new MailAccountDirectoryReader(
                Substitute.For<ICallerMailAccountCatalog>(),
                Substitute.For<ISynchronizationFreshnessReader>(),
                UnreachedScopeResolver(),
                Substitute.For<IMailboxReadTelemetry>(),
                granted),
            new MailSynchronizationRunLedger(new FakeTimeProvider()));

    private static MailboxScopeResolver UnreachedScopeResolver() =>
        new(
            Substitute.For<ICallerMailAccountCatalog>(),
            Substitute.For<IMailFolderParticipationReader>(),
            Substitute.For<IJunkMailFolderCatalog>(),
            new MailFolderReferenceResolver(Substitute.For<IMailFolderMappingReader>()));
}
