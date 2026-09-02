// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Nodes;
using MailFathom.Host;
using MailFathom.Host.Api;
using MailFathom.Host.Api.Documentation;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.RootSettings;
using MailFathom.Host.Hosting;
using MailFathom.Infrastructure.Persistence.Settings;
using MailFathom.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace MailFathom.PublicSurfaces.UnitTests;

/// <summary>Renders the OpenAPI document the host generates for its whole HTTP API, in a form two builds compare byte for byte.</summary>
/// <remarks>
/// <para>
/// The document is generated rather than described. Every path, verb, parameter, request body, status code, content
/// type, schema, and security requirement in it is derived by the framework from the endpoints the host maps and by
/// the transformers <see cref="ApiDocumentation" /> registers — so this renders the same artifact a developer reads at
/// <c>/openapi/v1.json</c> rather than a second inventory that would have to be kept in step with it.
/// </para>
/// <para>
/// What it composes is a deployment serving every surface at once, with a credential configured on each. That is a
/// choice about which document to record and it is the widest one available: a surface an operator did not enable maps
/// no routes, and a surface with no credential configured maps its routes carrying no requirement, so any other shape
/// records a subset of this one. The service graph is the real one, because whether a handler parameter is a service
/// or a request body is decided by asking the container — a document generated over a container that knows fewer
/// services describes bodies nobody sends.
/// </para>
/// <para>
/// Every other route the host maps is mapped here too, and none of them belongs in the document: the protocol route,
/// the attachment download, the probes, the two RFC 9728 metadata documents, the document itself, and the explorer.
/// They are the control for <see cref="ApiDocumentation.DescribesHttpApi" /> — the allow-list is what keeps them out,
/// and a rendering that never mapped them would record the same file whether that allow-list worked or had been
/// deleted. The last two of those are the reason the deployment below configures an authorization server as well as a
/// key: a surface allowing no OAuth maps no metadata document, so leaving that out would have made two of these
/// absences mean nothing.
/// </para>
/// <para>
/// One route family is absent for a reason the deployment below cannot repair: the client endpoint's OTLP proxy is
/// mapped only where a collector is named, and <see cref="EnvironmentOnlySettings" /> refuses an <c>OTEL_*</c> value
/// from any source but the process environment. Naming one in the in-memory configuration would therefore record a
/// deployment the product itself rejects, and setting the variable for real would attach live exporters to a suite
/// that reaches no network. What those routes publish is asserted where they are mapped, in
/// <c>ClientTelemetryEndpointTests</c>, and a client discovers them from the specification's own paths rather than
/// from this document.
/// </para>
/// <para>
/// Nothing here starts a host. The routes are read from the mapping, which is the boundary
/// <c>backend/tests/AGENTS.md</c> draws, and what the pipeline puts in front of them belongs to the integration suite.
/// </para>
/// </remarks>
internal static class HttpApiContractSurface
{
    /// <summary>What <c>info.version</c> is written as, because the stamped release moves under a document that has not.</summary>
    /// <remarks>
    /// The one value in the generated document that changes without the contract changing: it is the running release,
    /// which every build stamps and which continuous integration extends with a suffix a developer's build does not
    /// carry. Recording it would make the golden file fail on the next version bump and differ between a local run and
    /// a pipeline run, which is the failure mode a golden file is least able to report about itself. What the release
    /// was is recorded by the release rather than here.
    /// </remarks>
    private const string RecordedVersion = "<the running release>";

