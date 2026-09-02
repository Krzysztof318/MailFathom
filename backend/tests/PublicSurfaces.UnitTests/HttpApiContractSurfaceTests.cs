// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Host.Api;
using MailFathom.Host.Api.Documentation;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting;
using MailFathom.Host.Security.Transport;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Covers what the recorded HTTP surface holds, and that a change to an endpoint reaches it.</summary>
/// <remarks>
/// <para>
/// The golden file itself proves neither. A record that silently stopped describing operations would match a golden
/// file regenerated from the same emptiness, and a record that described everything the host maps would look exactly
/// as intentional — so which routes reach it is asserted here rather than read out of a file nobody diffs line by
/// line.
/// </para>
/// <para>
/// The mutations are the other half of that. What a golden file is worst at reporting is a rendering that no longer
/// varies with its subject, so each of the things a client binds to — the verb, the route, the shape it sends, the
/// shape it receives, the status it is answered with, and whether a credential is demanded — is moved on a fixture
/// endpoint and the rendering is required to move with it. They are moved on a fixture rather than on a real
/// operation because the claim is about the renderer rather than about any one route, and because mutating a real one
/// would mean changing the contract to test the record of it.
/// </para>
/// </remarks>
public sealed class HttpApiContractSurfaceTests
{
    /// <summary>The route the fixture endpoint answers on, beneath a prefix the document describes.</summary>
    private const string FixtureRoute = "/public-surface-fixture";

    /// <summary>Where RFC 9728 puts a protected resource's metadata document, which is the one thing both of them share.</summary>
    private const string MetadataDocumentPrefix = "/.well-known/";

    /// <summary>One mutation per thing a client binds to, each against <see cref="MapTheFixture" />.</summary>
    private static readonly IReadOnlyDictionary<string, Action<IEndpointRouteBuilder>> Mutations =
        new Dictionary<string, Action<IEndpointRouteBuilder>>(StringComparer.Ordinal)
        {
            ["the verb"] = routes => Fixture(routes)
                .MapPut(FixtureRoute, (FixtureRequest request) => TypedResults.Ok(new FixtureResponse(request.Note)))
                .RequireAuthorization(TransportSurface.Admin.AccessPolicyName),
            ["the route"] = routes => Fixture(routes)
                .MapPost($"{FixtureRoute}/moved", (FixtureRequest request) => TypedResults.Ok(new FixtureResponse(request.Note)))
                .RequireAuthorization(TransportSurface.Admin.AccessPolicyName),
            ["the request schema"] = routes => Fixture(routes)
                .MapPost(FixtureRoute, (RenamedFixtureRequest request) => TypedResults.Ok(new FixtureResponse(request.Remark)))
                .RequireAuthorization(TransportSurface.Admin.AccessPolicyName),
            ["the response schema"] = routes => Fixture(routes)
                .MapPost(FixtureRoute, (FixtureRequest request) => TypedResults.Ok(new WidenedFixtureResponse(request.Note, 1)))
                .RequireAuthorization(TransportSurface.Admin.AccessPolicyName),
            ["the status code"] = routes => Fixture(routes)
                .MapPost(FixtureRoute, (FixtureRequest request) => TypedResults.Accepted(default(string), new FixtureResponse(request.Note)))
                .RequireAuthorization(TransportSurface.Admin.AccessPolicyName),
            ["the security metadata"] = routes => Fixture(routes)
                .MapPost(FixtureRoute, (FixtureRequest request) => TypedResults.Ok(new FixtureResponse(request.Note))),
        };

    /// <summary>Names the mutations, which is what a failure renders.</summary>
    public static TheoryData<string> EndpointMutations => [.. Mutations.Keys];

