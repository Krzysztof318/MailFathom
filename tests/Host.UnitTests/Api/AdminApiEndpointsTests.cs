// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Contacts;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Host.Api;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Security.Endpoints;
using MailFathom.Host.Security.Transport;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what admits a caller to the administrative routes, which is the group rather than any route.</summary>
/// <remarks>
/// <para>
/// The surface's protection is structural: <see cref="AdminApiEndpoints.MapAdminApi" /> maps every route into one
/// group, and the composition root attaches the requirement to that group. A route mapped outside it, or mapped after
/// the requirement was attached, would be served to anybody who can reach the address — and for the write route that
/// means placing a mailbox owner's long-lived credential. So what is asserted here is that the routes the mapping
/// produces are all inside the group, whichever routes those come to be.
/// </para>
/// <para>
/// This builds the route group and reads the metadata the endpoints carry. It starts no server and issues no request,
/// which is the boundary <c>tests/AGENTS.md</c> draws for this project: whether the authorization middleware then
/// enforces that metadata is the framework's contract rather than this repository's, and proving it end to end is what
/// a composed-host test would add.
/// </para>
/// </remarks>
public sealed class AdminApiEndpointsTests
{
    /// <summary>What the use cases behind the routes consult. Nothing here issues a request, so it is never asked.</summary>
    private static readonly AccessAuthorization Authorization = AccessAuthorizations.ForPrincipal(principal: null);

    /// <summary>
    /// The regression this exists for: a route added to the file but mapped outside the group would compile, answer,
    /// and be unauthenticated, and every other test here would still pass.
    /// </summary>
    [Fact]
    public void MapAdminApi_WithTheGroupRequirementAttached_LeavesNoRouteWithoutIt()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapAdminApi().RequireAuthorization(TransportSurface.Admin.AccessPolicyName);

