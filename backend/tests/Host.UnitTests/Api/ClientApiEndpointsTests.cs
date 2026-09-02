// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Observability.ClientTelemetry;
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
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using OpenTelemetry.Exporter;
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
                $"{ClientEndpointOptions.RoutePrefix}{ClientDraftEndpoints.DraftsRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientDraftEndpoints.DraftsRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientDraftEndpoints.DraftRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientDraftEndpoints.DraftRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientDraftEndpoints.DraftRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientDraftEndpoints.DraftAttachmentsRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientDraftEndpoints.DraftAttachmentRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientDraftEndpoints.DraftSendRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailTimelineEndpoint.MailTimelineRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailSearchEndpoint.MailSearchRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailFoldersEndpoint.MailFoldersRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailMessageEndpoint.MailMessageRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailAttachmentEndpoint.MailAttachmentRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientMailBodyEndpoint.MailBodyRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOutboxEndpoints.OutboxRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOutboxEndpoints.OutboxCancellationRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOutboxEndpoints.OutboxRequeueRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOutboxEndpoints.OutboxSendRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientPreferencesEndpoint.PreferencesRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientPreferencesEndpoint.PreferencesRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOwnerRecordEndpoint.RecordRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOwnerRecordEndpoint.RecordRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOwnerRecordEndpoint.MailAccountsRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientOwnerRecordEndpoint.MailAccountRemovalRoute}",
                $"{ClientEndpointOptions.RoutePrefix}{ClientApiEndpoints.SessionRoute}",
                .. ClientTelemetrySignal.All
                    .Select(signal =>
                        $"{ClientEndpointOptions.RoutePrefix}{ClientTelemetryEndpoint.TelemetryRoutePrefix}{signal.Route}")
                    .Order(StringComparer.Ordinal),
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
                $"DELETE {prefix}{ClientDraftEndpoints.DraftRoute} -> {MailFathomPermission.MailDraftsWrite.Name}",
                $"DELETE {prefix}{ClientDraftEndpoints.DraftAttachmentRoute} -> {MailFathomPermission.MailDraftsWrite.Name}",
                $"GET {prefix}{ClientMailAccountsEndpoint.MailAccountsRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientDraftEndpoints.DraftsRoute} -> {MailFathomPermission.MailDraftsWrite.Name}",
                $"GET {prefix}{ClientDraftEndpoints.DraftRoute} -> {MailFathomPermission.MailDraftsWrite.Name}",
                $"GET {prefix}{ClientMailTimelineEndpoint.MailTimelineRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailSearchEndpoint.MailSearchRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailFoldersEndpoint.MailFoldersRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailMessageEndpoint.MailMessageRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailAttachmentEndpoint.MailAttachmentRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientMailBodyEndpoint.MailBodyRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientOutboxEndpoints.OutboxRoute} -> {MailFathomPermission.MailSend.Name}",
                $"GET {prefix}{ClientOutboxEndpoints.OutboxSendRoute} -> {MailFathomPermission.MailSend.Name}",
                $"GET {prefix}{ClientPreferencesEndpoint.PreferencesRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientOwnerRecordEndpoint.RecordRoute} -> {MailFathomPermission.MailRead.Name}",
                $"GET {prefix}{ClientApiEndpoints.SessionRoute} -> none",
                $"GET {prefix}{ClientMailThreadEndpoint.MailThreadRoute} -> {MailFathomPermission.MailRead.Name}",
                $"POST {prefix}{ClientDraftEndpoints.DraftsRoute} -> {MailFathomPermission.MailDraftsWrite.Name}",
                $"POST {prefix}{ClientDraftEndpoints.DraftAttachmentsRoute} -> {MailFathomPermission.MailDraftsWrite.Name}",
                $"POST {prefix}{ClientDraftEndpoints.DraftSendRoute} -> {MailFathomPermission.MailSend.Name}",
                $"POST {prefix}{ClientOutboxEndpoints.OutboxCancellationRoute} -> {MailFathomPermission.MailSend.Name}",
                $"POST {prefix}{ClientOutboxEndpoints.OutboxRequeueRoute} -> {MailFathomPermission.MailSend.Name}",
                $"POST {prefix}{ClientPreferencesEndpoint.PreferencesRoute} -> {MailFathomPermission.MailRead.Name}",
                $"POST {prefix}{ClientOwnerRecordEndpoint.RecordRoute} -> {MailFathomPermission.MailAccountsWrite.Name}",
                $"POST {prefix}{ClientOwnerRecordEndpoint.MailAccountsRoute} -> {MailFathomPermission.MailAccountsWrite.Name}",
                $"POST {prefix}{ClientOwnerRecordEndpoint.MailAccountRemovalRoute} -> {MailFathomPermission.MailAccountsWrite.Name}",
                .. ClientTelemetrySignal.All
                    .Select(signal =>
                        $"POST {prefix}{ClientTelemetryEndpoint.TelemetryRoutePrefix}{signal.Route} -> none")
                    .Order(StringComparer.Ordinal),
                $"PUT {prefix}{ClientDraftEndpoints.DraftRoute} -> {MailFathomPermission.MailDraftsWrite.Name}",
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
    /// A read under the reading grant, and every write under a grant that says what it writes, save the two a grant
    /// could not tell apart: the caller's own client preferences, and the client handing over its own telemetry.
    /// Reading mail, changing the caller's own record, composing a draft, filing one on their server, and sending are
    /// five separately provisioned powers, so a route that changes anything under <c>mailfathom.mail.read</c> is one
    /// somebody added without deciding what it costs — and a credential provisioned to read a mailbox would then send
    /// from it.
    /// </summary>
    [Fact]
    public void MapClientApi_Always_PublishesEveryWriteUnderAGrantThatSaysWhatItWrites()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        Assert.All(
            endpoints.Materialize(),
            endpoint =>
            {
                // A route carrying no verb metadata answers every verb, so reading the absence as an empty set would
                // pass this test on exactly the route it exists to refuse.
                var verbs = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();

                Assert.NotNull(verbs);
                Assert.NotEmpty(verbs.HttpMethods);
                Assert.All(
                    verbs.HttpMethods,
                    method => Assert.True(
                        method == "GET"
                        || IsPublishedAsAWrite(endpoint)
                        || WritesTheCallersOwnPreferences(endpoint)
                        || HandsOverTheClientsOwnTelemetry(endpoint),
                        $"{method} {endpoint} changes something under a grant that does not say so."));
            });
    }

    /// <summary>Reports whether a route was published under one of the grants a write is provisioned by.</summary>
    /// <remarks>The grant rather than the path, because what makes a write admissible on an owner-facing surface is that it is separately provisioned: a path renamed keeps the claim, and a route quietly published under the read grant breaks it.</remarks>
    private static bool IsPublishedAsAWrite(Endpoint endpoint) =>
        endpoint.Metadata.GetOrderedMetadata<RoutePermission>()
            .Any(published =>
                published.Permission == MailFathomPermission.MailAccountsWrite
                || published.Permission == MailFathomPermission.MailDraftsWrite
                || published.Permission == MailFathomPermission.MailSend);

    /// <summary>Reports whether a route is the write of the caller's own client preferences, by the route it is served at.</summary>
    /// <remarks>
    /// The route rather than the grant, which is the one place this file cannot use the test above's reasoning. That
    /// write is deliberately admitted under the grant every read here holds — a person whose mail accounts an
    /// administrator maintains does not hold <see cref="MailFathomPermission.MailAccountsWrite" /> and still turns
    /// their own telemetry off — so nothing about the permission tells it apart from a <c>GET</c>. Naming the one route
    /// keeps the claim narrow: a second write published under the read grant fails this rather than joining it.
    /// </remarks>
    private static bool WritesTheCallersOwnPreferences(Endpoint endpoint) =>
        endpoint is RouteEndpoint route
        && $"/{route.RoutePattern.RawText?.TrimStart('/')}"
            == $"{ClientEndpointOptions.RoutePrefix}{ClientPreferencesEndpoint.PreferencesRoute}";

    /// <summary>Reports whether a route is the client posting its own telemetry, which changes nothing this deployment holds.</summary>
    /// <remarks>The path rather than the grant here, because these are published under none by design — the caller is handing over what it recorded about itself, and no permission in the mailbox half names that act.</remarks>
    private static bool HandsOverTheClientsOwnTelemetry(Endpoint endpoint) =>
        endpoint is RouteEndpoint { RoutePattern.RawText: { } path }
        && path.Contains(
            $"{ClientEndpointOptions.RoutePrefix}{ClientTelemetryEndpoint.TelemetryRoutePrefix}/",
            StringComparison.Ordinal);

    /// <summary>
    /// The bound on each record-route body, which the routes carry as metadata the routing pipeline reads. This surface
    /// is reached by a person's own password rather than by an operator's key, so the default the server would apply
    /// instead is the one bound nobody here decided on.
    /// </summary>
    /// <param name="route">The route the write is made on.</param>
    [Theory]
    [InlineData(ClientOwnerRecordEndpoint.RecordRoute)]
    [InlineData(ClientOwnerRecordEndpoint.MailAccountsRoute)]
    [InlineData(ClientOwnerRecordEndpoint.MailAccountRemovalRoute)]
    public void MapClientApi_ARecordRouteThatReadsABody_CarriesTheRequestBodyBound(string route)
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        var write = endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Single(endpoint =>
                $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}" == $"{ClientEndpointOptions.RoutePrefix}{route}"
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        Assert.Equal(
            OwnerRecordEndpoints.MaxWriteRequestBytes,
            write.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

    /// <summary>
    /// The preferences write carries a bound of its own rather than the record's. The document is three scalars, so a
    /// body sized for a page of mail-account declarations would be a bound nobody decided on.
    /// </summary>
    [Fact]
    public void MapClientApi_ThePreferencesWrite_CarriesItsOwnRequestBodyBound()
    {
        // Arrange
        var endpoints = BuildRouteBuilder();

        // Act
        endpoints.MapClientApi();

        // Assert
        var write = endpoints.Materialize()
            .OfType<RouteEndpoint>()
            .Single(endpoint =>
                $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}"
                    == $"{ClientEndpointOptions.RoutePrefix}{ClientPreferencesEndpoint.PreferencesRoute}"
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST"));

        Assert.Equal(
            ClientPreferencesEndpoint.MaxWriteRequestBytes,
            write.Metadata.GetMetadata<Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata>()!.MaxRequestBodySize);
    }

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
        services.AddSingleton(Options.Create(new MailDeliveryOptions()));
        services.AddScoped(_ => UnreachedFreshnessReader(granted));
        services.AddScoped(_ => new MailFolderDirectoryReader(
            UnreachedFreshnessReader(granted),
            UnreachedScopeResolver(),
            Substitute.For<IStoredMailFolderReader>(),
            Substitute.For<IMailFolderMappingReader>()));

        // A destination is registered so the telemetry routes are mapped, because every surface-wide claim in this
        // file — the group's requirement, the CORS policy, the prefix, the published decision — has to hold over them
        // too. Which deployment maps them at all is ClientTelemetryEndpointTests' subject rather than this file's.
        services.AddSingleton(new ClientTelemetryDestination(
            new Uri("https://collector.example.test"),
            OtlpExportProtocol.HttpProtobuf,
            [],
            TimeSpan.FromSeconds(10)));

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
