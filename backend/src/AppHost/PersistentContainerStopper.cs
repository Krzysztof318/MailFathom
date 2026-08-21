// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MailFathom.AppHost;

/// <summary>Stops the resources that outlive the app host, when the app host stops.</summary>
/// <remarks>
/// <para>
/// A container resource has two lifetimes and neither is the one a developer's database wants. A session lifetime
/// destroys the container on shutdown, which costs a build and an initialization pass on every start; a persistent one
/// keeps it and also keeps it running, which leaves a PostgreSQL server and its port occupied by an orchestration that
/// has exited. What is wanted is the container kept and stopped, so this stops it: the next run reattaches to the same
/// container and starts it again.
/// </para>
/// <para>
/// It runs as the last hosted service registered, so its shutdown runs first — before the orchestrator that would
/// otherwise be gone by the time the command is issued. A stop that fails is logged and nothing more: a container left
/// running is a developer's inconvenience, and failing the shutdown over it would be worse than the state it reports.
/// A process killed rather than stopped runs none of this and leaves the container running, which is the same outcome
/// the persistent lifetime gives on its own.
/// </para>
/// </remarks>
/// <param name="commandService">The service that executes the orchestrator's own resource commands.</param>
/// <param name="resources">The resources to stop, in the order they are stopped.</param>
/// <param name="logger">Records a stop that did not happen.</param>
internal sealed class PersistentContainerStopper(
    ResourceCommandService commandService,
    IReadOnlyList<IResource> resources,
    ILogger<PersistentContainerStopper> logger) : IHostedService
{
    /// <summary>Does nothing; the orchestrator starts these resources itself.</summary>
    /// <param name="cancellationToken">Unused.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Stops each resource, reporting rather than raising whatever did not stop.</summary>
    /// <param name="cancellationToken">Cancels the remaining stops; the shutdown continues either way.</param>
    /// <returns>A task that completes once every resource has been asked to stop.</returns>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var resource in resources)
        {
            try
            {
                var result = await commandService.ExecuteCommandAsync(
                    resource,
                    KnownResourceCommands.StopCommand,
                    cancellationToken);

                if (!result.Success)
                {
                    logger.LogWarning(
                        "Resource {ResourceName} was not stopped and is still running: {Reason}",
                        resource.Name,
                        result.Message ?? "the orchestrator reported no reason");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Resource {ResourceName} was not stopped and is still running.", resource.Name);
            }
        }
    }
}