        // Assert
        var mapped = endpoints.Materialize();
        Assert.NotEmpty(mapped);
        Assert.All(
            mapped,
            endpoint => Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                requirement => requirement.Policy == TransportSurface.Admin.AccessPolicyName));
    }

    /// <summary>
    /// The control for the test above. Without it, an assertion that every route carries the requirement would pass
    /// just as happily against a reader that never finds the metadata at all, and the protection it claims to check
    /// would be unobserved rather than present.
    /// </summary>
    [Fact]
    public void MapAdminApi_WithNoGroupRequirementAttached_LeavesEveryRouteWithoutOne()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapAdminApi();

        // Assert
        var mapped = endpoints.Materialize();
        Assert.NotEmpty(mapped);
        Assert.All(mapped, endpoint => Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()));
    }

    /// <summary>Every route is the group's, and the write route is the one whose absence from it would matter most.</summary>
    [Fact]
    public void MapAdminApi_Always_ServesEveryRouteBeneathTheAdministrativePrefix()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapAdminApi();

        // Assert
        var routes = endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Select(endpoint => $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}")
            .Order(StringComparer.Ordinal);

        // The activation path, the rule-run path, the classification-run path, and the rewind path each appear twice,
        // because each is one resource read with a get and performed with a post, and both verbs are mapped separately.
        // The contact-book paths appear twice and three times for the same reason: the book is listed and written to at
        // one path, and one contact is read, amended, and erased at another.
        Assert.Equal(
            [
                $"{AdminEndpointOptions.RoutePrefix}{MailAnsweringAuditEndpoint.Route}",
                $"{AdminEndpointOptions.RoutePrefix}{ContactEndpoints.ContactsRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{ContactEndpoints.ContactsRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{ContactEndpoints.ContactByAddressRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{ContactEndpoints.ContactRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{ContactEndpoints.ContactRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{ContactEndpoints.ContactRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{ContactEndpoints.ContactExportRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{ContactEndpoints.ContactPromotionRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{EmbeddingProfileEndpoints.StatusRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{EmbeddingProfileEndpoints.ActivationRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{EmbeddingProfileEndpoints.ActivationRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{EmbeddingProfileEndpoints.ReindexCancellationRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailFolderErasureEndpoint.ErasureRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{JobDeadLetterEndpoints.DeadLettersRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{JobDeadLetterEndpoints.DropRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{JobDeadLetterEndpoints.RetryRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailboxMutationAuditEndpoint.Route}",
                $"{AdminEndpointOptions.RoutePrefix}{MailboxMaintenanceEndpoints.RederivationRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailboxRefreshTokenEndpoint.Route}",
                $"{AdminEndpointOptions.RoutePrefix}{MailboxMaintenanceEndpoints.RewindRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailboxMaintenanceEndpoints.RewindRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailboxSynchronizationStatusEndpoint.StatusRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailRuleEndpoints.RulesRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailRuleEndpoints.HistoryRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailRuleEndpoints.RunsRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{MailRuleEndpoints.RunsRoute}",
                $"{AdminEndpointOptions.RoutePrefix}/session",
                $"{AdminEndpointOptions.RoutePrefix}{SpamClassificationEndpoints.ClassificationsRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{SpamClassificationEndpoints.RunsRoute}",
                $"{AdminEndpointOptions.RoutePrefix}{SpamClassificationEndpoints.RunsRoute}",
            ],
            routes);
    }

    /// <summary>
    /// The regression this exists for: a route added without deciding what reaching it requires. The decision is
    /// metadata the route carries, so forgetting it compiles and answers — and the filter over the group refuses such a
    /// route rather than serving it, which is what this asserts is never something an operator has to discover.
    /// </summary>
    [Fact]
    public void MapAdminApi_Always_LeavesNoRouteWithoutAPublishedDecision()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapAdminApi();

        // Assert
        var mapped = endpoints.Materialize();
        Assert.NotEmpty(mapped);
        Assert.All(mapped, endpoint => Assert.NotNull(endpoint.Metadata.GetMetadata<AdminRoutePermission>()));
    }

    /// <summary>
    /// The allocation itself, pinned. Which permission a route is published under is a decision about what an operator
    /// can provision separately, so moving one is a change to the deployment contract rather than a refactoring, and
    /// this is what makes it one somebody has to write down.
    /// </summary>
    [Fact]
    public void MapAdminApi_Always_PublishesEachRouteUnderThePermissionItWasAllocated()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();
        var prefix = AdminEndpointOptions.RoutePrefix;

        // Act
        endpoints.MapAdminApi();

        // Assert
        Assert.Equal(
            new[]
            {
                $"GET {prefix}{AdminApiEndpoints.SessionRoute} -> none",
                $"POST {prefix}{MailboxRefreshTokenEndpoint.Route} -> {MailFathomPermission.AdminCredentialsWrite.Name}",
                $"GET {prefix}{MailboxSynchronizationStatusEndpoint.StatusRoute} -> {MailFathomPermission.AdminRead.Name}",
                $"GET {prefix}{MailboxMaintenanceEndpoints.RewindRoute} -> {MailFathomPermission.AdminRead.Name}",
                $"POST {prefix}{MailboxMaintenanceEndpoints.RewindRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"POST {prefix}{MailboxMaintenanceEndpoints.RederivationRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"GET {prefix}{MailboxMutationAuditEndpoint.Route} -> {MailFathomPermission.AdminAuditRead.Name}",
                $"GET {prefix}{MailAnsweringAuditEndpoint.Route} -> {MailFathomPermission.AdminAuditRead.Name}",
                $"GET {prefix}{EmbeddingProfileEndpoints.StatusRoute} -> {MailFathomPermission.AdminRead.Name}",
                $"GET {prefix}{EmbeddingProfileEndpoints.ActivationRoute} -> {MailFathomPermission.AdminRead.Name}",
                $"POST {prefix}{EmbeddingProfileEndpoints.ActivationRoute} -> {MailFathomPermission.AdminSpend.Name}",
                $"POST {prefix}{EmbeddingProfileEndpoints.ReindexCancellationRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"GET {prefix}{MailRuleEndpoints.RulesRoute} -> {MailFathomPermission.AdminRead.Name}",
                $"GET {prefix}{MailRuleEndpoints.RunsRoute} -> {MailFathomPermission.AdminRead.Name}",
                $"POST {prefix}{MailRuleEndpoints.RunsRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"GET {prefix}{MailRuleEndpoints.HistoryRoute} -> {MailFathomPermission.AdminAuditRead.Name}",
                $"GET {prefix}{SpamClassificationEndpoints.RunsRoute} -> {MailFathomPermission.AdminRead.Name}",
                $"POST {prefix}{SpamClassificationEndpoints.RunsRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"GET {prefix}{SpamClassificationEndpoints.ClassificationsRoute} -> {MailFathomPermission.AdminAuditRead.Name}",
                $"GET {prefix}{JobDeadLetterEndpoints.DeadLettersRoute} -> {MailFathomPermission.AdminRead.Name}",
                $"POST {prefix}{JobDeadLetterEndpoints.RetryRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"POST {prefix}{JobDeadLetterEndpoints.DropRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"POST {prefix}{MailFolderErasureEndpoint.ErasureRoute} -> {MailFathomPermission.AdminErase.Name}",
                $"GET {prefix}{ContactEndpoints.ContactsRoute} -> {MailFathomPermission.AdminAuditRead.Name}",
                $"POST {prefix}{ContactEndpoints.ContactsRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"GET {prefix}{ContactEndpoints.ContactByAddressRoute} -> {MailFathomPermission.AdminAuditRead.Name}",
                $"GET {prefix}{ContactEndpoints.ContactRoute} -> {MailFathomPermission.AdminAuditRead.Name}",
                $"PUT {prefix}{ContactEndpoints.ContactRoute} -> {MailFathomPermission.AdminOperate.Name}",
                $"DELETE {prefix}{ContactEndpoints.ContactRoute} -> {MailFathomPermission.AdminErase.Name}",
                $"GET {prefix}{ContactEndpoints.ContactExportRoute} -> {MailFathomPermission.AdminAuditRead.Name}",
                $"POST {prefix}{ContactEndpoints.ContactPromotionRoute} -> {MailFathomPermission.AdminOperate.Name}",
            }.Order(StringComparer.Ordinal),
            PublishedAllocation(endpoints).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The one route outside the model, and it is a stated decision rather than a route nobody decided about. A
    /// credential granted nothing reaches it, which is what lets <c>mfctl login</c> confirm a key before any grant is
    /// written and what keeps the session read out of every administrative grant.
    /// </summary>
    [Fact]
    public void MapAdminApi_TheSessionRoute_RequiresNoPermission()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapAdminApi();

        // Assert
        var sessionRoute = endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText?.EndsWith(AdminApiEndpoints.SessionRoute, StringComparison.Ordinal) == true);

        Assert.False(sessionRoute.Metadata.GetMetadata<AdminRoutePermission>()!.Permission.IsSpecified);
    }

    /// <summary>The write route is a post, because a get carrying a credential reaches every access log on the path.</summary>
    [Fact]
    public void MapAdminApi_TheMailboxRefreshTokenRoute_AcceptsOnlyAPost()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapAdminApi();

        // Assert
        var writeRoute = endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText?.EndsWith(MailboxRefreshTokenEndpoint.Route, StringComparison.Ordinal) == true);

        Assert.Equal(
            ["POST"],
            writeRoute.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
    }

    /// <summary>The bound on the body, which the route carries as metadata the routing pipeline reads.</summary>
    [Fact]
    public void MapAdminApi_TheMailboxRefreshTokenRoute_CarriesTheRequestBodyBound()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapAdminApi();

        // Assert
        var writeRoute = endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText?.EndsWith(MailboxRefreshTokenEndpoint.Route, StringComparison.Ordinal) == true);

        Assert.Equal(
            MailboxRefreshTokenEndpoint.MaxRequestBytes,
            writeRoute.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

    /// <summary>Reads back what each mapped route decided, as one line per verb and path.</summary>
    /// <remarks>
    /// Per verb rather than per path, because two verbs on one path are two operations and are deliberately published
    /// under different permissions: reading what a rewind would cost is not asking for one.
    /// </remarks>
    private static IEnumerable<string> PublishedAllocation(TestEndpointRouteBuilder endpoints) => endpoints
        .Materialize()
        .OfType<RouteEndpoint>()
        .SelectMany(
            endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods,
            (endpoint, method) => $"{method} /{endpoint.RoutePattern.RawText?.TrimStart('/')} -> {Describe(endpoint)}");

    /// <summary>Names what a route decided, with the route that decided on none saying so.</summary>
    private static string Describe(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<AdminRoutePermission>() is { Permission.IsSpecified: true } published
            ? published.Permission.Name
            : "none";

    /// <summary>Builds the routing seam the mapping extends, with the routing services the group needs and nothing else.</summary>
    /// <remarks>
    /// The recorder is registered because minimal API parameter inference resolves a handler's non-primitive parameters
    /// against the container while the endpoint is built, and refuses to build one it cannot place. It is never invoked
    /// here — no request is made — so a substitute with no behavior configured is the whole of what this needs.
    /// </remarks>
    private static TestEndpointRouteBuilder BuildRouteBuilder()
    {
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddLogging();
        services.AddScoped(_ => new MailboxRefreshTokenRecorder(
            Substitute.For<IMailAccountCatalog>(),
            Substitute.For<IMailboxRefreshTokenStore>(),
            Authorization));
        services.AddScoped(_ => Substitute.For<IMailAccountCatalog>());
        services.AddScoped(_ => Substitute.For<IMailboxMutationAuditEntryStore>());
        services.AddScoped(_ => Substitute.For<IAuthorizedPrincipalSource>());
        RegisterEmbeddingAdministration(services);
        RegisterContactBook(services);

        return new TestEndpointRouteBuilder(services.BuildServiceProvider());
    }

    /// <summary>Places what the embedding routes resolve, so their endpoints can be built without a container of the real thing.</summary>
    /// <remarks>
    /// The three services are sealed classes over application ports, so they are constructed here rather than
    /// substituted. None of them is invoked — no request is made — and what this arrangement exists for is that a route
    /// whose parameters cannot be placed is refused at build time and would disappear from the assertions above.
    /// </remarks>
    private static void RegisterEmbeddingAdministration(IServiceCollection services)
    {
        var generationStore = Substitute.For<IEmbeddingGenerationStore>();
        var workloadReader = Substitute.For<IEmbeddingWorkloadReader>();
        var vectorIndex = Substitute.For<IEmbeddingProfileVectorIndex>();
        var timeProvider = new FakeTimeProvider();
        var spendGate = new EmbeddingSpendGate(
            Substitute.For<IEmbeddingSpendLedger>(),
            EmbeddingSpendBudget.Unbounded,
            timeProvider);
        var retryPolicy = new OptimisticConcurrencyRetryPolicy(
            Substitute.For<IPersistenceSessionFactory>(),
            new PersistenceConcurrencyOptions(),
            timeProvider);

        var backfillSchedule = new EmbeddingBackfillSchedule(timeProvider);

        services.AddSingleton(new DeclaredEmbeddingGeometry(Identity: null));
        services.AddScoped(_ => new EmbeddingStatusReader(
            generationStore,
            workloadReader,
            spendGate,
            Substitute.For<IAiProviderHealthReader>(),
            backfillSchedule,
            Authorization));
        services.AddScoped(_ => new CountedEmbeddingActivation(
            generationStore,
            workloadReader,
            spendGate,
            new EmbeddingProfileActivation(generationStore, vectorIndex, retryPolicy, backfillSchedule),
            Authorization));
        services.AddScoped(_ => new EmbeddingReindexCancellation(
            generationStore,
            vectorIndex,
            retryPolicy,
            backfillSchedule,
            Authorization));
    }

    /// <summary>Places what the contact routes resolve, for the reason the embedding registrations exist.</summary>
    /// <remarks>
    /// The book is a sealed class over application ports, so it is constructed here rather than substituted; the
    /// directory is a port and is. Neither is invoked, because no request is made — what this exists for is that a route
    /// whose parameters cannot be placed is refused at build time and would disappear from the assertions above.
    /// </remarks>
    private static void RegisterContactBook(IServiceCollection services)
    {
        var directory = Substitute.For<IContactDirectory>();
        var timeProvider = new FakeTimeProvider();

        services.AddScoped(_ => directory);
        services.AddScoped(_ => new ContactBook(
            Substitute.For<IContactStore>(),
            directory,
            new OptimisticConcurrencyRetryPolicy(
                Substitute.For<IPersistenceSessionFactory>(),
                new PersistenceConcurrencyOptions(),
                timeProvider),
            timeProvider,
            Authorization));
    }
}
