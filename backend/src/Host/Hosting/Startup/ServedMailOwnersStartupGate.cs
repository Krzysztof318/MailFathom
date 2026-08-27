// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.Infrastructure.Persistence.Owners;
using Microsoft.Extensions.Options;

namespace MailFathom.Host.Hosting.Startup;

/// <summary>Reconciles the owners this deployment declares against the rows it holds, and settles who it serves.</summary>
/// <remarks>
/// <para>
/// A deployment may keep its whole configuration outside the database, owners included. What the database still holds
/// per owner is the relational envelope — the identifier, the label, the version, the instants — because
/// <c>mailbox_accounts.OwnerId</c> is a foreign key and the integrity of the mail graph is relational rather than a
/// predicate over a document. So this gate gives every declared owner that row and nothing inside it: their mail
/// accounts go on being read from the effective configuration until an adoption writes their document.
/// </para>
/// <para>
/// The handover is per owner and never happens here. An owner whose row carries the runtime-written marker is served
/// from their own document, permanently and for that owner alone, and every other owner goes on being read from the
/// file beside them. What this gate does about it is report which of the two each owner is, because a section somebody
/// goes on editing for an owner that no longer reads it is exactly the mistake nothing else would surface.
/// </para>
/// <para>
/// It runs behind the schema gate, because it reads and writes a table that migration creates, and ahead of the
/// workers, so nothing synchronizes mail before the owner it belongs to is named. It is not ahead of the listener: the
/// web host registers its own hosted service while the builder runs and therefore starts it first, so the port is
/// already open while this gate runs, and the startup probe is what reports the deployment unstarted until it
/// completes.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this hosted service.")]
internal sealed partial class ServedMailOwnersStartupGate : IHostedService
{
    /// <summary>The label the owner of a deployment that declares none is recorded under.</summary>
    /// <remarks>
    /// The same label the migration that provisions that owner writes, so a deployment upgraded through it and one
    /// whose row this gate had to create read identically. It is only ever used where the deployment holds no owner at
    /// all, so nothing can already be carrying it.
    /// </remarks>
    private const string SoleOwnerDisplayName = "owner";

    private readonly IServiceScopeFactory scopeFactory;
    private readonly IConfiguration configuration;
    private readonly ServedMailOwners servedOwners;
    private readonly HostStartupGates startupGates;
    private readonly McpEndpointOptions mcpEndpointSettings;
    private readonly ClientEndpointOptions clientEndpointSettings;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<ServedMailOwnersStartupGate> logger;

    /// <summary>Initializes a new served-owner startup gate.</summary>
    /// <param name="scopeFactory">Creates the scope the owner directory and the provisioning are resolved from.</param>
    /// <param name="configuration">The configuration the owner declarations are read from.</param>
    /// <param name="servedOwners">The holder this gate publishes the roster into.</param>
    /// <param name="startupGates">The tracker this gate reports its completion to, which is what the startup probe reads.</param>
    /// <param name="mcpEndpointSettings">The MCP endpoint settings startup was composed from.</param>
    /// <param name="clientEndpointSettings">The client endpoint settings startup was composed from.</param>
    /// <param name="timeProvider">Supplies the date a declared synchronization bound is read against.</param>
    /// <param name="logger">The startup logger.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public ServedMailOwnersStartupGate(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ServedMailOwners servedOwners,
        HostStartupGates startupGates,
        IOptions<McpEndpointOptions> mcpEndpointSettings,
        IOptions<ClientEndpointOptions> clientEndpointSettings,
        TimeProvider timeProvider,
        ILogger<ServedMailOwnersStartupGate> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(servedOwners);
        ArgumentNullException.ThrowIfNull(startupGates);
        ArgumentNullException.ThrowIfNull(mcpEndpointSettings);
        ArgumentNullException.ThrowIfNull(clientEndpointSettings);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.scopeFactory = scopeFactory;
        this.configuration = configuration;
        this.servedOwners = servedOwners;
        this.startupGates = startupGates;
        this.mcpEndpointSettings = mcpEndpointSettings.Value;
        this.clientEndpointSettings = clientEndpointSettings.Value;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="DeploymentMailOwnerUnresolvedException">Thrown when the roster and the declarations cannot be reconciled into a set of owners this deployment may serve.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = this.scopeFactory.CreateAsyncScope();

        var declared = DeclaredOwners.ReadFrom(this.configuration);
        var directory = scope.ServiceProvider.GetRequiredService<IMailOwnerDirectory>();

        // One more than a deployment may hold, so that "more than the roster admits" is observable rather than
        // silently truncated into a roster this gate would then serve.
        var held = await directory.ReadOwnersAsync(DeclaredOwners.MaximumDeclaredOwners + 1, cancellationToken);

        if (held.Count > DeclaredOwners.MaximumDeclaredOwners)
        {
            throw DeploymentMailOwnerUnresolvedException.TooManyOwners(DeclaredOwners.MaximumDeclaredOwners);
        }