    /// <summary>What the record is for: both API surfaces reach it, and nothing else this process maps does.</summary>
    /// <remarks>
    /// The absences are the assertion rather than the presences. Each of those routes is mapped by the rendering, so a
    /// path missing here is missing because <see cref="ApiDocumentation.DescribesHttpApi" /> left it out — which is
    /// the one thing standing between an anonymous development document and a catalogue of the protocol route, the
    /// attachment download, the probes, the metadata documents, and the explorer. That each of them really was mapped
    /// is the next test's subject rather than something taken on trust here.
    /// </remarks>
    [Fact]
    public async Task RenderAsync_ForTheComposedHost_RecordsBothApiSurfacesAndNothingElseTheHostMaps()
    {
        // Arrange, Act
        var paths = await RecordedPathsAsync();

        // Assert
        Assert.Contains($"{AdminEndpointOptions.RoutePrefix}{AdminApiEndpoints.SessionRoute}", paths, StringComparer.Ordinal);
        Assert.Contains($"{ClientEndpointOptions.RoutePrefix}{ClientApiEndpoints.SessionRoute}", paths, StringComparer.Ordinal);
        Assert.Contains(
            $"{ClientEndpointOptions.RoutePrefix}{ClientMailAccountsEndpoint.MailAccountsRoute}",
            paths,
            StringComparer.Ordinal);
        Assert.Contains(
            $"{ClientEndpointOptions.RoutePrefix}{ClientMailFoldersEndpoint.MailFoldersRoute}",
            paths,
            StringComparer.Ordinal);

        Assert.DoesNotContain(McpEndpointRoute.Path, paths, StringComparer.Ordinal);
        Assert.DoesNotContain(ApiDocumentation.DocumentRoute, paths, StringComparer.Ordinal);
        Assert.Equal(
            [],
            HealthProbe.All.Select(probe => probe.Path).Intersect(paths, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    /// <summary>The routes the record leaves out are routes the rendering really mapped, which is what makes their absence mean anything.</summary>
    /// <remarks>
    /// The control for the absences above. Each of them is asserted against a record produced by a composition that
    /// mapped the route — so this reads the endpoints back out of that same composition and requires the two RFC 9728
    /// metadata documents to be among them, since those are the ones a deployment maps only where it allows OAuth and
    /// would otherwise be absent from the record for never having existed.
    /// </remarks>
    [Fact]
    public async Task RenderAsync_ForTheComposedHost_MapsTheMetadataDocumentsItThenLeavesOut()
    {
        // Arrange
        var metadataDocuments = new List<string>();

        // Act
        var recorded = await HttpApiContractSurface.RenderAsync(
            (routes, surfaces, environment) =>
            {
                HttpApiContractSurface.MapEveryRouteTheHostServes(routes, surfaces, environment);

                // Read while the rendering's container is still alive: a route group builds its endpoints on demand
                // and asks that container which of a handler's parameters are services, so the same read afterwards
                // meets a provider this method has already disposed.
                metadataDocuments.AddRange(routes.DataSources
                    .SelectMany(source => source.Endpoints)
                    .OfType<RouteEndpoint>()
                    .Select(endpoint => $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}")
                    .Where(route => route.StartsWith(MetadataDocumentPrefix, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal));
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, metadataDocuments.Count);
        Assert.Equal([], metadataDocuments.Intersect(PathsIn(recorded), StringComparer.Ordinal));
    }

    /// <summary>Every path the record holds is one of the two surfaces', whatever the host comes to map beside them.</summary>
    /// <remarks>
    /// The rule the assertion above states by example, and stated as the prefixes themselves rather than by asking the
    /// allow-list. The allow-list is what put each path in the record, so re-running it here could only report it
    /// having been unwired — a widened one, admitting the documentation prefix or a metadata document, would put that
    /// path in the record and be agreed with by its own predicate.
    /// </remarks>
    [Fact]
    public async Task RenderAsync_ForTheComposedHost_RecordsNoPathOutsideTheTwoApiPrefixes()
    {
        // Arrange, Act
        var paths = await RecordedPathsAsync();

        // Assert
        Assert.NotEmpty(paths);
        Assert.All(paths, path => Assert.True(
            path.StartsWith(AdminEndpointOptions.RoutePrefix, StringComparison.Ordinal)
            || path.StartsWith(ClientEndpointOptions.RoutePrefix, StringComparison.Ordinal),
            $"'{path}' is on neither API surface."));
    }

    /// <summary>
    /// The control the golden file cannot be. A rendering that stopped varying with its subject matches whatever file
    /// was last regenerated from it and reports a moved contract as no change at all, so each thing a client binds to
    /// is moved and the record is required to move with it.
    /// </summary>
    [Theory]
    [MemberData(nameof(EndpointMutations))]
    public async Task RenderAsync_WithOneEndpointChanged_RecordsSomethingElse(string mutation)
    {
        // Arrange
        var recorded = await RenderAsync(MapTheFixture);

        // Act
        var mutated = await RenderAsync(Mutations[mutation]);

        // Assert
        Assert.NotEqual(recorded, mutated, StringComparer.Ordinal);
    }

    /// <summary>The fixture endpoint every mutation is a variation on.</summary>
    private static void MapTheFixture(IEndpointRouteBuilder routes) => Fixture(routes)
        .MapPost(FixtureRoute, (FixtureRequest request) => TypedResults.Ok(new FixtureResponse(request.Note)))
        .RequireAuthorization(TransportSurface.Admin.AccessPolicyName);

    /// <summary>Opens the group the fixture is mapped into, which is a prefix the document describes.</summary>
    private static RouteGroupBuilder Fixture(IEndpointRouteBuilder routes) =>
        routes.MapGroup(AdminEndpointOptions.RoutePrefix);

    /// <summary>Renders a stated mapping, which is the seam the mutations are read through.</summary>
    private static Task<string> RenderAsync(Action<IEndpointRouteBuilder> map) =>
        HttpApiContractSurface.RenderAsync(
            (routes, _, _) => map(routes),
            TestContext.Current.CancellationToken);

    /// <summary>Reads the paths out of the record the composed host produces.</summary>
    private static async Task<IReadOnlyList<string>> RecordedPathsAsync() =>
        PathsIn(await HttpApiContractSurface.RenderAsync(TestContext.Current.CancellationToken));

    /// <summary>Reads the paths out of a rendering.</summary>
    private static IReadOnlyList<string> PathsIn(string recorded) =>
        [.. JsonNode.Parse(recorded)!["paths"]!.AsObject().Select(path => path.Key)];

    /// <summary>What the fixture endpoint accepts.</summary>
    /// <param name="Note">A value with no meaning beyond being one the schema describes.</param>
    public sealed record FixtureRequest(string Note);

    /// <summary>The same body under a different member, so the request schema is the only thing that moved.</summary>
    /// <param name="Remark">A value with no meaning beyond being one the schema describes.</param>
    public sealed record RenamedFixtureRequest(string Remark);

    /// <summary>What the fixture endpoint answers with.</summary>
    /// <param name="Note">A value with no meaning beyond being one the schema describes.</param>
    public sealed record FixtureResponse(string Note);

    /// <summary>The same answer with a member added, so the response schema is the only thing that moved.</summary>
    /// <param name="Note">A value with no meaning beyond being one the schema describes.</param>
    /// <param name="Revision">A second value, which is the whole of the difference from <see cref="FixtureResponse" />.</param>
    public sealed record WidenedFixtureResponse(string Note, int Revision);
}
