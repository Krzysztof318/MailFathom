// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Xml.Linq;
using MailFathom.Host;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>The real host, started inside the test process, with the pipeline reachable and nothing outside it touched.</summary>
/// <remarks>
/// <para>
/// It composes the service graph the deployment composes, fills in the pipeline
/// <see cref="HostPipeline.Compose(WebApplication, ComposedHostSurfaces)" /> composes, and starts the host so that
/// minimal hosting assembles what it puts around them — routing in front, endpoint execution behind, and its own
/// authentication and authorization middleware wherever the application added none. That last part is the reason this
/// exists: it is decided while the host starts and is invisible to anything that only inspects the application.
/// </para>
/// <para>
/// It is in this suite rather than in <c>Host.UnitTests</c> because building and starting a
/// <see cref="WebApplication" /> is what separates the two, whatever the started host then turns out to reach. What it
/// is not is a second orchestrated host: the resources <see cref="Orchestration.MailFathomOrchestrationFixture" />
/// starts are whole MailFathom processes the app model configures, and the shapes below are configured per test,
/// several per run, which is the only way to drive a deployment shape a running process would have to be restarted to
/// take.
/// </para>
/// <para>
/// Three things are taken out so that what starts is a pipeline rather than a deployment.
/// <see cref="PipelineCapturingServer" /> replaces Kestrel, so no socket is bound and no port is contended for with
/// the orchestrated hosts this suite already runs. Every hosted service the composition added is removed, because those
/// are the workers that reach a database, a mail server, and a model endpoint the moment they start — this host is
/// composed for its request pipeline and shares neither the orchestrated database nor the orchestrated mailbox. And the
/// data protection key ring is held in memory rather than under whoever ran the suite.
/// </para>
/// </remarks>
internal sealed class InProcessComposedHost : IAsyncDisposable
{
    /// <summary>The one setting every shape carries, because a deployment reaching no database is not one worth composing.</summary>
    /// <remarks>Nothing dials it: the workers that would are removed before the container is built, and the address names no host this suite started.</remarks>
    private static readonly KeyValuePair<string, string?>[] Database =
    [
        new("ConnectionStrings:mailfathom", "Host=localhost;Database=mailfathom;Username=mailfathom"),
    ];

    private readonly WebApplication app;
    private readonly PipelineCapturingServer server;

    private InProcessComposedHost(WebApplication app, PipelineCapturingServer server, AuthenticationSchemeLog authenticatedSchemes)
    {
        this.app = app;
        this.server = server;
        this.AuthenticatedSchemes = authenticatedSchemes;
    }

    /// <summary>Gets every authentication scheme the pipeline has asked to judge a request so far.</summary>
    internal AuthenticationSchemeLog AuthenticatedSchemes { get; }

    /// <summary>Gets the services the composition registered, as the running host resolved them.</summary>
    internal IServiceProvider Services => this.app.Services;

    /// <summary>Gets the body-size limit the pipeline applied to the last request it answered.</summary>
    /// <remarks>
    /// A server states one and the routing pipeline narrows it to whatever the selected endpoint declared, which is
    /// the mechanism every write on these surfaces bounds its body with. Reading it back is what tells a route's
    /// declared bound apart from one that was declared and never reached the server.
    /// </remarks>
    internal long? AppliedRequestBodySizeLimit { get; private set; }