        var served = declared.Count == 0
            ? [await this.ServeTheSoleOwnerAsync(scope, held, cancellationToken)]
            : await this.ServeDeclaredOwnersAsync(scope, declared, held, cancellationToken);

        this.RefuseSeveralOwnersOnAnOwnerFacingSurface(served);

        this.servedOwners.Resolved(served);

        this.Report(served, held);

        this.startupGates.MarkCompleted(HostStartupGate.ServedMailOwners);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Serves the shape a deployment that declares no owner keeps, which is the one every upgrade arrives in.</summary>
    /// <remarks>
    /// The mail accounts stay in <c>MailSynchronization:Accounts</c> and no file has to change. The identifier is
    /// generated once and recorded, which the release's own migration ordinarily did; generating one here is what
    /// answers a database whose row is not there at all, and it is a version 4 value for the reason the column is —
    /// an owner identifier reaches administrative APIs, audit records, and logs, and a time-ordered one would publish
    /// when each owner was created and in what order.
    /// </remarks>
    private async Task<ServedMailOwner> ServeTheSoleOwnerAsync(
        AsyncServiceScope scope,
        IReadOnlyList<MailOwnerRecord> held,
        CancellationToken cancellationToken)
    {
        if (held.Count > 1)
        {
            throw DeploymentMailOwnerUnresolvedException.SeveralOwners();
        }

        if (held is not [var soleOwner])
        {
            var generated = MailOwnerId.Create(Guid.NewGuid());

            await scope.ServiceProvider
                .GetRequiredService<IMailOwnerProvisioning>()
                .ProvisionAsync(generated, SoleOwnerDisplayName, cancellationToken);

            this.LogSoleOwnerRecorded();

            return new ServedMailOwner(
                generated,
                SoleOwnerDisplayName,
                MailOwnerAccountSource.DeploymentSection,
                MailAccounts: []);
        }

        return soleOwner.DocumentWrittenAtRuntime
            ? await this.ServeFromTheOwnDocumentAsync(scope, soleOwner, cancellationToken)
            : new ServedMailOwner(
                soleOwner.Owner,
                soleOwner.DisplayName,
                MailOwnerAccountSource.DeploymentSection,
                MailAccounts: []);
    }

    /// <summary>Gives every declared owner their row, and serves each of them from the source their own row names.</summary>
    private async Task<IReadOnlyList<ServedMailOwner>> ServeDeclaredOwnersAsync(
        AsyncServiceScope scope,
        IReadOnlyList<DeclaredOwnerOptions> declared,
        IReadOnlyList<MailOwnerRecord> held,
        CancellationToken cancellationToken)
    {
        var provisioning = scope.ServiceProvider.GetRequiredService<IMailOwnerProvisioning>();
        var served = new List<ServedMailOwner>(declared.Count);

        foreach (var declaration in declared)
        {
            // Every declaration has already been proven to carry one, by the composed rules a start refuses before its
            // container exists, so the identifier is read rather than judged again here.
            var owner = MailOwnerId.Create(DeclaredOwners.TryReadIdentifier(declaration.Id)!.Value);
            var label = declaration.DisplayName.Trim();
            var record = held.FirstOrDefault(candidate => candidate.Owner == owner);

            // A label another owner is already recorded under is what the unique index refuses, whichever of the two
            // writes below would meet it. Refusing here is what turns a constraint violation into a sentence, and which
            // sentence it is depends on whether this owner has a row at all: without one the declaration is the same
            // person written down twice under a new identifier, and their mail would stay on the row nothing serves;
            // with one it is a label moving onto an owner while its holder still carries it.
            if (held.Any(candidate =>
                candidate.Owner != owner && StringComparer.Ordinal.Equals(candidate.DisplayName, label)))
            {
                throw record is null
                    ? DeploymentMailOwnerUnresolvedException.OwnerIdentifierChanged(label)
                    : DeploymentMailOwnerUnresolvedException.OwnerLabelHeldByAnother(label);
            }

            if (record is null)
            {
                await provisioning.ProvisionAsync(owner, label, cancellationToken);
                served.Add(new ServedMailOwner(owner, label, MailOwnerAccountSource.OwnerDeclaration, declaration.MailAccounts));

                continue;
            }

            if (!StringComparer.Ordinal.Equals(record.DisplayName, label))
            {
                await provisioning.RelabelAsync(owner, label, cancellationToken);
            }

            served.Add(record.DocumentWrittenAtRuntime
                ? await this.ServeFromTheOwnDocumentAsync(scope, record with { DisplayName = label }, cancellationToken)
                : new ServedMailOwner(owner, label, MailOwnerAccountSource.OwnerDeclaration, declaration.MailAccounts));
        }

        return served;
    }