    /// <summary>The deployment the recorded document is generated from, as the configuration an operator would have written.</summary>
    /// <remarks>
    /// <para>
    /// Every surface enabled and every one of them authenticating, for the reason the class remarks give. The API key
    /// is the cheapest credential that makes a surface authenticate — the requirement an operation carries is the same
    /// whichever credential admitted it, because all three arrive in one header and are published as one scheme.
    /// </para>
    /// <para>
    /// An authorization server is configured beside each key so that the surface allows OAuth, which is the condition
    /// under which its RFC 9728 metadata document is mapped at all. Nothing validates a token here and no request is
    /// made to that issuer: what the setting buys is the two routes existing, so the record has to leave them out
    /// rather than never meeting them.
    /// </para>
    /// </remarks>
    private static readonly KeyValuePair<string, string?>[] DeploymentServingEverySurface =
    [
        new("ConnectionStrings:mailfathom", "Host=localhost;Database=mailfathom;Username=mailfathom"),
        new("McpEndpoint:Enabled", "true"),
        new("McpEndpoint:Authentication:0:Method", "api-key"),
        new("McpEndpoint:Authentication:1:Method", "oauth-subject"),
        new("McpEndpoint:Authentication:1:OAuth:Resource", $"https://mailfathom.example.test{McpEndpointRoute.Path}"),
        new("McpEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Name", "example-issuer"),
        new("McpEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Issuer", "https://issuer.example.test"),
        new("AdminEndpoint:Enabled", "true"),
        new("AdminEndpoint:Port", "8082"),
        new("AdminEndpoint:Authentication:0:ApiKey:Name", "operator"),
        new("AdminEndpoint:Authentication:0:ApiKey:SecretReference", "plaintext:not-a-real-key-either"),
        new("AdminEndpoint:Authentication:1:OAuth:Resource", $"https://mailfathom.example.test{AdminEndpointOptions.RoutePrefix}"),
        new("AdminEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Name", "example-issuer"),
        new("AdminEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Issuer", "https://issuer.example.test"),
        new("AdminEndpoint:Authentication:1:OAuth:AuthorizationServers:0:AuthorizedSubjects:0", "not-a-real-subject"),
        new("ClientEndpoint:Enabled", "true"),
        new("ClientEndpoint:Port", "8084"),
        new("ClientEndpoint:Authentication:0:Method", "api-key"),
        new("ClientEndpoint:Authentication:1:Method", "oauth-subject"),
        new("ClientEndpoint:Authentication:1:OAuth:Resource", $"https://mailfathom.example.test{ClientEndpointOptions.RoutePrefix}"),
        new("ClientEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Name", "example-issuer"),
        new("ClientEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Issuer", "https://issuer.example.test"),
    ];

    /// <summary>Maps the routes one rendering describes.</summary>
    /// <param name="routes">The route builder the rendering reads its endpoints from.</param>
    /// <param name="surfaces">What the composition settled about the surfaces this deployment serves.</param>
    /// <param name="environment">The environment the rendering composes, which is the one publishing a document at all.</param>
    internal delegate void RouteComposition(
        IEndpointRouteBuilder routes,
        ComposedHostSurfaces surfaces,
        IHostEnvironment environment);

    /// <summary>Renders the published HTTP API contract.</summary>
    /// <param name="cancellationToken">Cancels the generation.</param>
    /// <returns>The canonical JSON form of the document the host generates for its whole HTTP API.</returns>
    public static Task<string> RenderAsync(CancellationToken cancellationToken) =>
        RenderAsync(MapEveryRouteTheHostServes, cancellationToken);

    /// <summary>Renders the document a stated mapping produces, which is how a change to one endpoint is proven visible.</summary>
    /// <param name="compose">The routes to map before the document is generated.</param>
    /// <param name="cancellationToken">Cancels the generation.</param>
    /// <returns>The canonical JSON form of the generated document.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="compose" /> is <see langword="null" />.</exception>
    internal static async Task<string> RenderAsync(RouteComposition compose, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compose);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            // Development is the only environment publishing a document at all, and the content root is this run's
            // rather than the working directory, so a suite started from anywhere reads the same configuration.
            EnvironmentName = Environments.Development,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Cleared rather than layered under, because the sources the framework supplies are this machine's: the host's
        // own appsettings.json travels into the test output through the project reference, and an environment variable
        // set for a developer's run would otherwise decide which surfaces the recorded document describes.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(DeploymentServingEverySurface);

