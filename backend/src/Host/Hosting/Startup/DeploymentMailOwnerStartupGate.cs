// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Refuses to start unless this deployment holds exactly one owner for its configured mail accounts to belong to.</summary>
/// <remarks>
/// <para>
/// A mail account declared in configuration names no owner, so which owner it belongs to is answered by there being one
/// to answer with. This gate establishes that before anything is served and publishes the answer, so every caller a
/// mail-reading surface admits is composed for a named owner rather than for whichever owner a read happened to find.
/// </para>
/// <para>
/// Zero and several are both refused, and each says which it was. Zero means the release's schema has not been applied,
/// since that is what provisions the owner an upgraded deployment's accounts are carried onto. Several means a
/// deployment has acquired owner records while its accounts are still declared in a file that cannot say whose they
/// are; serving it would mean attributing every configured account to whichever owner a query returned first, which is
/// the reading that quietly hands one person another person's mail. Refusing here is also what makes a second owner
/// unusable while accounts stay in configuration, rather than usable and wrong.
/// </para>
/// <para>
/// It runs behind the schema gate, because it reads a table that migration creates, and ahead of the workers and the
/// listener, because a request answered without it would be a request answered for nobody. It is one read of at most two
/// rows, so the interval it adds to startup is the connection rather than the query.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class DeploymentMailOwnerStartupGate : IHostedService
{
    /// <summary>How many owners are read, which is one more than a deployment may hold so that "several" is observable.</summary>
    private const int OwnersRead = 2;

    private readonly IServiceScopeFactory scopeFactory;
    private readonly DeploymentMailOwner deploymentOwner;
    private readonly HostStartupGates startupGates;
    private readonly ILogger<DeploymentMailOwnerStartupGate> logger;

    /// <summary>Initializes a new deployment owner startup gate.</summary>
    /// <param name="scopeFactory">Creates the scope the owner directory is resolved from.</param>
    /// <param name="deploymentOwner">The holder this gate publishes the resolved owner into.</param>
    /// <param name="startupGates">The tracker this gate reports its completion to, which is what the startup probe reads.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scopeFactory" />, <paramref name="deploymentOwner" />, or <paramref name="startupGates" /> is <see langword="null" />.</exception>
    public DeploymentMailOwnerStartupGate(
        IServiceScopeFactory scopeFactory,
        DeploymentMailOwner deploymentOwner,
        HostStartupGates startupGates,
        ILogger<DeploymentMailOwnerStartupGate> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(deploymentOwner);
        ArgumentNullException.ThrowIfNull(startupGates);

        this.scopeFactory = scopeFactory;
        this.deploymentOwner = deploymentOwner;
        this.startupGates = startupGates;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="DeploymentMailOwnerUnresolvedException">Thrown when the deployment holds no owner record, or more than one.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = this.scopeFactory.CreateAsyncScope();

        var owners = await scope.ServiceProvider
            .GetRequiredService<IMailOwnerDirectory>()
            .ReadOwnersAsync(OwnersRead, cancellationToken);

        var owner = owners switch
        {
            [var soleOwner] => soleOwner,
            [] => throw DeploymentMailOwnerUnresolvedException.NoOwner(),
            _ => throw DeploymentMailOwnerUnresolvedException.SeveralOwners(),
        };

        this.deploymentOwner.Resolved(owner);

        this.LogOwnerResolved();

        this.startupGates.MarkCompleted(HostStartupGate.DeploymentMailOwner);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <remarks>The record names no owner. The identity is a generated identifier for a person this deployment serves, and what an operator needs from this line is that the question was settled rather than which answer it was settled with.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment serves one owner, and every configured mail account belongs to them.")]
    private partial void LogOwnerResolved();
}