    /// <summary>Serves one owner from the document their row holds, which is what an adoption made the source.</summary>
    /// <remarks>
    /// The document is put through the one binder both directions share, so what an owner's record is judged by here is
    /// what a write to it would be judged by. A record that will not bind stops the start rather than leaving that
    /// owner served from a section they have stopped reading: the alternative is a deployment quietly synchronizing the
    /// mailboxes an adoption was meant to replace.
    /// </remarks>
    private async Task<ServedMailOwner> ServeFromTheOwnDocumentAsync(
        AsyncServiceScope scope,
        MailOwnerRecord record,
        CancellationToken cancellationToken)
    {
        var document = await scope.ServiceProvider
            .GetRequiredService<IOwnerSettingsDocumentReader>()
            .ReadAsync(record.Owner, cancellationToken)
            ?? throw DeploymentMailOwnerUnresolvedException.OwnerRecordUnusable(
                record.DisplayName,
                ["The row it was read from is no longer there."]);

        var binding = scope.ServiceProvider
            .GetRequiredService<OwnerAccountDocumentBinder>()
            .Bind(document.Json);

        if (binding.Owner is not { } bound)
        {
            throw DeploymentMailOwnerUnresolvedException.OwnerRecordUnusable(record.DisplayName, binding.Refusals);
        }

        var windowErrors = OwnerMailAccountRules.FindSynchronizationWindowErrors(
            bound.MailAccounts,
            DateOnly.FromDateTime(this.timeProvider.GetUtcNow().UtcDateTime));

        return windowErrors.Count > 0
            ? throw DeploymentMailOwnerUnresolvedException.OwnerRecordUnusable(
                record.DisplayName,
                [.. windowErrors.Select(error => error.ErrorMessage ?? "A declared synchronization bound is unusable.")])
            : new ServedMailOwner(
                record.Owner,
                record.DisplayName,
                MailOwnerAccountSource.OwnerDocument,
                bound.MailAccounts);
    }

    /// <summary>Refuses a deployment whose owner-facing surfaces could not say which owner a caller acts for.</summary>
    /// <remarks>
    /// An owner-facing surface answers one person about their own mail, and nothing this release admits a caller with
    /// names the owner they act for: authentication-free operation admits them with no credential at all, and a
    /// configured credential authenticates without carrying an owner. Both leave a deployment serving several owners
    /// with no way to compose a caller, so the surface is refused rather than served against whichever owner a read
    /// happened to find.
    /// </remarks>
    private void RefuseSeveralOwnersOnAnOwnerFacingSurface(IReadOnlyList<ServedMailOwner> served)
    {
        var ownerFacingSurfaces =
            (this.mcpEndpointSettings.Enabled, this.clientEndpointSettings.Enabled) is (true, _) or (_, true);

        if (served.Count > 1 && ownerFacingSurfaces)
        {
            var authenticationDisabled =
                (this.mcpEndpointSettings.Enabled && !this.mcpEndpointSettings.RequiresAuthentication)
                || (this.clientEndpointSettings.Enabled && !this.clientEndpointSettings.RequiresAuthentication);

            throw DeploymentMailOwnerUnresolvedException.SeveralOwnersOnAnOwnerFacingSurface(authenticationDisabled);
        }
    }

    /// <summary>Reports which owners are served, where each is read from, and which held owners are not served at all.</summary>
    private void Report(IReadOnlyList<ServedMailOwner> served, IReadOnlyList<MailOwnerRecord> held)
    {
        var configuredOwnerCount = served.Count(owner => owner.ReadFromConfiguration);

        this.LogOwnersResolved(served.Count, configuredOwnerCount, served.Count - configuredOwnerCount);

        foreach (var owner in served.Where(owner => owner.Source == MailOwnerAccountSource.OwnerDocument))
        {
            this.LogOwnerReadFromTheirDocument(owner.DisplayName);
        }

        foreach (var record in held.Where(record => served.All(owner => owner.Owner != record.Owner)))
        {
            this.LogOwnerNotServed(record.DisplayName);
        }
    }

    /// <remarks>The record names no owner. The identity is a generated identifier for a person this deployment serves, and what an operator needs from this line is how the roster came out rather than who is on it.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment serves {ServedOwnerCount} owners: {ConfiguredOwnerCount} read from configuration and {AdoptedOwnerCount} from their own document.")]
    private partial void LogOwnersResolved(int servedOwnerCount, int configuredOwnerCount, int adoptedOwnerCount);

    /// <remarks>The label is the operator's own text for a row of their own file, which is what makes the line actionable: it is the owner whose declared section has stopped being applied.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The owner labelled {OwnerDisplayName} is read from their own document; no configuration source reaches their mail accounts. Change them with mfctl.")]
    private partial void LogOwnerReadFromTheirDocument(string ownerDisplayName);

    /// <remarks>A warning rather than information, because an owner the deployment holds and no longer serves keeps every message of theirs and synchronizes none of it, which is a state an operator meant either to reach or to notice.</remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The owner labelled {OwnerDisplayName} is held by this deployment and declared nowhere, so they are not served. Their mail is kept and neither read nor refreshed; removing them is an explicit act through mfctl.")]
    private partial void LogOwnerNotServed(string ownerDisplayName);

    /// <remarks>Reached only where the deployment holds no owner row at all, which the release's own migration ordinarily provisions.</remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "This deployment declared no owner and held none, so one has been recorded for the mail accounts it is configured with.")]
    private partial void LogSoleOwnerRecorded();
}