    /// <summary>Composes one deployment shape and starts it.</summary>
    /// <param name="configuration">The settings the shape is written as, which is the input the composition actually reads.</param>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <param name="beyondComposition">Anything a test has to register after the composition has run and before the container is built.</param>
    /// <returns>The started host.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration" /> is <see langword="null" />.</exception>
    internal static async Task<InProcessComposedHost> StartAsync(
        IReadOnlyList<KeyValuePair<string, string?>> configuration,
        CancellationToken cancellationToken,
        Action<WebApplicationBuilder>? beyondComposition = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ContentRootPath = AppContext.BaseDirectory,
        });

        // Cleared because the framework's sources are this machine's, and an environment variable set for a developer's
        // run would otherwise decide what a shape composes. What the shape states is then the whole of what the
        // composition reads.
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection([.. Database, .. configuration]);
        builder.Logging.ClearProviders();
        builder.Services.Configure<KeyManagementOptions>(
            keyManagement => keyManagement.XmlRepository = new KeysHeldInMemory());

        // Read before the composition adds its own, because what has to go is the difference between the two: the
        // framework's hosted services build the pipeline and the key ring, and MailFathom's are the workers that
        // synchronize mailboxes, run jobs, and gate on a database schema.
        var frameworkWorkers = builder.Services
            .Where(static descriptor => descriptor.ServiceType == typeof(IHostedService))
            .ToHashSet();

        var composition = HostComposition.Compose(builder);

        foreach (var composedWorker in builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && !frameworkWorkers.Contains(descriptor))
            .ToArray())
        {
            builder.Services.Remove(composedWorker);
        }

        var server = new PipelineCapturingServer();
        builder.Services.AddSingleton<IServer>(server);

        // The console lifetime registers process-wide signal handlers and writes to standard output. Neither belongs in
        // a suite that starts several hosts of its own.
        builder.Services.AddSingleton<IHostLifetime, UnattendedHostLifetime>();

        var authenticatedSchemes = new AuthenticationSchemeLog();

        // Only where the shape configured authentication at all: registering the decorator otherwise would add an
        // authentication service to a deployment that composed none, which is the very thing one of these tests asserts
        // does not exist.
        if (builder.Services.Any(static descriptor => descriptor.ServiceType == typeof(IAuthenticationService)))
        {
            builder.Services.AddScoped<IAuthenticationService>(provider => new RecordingAuthenticationService(
                ActivatorUtilities.CreateInstance<AuthenticationService>(provider),
                authenticatedSchemes));
        }

        beyondComposition?.Invoke(builder);

        var app = builder.Build();

        HostPipeline.Compose(app, composition);

        await app.StartAsync(cancellationToken);

        return new InProcessComposedHost(app, server, authenticatedSchemes);
    }

    /// <summary>Runs one request through the started pipeline.</summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The request path.</param>
    /// <param name="localPort">The port the request arrived on, which is what surface isolation reads to decide whether this listener serves the path.</param>
    /// <param name="headers">The request headers, which is where a credential and a forwarded scheme arrive.</param>
    /// <returns>The response the pipeline produced.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    internal async Task<HttpResponseFeature> SendAsync(
        string method,
        string path,
        int localPort,
        params (string Name, string Value)[] headers)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(headers);

        var request = new HttpRequestFeature
        {
            Method = method,
            Path = path,
            Scheme = "http",
            Protocol = "HTTP/1.1",
        };

        request.Headers.Host = "mail.example.test";

        foreach (var (name, value) in headers)
        {
            request.Headers[name] = value;
        }

        var response = new HttpResponseFeature();

        await using var body = new MemoryStream();

        // The limit a server carries before an endpoint narrows it, which is Kestrel's own default. It is here so a
        // route's declared bound has something to reach: without the feature the pipeline has nowhere to write one.
        var bodySizeLimit = new ServerRequestBodySizeLimit();

        var requestFeatures = new FeatureCollection();
        requestFeatures.Set<IHttpRequestFeature>(request);
        requestFeatures.Set<IHttpResponseFeature>(response);
        requestFeatures.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(body));
        requestFeatures.Set<IHttpMaxRequestBodySizeFeature>(bodySizeLimit);

        // The port is what surface isolation matches a listener by, and the address is what the forwarded-header
        // middleware judges as a peer — a shape naming no trusted proxy trusts every address, which is the default this
        // deployment warns about rather than one these tests configure around.
        requestFeatures.Set<IHttpConnectionFeature>(new HttpConnectionFeature
        {
            LocalPort = localPort,
            LocalIpAddress = IPAddress.Loopback,
            RemoteIpAddress = IPAddress.Loopback,
            RemotePort = 51234,
        });

        await this.server.SendAsync(requestFeatures);

        this.AppliedRequestBodySizeLimit = bodySizeLimit.MaxRequestBodySize;

        return response;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.app.StopAsync();
        await this.app.DisposeAsync();
    }

    /// <summary>Starts and stops the host without touching the console or the process's signal handlers.</summary>
    [SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The host materializes the lifetime it resolves from the container.")]
    private sealed class UnattendedHostLifetime : IHostLifetime
    {
        /// <inheritdoc />
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>The body-size limit a server offers the pipeline, which the pipeline then narrows per endpoint.</summary>
    /// <remarks>It enforces nothing, because enforcement is the server's half and no octets flow here; what it holds is the number the pipeline decided on, which is the part a route's declared bound has to reach.</remarks>
    private sealed class ServerRequestBodySizeLimit : IHttpMaxRequestBodySizeFeature
    {
        /// <inheritdoc />
        public bool IsReadOnly => false;

        /// <inheritdoc />
        public long? MaxRequestBodySize { get; set; } = 30 * 1024 * 1024;
    }

    /// <summary>Where the framework's data protection keys go while a pipeline is being driven.</summary>
    /// <remarks>Nothing here protects anything, so the repository is never read from and never written to; it exists so the key manager settles on something that is not a directory in somebody's home.</remarks>
    private sealed class KeysHeldInMemory : IXmlRepository
    {
        private readonly List<XElement> elements = [];

        /// <inheritdoc />
        public IReadOnlyCollection<XElement> GetAllElements() => this.elements.AsReadOnly();

        /// <inheritdoc />
        public void StoreElement(XElement element, string friendlyName) => this.elements.Add(element);
    }
}
