// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;

namespace MailFathom.IntegrationTests.Hosting;

/// <summary>Stands where Kestrel does and keeps the pipeline the host handed it, without binding a socket.</summary>
/// <remarks>
/// <para>
/// The pipeline this exists to reach is not the one the application composes. Minimal hosting wraps that one — it runs
/// routing in front of it and appends endpoint execution behind it, and it inserts an authentication and an
/// authorization middleware of its own unless the application added each explicitly. All of it is assembled while the
/// host starts, so nothing short of starting the host produces it, and a test that builds only the application's own
/// middleware would prove nothing about the order a request actually meets.
/// </para>
/// <para>
/// Starting the host is exactly what the server abstraction lets a test do without a listener: the host builds the
/// pipeline, hands it here, and this keeps it instead of accepting connections. A request is then a feature collection
/// the test composes, which is what a request is by the time it reaches the first middleware anyway — so no port is
/// bound, and this suite's own orchestrated hosts keep the ones they were given.
/// </para>
/// </remarks>
internal sealed class PipelineCapturingServer : IServer
{
    private Func<IFeatureCollection, Task>? pipeline;

    /// <inheritdoc />
    /// <remarks>Empty, and read by the host for the addresses it would have bound. Nothing binds one here, and the host treats an absent address feature as a server that decides its own addresses.</remarks>
    public IFeatureCollection Features { get; } = new FeatureCollection();

    /// <inheritdoc />
    public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken)
        where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(application);

        this.pipeline = async requestFeatures =>
        {
            var context = application.CreateContext(requestFeatures);

            await application.ProcessRequestAsync(context);

            application.DisposeContext(context, exception: null);
        };

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>Runs one request through the pipeline the host built.</summary>
    /// <param name="requestFeatures">The features the request arrives with, which is what a connection would otherwise have supplied.</param>
    /// <returns>A task completing when the pipeline has finished with the request.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requestFeatures" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the host has not been started, so there is no pipeline to run.</exception>
    internal Task SendAsync(IFeatureCollection requestFeatures)
    {
        ArgumentNullException.ThrowIfNull(requestFeatures);

        var built = this.pipeline
            ?? throw new InvalidOperationException("The host has not been started, so no pipeline has been handed to the server.");

        return built(requestFeatures);
    }
}