        // The persisted configuration layer, empty but present. Every deployment composes one — the host reads it
        // before it composes anything else — and the routes that administer it are mapped only where it exists, so a
        // rendering over sources cleared to an in-memory list would record a document missing a whole route family for
        // a reason no operator ever chose. The document is empty because what the layer carries decides nothing about
        // the contract; that it was composed at all is the whole of what this line is for.
        builder.Configuration.AddRootSettings(new RootSettingsDocument("{}", Version: 0));

        var surfaces = HostComposition.Compose(builder);

        // The endpoints a rendering describes are the ones mapped below, and the API explorer reads them from the
        // container. Registering the composite over a list filled afterwards is what lets one container answer both
        // questions — which services exist, and which endpoints were mapped — without a host being started to join them.
        var mapped = new List<EndpointDataSource>();
        builder.Services.AddSingleton<EndpointDataSource>(_ => new CompositeEndpointDataSource(mapped));

        await using var provider = builder.Services.BuildServiceProvider();

        var routes = new MappedRoutes(provider);
        compose(routes, surfaces, builder.Environment);
        mapped.AddRange(routes.DataSources);

        var document = await provider
            .GetRequiredKeyedService<IOpenApiDocumentProvider>(ApiDocumentation.DocumentName)
            .GetOpenApiDocumentAsync(cancellationToken);

        return CanonicalJson.Render(Recorded(document, VersionServedBy(provider)));
    }

    /// <summary>Maps every route the composed host serves, whether or not the document describes it.</summary>
    internal static void MapEveryRouteTheHostServes(
        IEndpointRouteBuilder routes,
        ComposedHostSurfaces surfaces,
        IHostEnvironment environment)
    {
        HostPipeline.ComposeAdminSurface(routes, surfaces);
        HostPipeline.ComposeClientSurface(routes, surfaces);

        // The control described in the class remarks. Each of these is a route this process answers and none of them is
        // an operation with a published HTTP contract, so each has to be absent from the record for the allow-list's
        // reason rather than because nothing mapped it.
        routes.MapMcp(McpEndpointRoute.Path);
        routes.MapEmailAttachmentDownload();
        routes.MapHealthProbes();
        routes.MapApiDocumentation(environment);
    }

    /// <summary>Reads back which specification version the host serves this document under.</summary>
    /// <remarks>
    /// Asked rather than stated, because a document written under one version and served under another describes types
    /// differently — a nullable schema above all — and the record is meant to be the served artifact.
    /// </remarks>
    private static OpenApiSpecVersion VersionServedBy(IServiceProvider provider) => provider
        .GetRequiredService<IOptionsMonitor<OpenApiOptions>>()
        .Get(ApiDocumentation.DocumentName)
        .OpenApiVersion;

    /// <summary>Writes the generated document out as JSON, with the values that move under an unchanged contract replaced.</summary>
    private static JsonObject Recorded(OpenApiDocument document, OpenApiSpecVersion version)
    {
        var buffer = new StringWriter();
        document.SerializeAs(version, new OpenApiJsonWriter(buffer));

        var recorded = JsonNode.Parse(buffer.ToString())!.AsObject();

        // Read rather than tested for, because a document arriving without one is a generator that stopped describing
        // the API rather than a value to leave alone — and a rendering that quietly skipped the replacement would
        // record whichever release the last regeneration ran on.
        var info = recorded["info"]?.AsObject()
            ?? throw new InvalidOperationException("The generated document carries no info object.");

        info["version"] = RecordedVersion;

        // Absent from a document generated outside a request, which is where this one comes from, and environment-
        // specific wherever it is not. Removed rather than left to chance, so a run under a framework that starts
        // filling it in records the contract rather than the address a machine happened to answer on.
        recorded.Remove("servers");

        return recorded;
    }

    /// <summary>The smallest thing the routing builder API accepts, so a mapping can be read without a web host.</summary>
    private sealed class MappedRoutes(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;

        public ICollection<EndpointDataSource> DataSources { get; } = [];

        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(this.ServiceProvider);
    }
}
