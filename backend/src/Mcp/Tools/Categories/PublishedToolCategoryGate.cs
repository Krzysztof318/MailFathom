// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Categories;

/// <summary>Refuses to start a host whose protocol surface registers a tool that declares no category.</summary>
/// <remarks>
/// <para>
/// A category is what a deployment selects by, so a tool without one has no answer to the question the selection asks.
/// Defaulting it into a category would be the surface deciding on the operator's behalf which kind of thing a new tool
/// is, and defaulting it out of every category would leave a registered tool silently unreachable — both are worse than
/// refusing to start, which is a defect the person who added the tool meets on their first run rather than one an
/// operator meets as a listing that stopped mentioning something.
/// </para>
/// <para>
/// It reads the registered tools rather than a list of its own, so the set it judges is the one a host actually serves.
/// The check is a dictionary lookup per tool and reaches nothing outside the process, which is why it needs no probe,
/// no timeout, and no place in the startup gates a readiness answer is composed from.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed class PublishedToolCategoryGate : IHostedService
{
    private readonly IEnumerable<McpServerTool> registeredTools;

    /// <summary>Initializes the gate over the tools the registration composed.</summary>
    /// <param name="registeredTools">Every tool this host's protocol surface registered.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="registeredTools" /> is <see langword="null" />.</exception>
    public PublishedToolCategoryGate(IEnumerable<McpServerTool> registeredTools)
    {
        ArgumentNullException.ThrowIfNull(registeredTools);

        this.registeredTools = registeredTools;
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when a registered tool declares no category, naming every tool that does not.</exception>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var uncategorized = this.registeredTools
            .Select(static tool => tool.ProtocolTool.Name)
            .Where(static name => !PublishedTools.TryGetCategory(name, out _))
            .ToArray();

        return uncategorized.Length is 0
            ? Task.CompletedTask
            : throw new InvalidOperationException(
                $"The protocol surface registers {string.Join(", ", uncategorized)}, which declare no tool category, so no deployment could decide whether to publish them. Declare a category on the tool and add it to the published set.");
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
