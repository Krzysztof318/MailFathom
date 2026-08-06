// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.Resolution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>The production registrations, resolved against the orchestrated database and mail server.</summary>
/// <remarks>
/// <para>
/// A real host rather than a bare service provider, because infrastructure composes the connection string during
/// hosted-service startup so that resolving a secret reference stays asynchronous. Everything MailFathom itself
/// registers comes from <see cref="ServiceCollectionExtensions.AddInfrastructure" />, so what a test exercises is the
/// wiring a deployment gets; only the inputs a composition root binds from configuration are supplied here, because the
/// suite deliberately does not start the host resource.
/// </para>
/// <para>
/// Batching is left at values a test can reason about rather than at production defaults: one run must be able to
/// consume a seeded mailbox whole, so a checkpoint a later test reads describes the whole folder rather than the first
/// page of it.
/// </para>
/// </remarks>
internal sealed class OrchestratedMailFathomServices : IAsyncDisposable
{
    /// <summary>The identifier every value this suite seals is written under.</summary>
    internal const string DataEncryptionKeyId = "integration-tests";

    /// <summary>The key this suite seals with, base64 of the ASCII text <c>mailfathom-integrationtests-key!</c>.</summary>
    /// <remarks>A literal under the same restriction as the orchestrated mailbox's password: it protects synthetic data in a database created and destroyed by one run, and it unlocks nothing anything else holds.</remarks>
    internal const string DataEncryptionKeyMaterial = "bWFpbGZhdGhvbS1pbnRlZ3JhdGlvbnRlc3RzLWtleSE=";

    private readonly IHost host;

    private OrchestratedMailFathomServices(IHost host) => this.host = host;

    /// <summary>Starts the composed services against the orchestrated infrastructure.</summary>
    /// <param name="orchestration">The running orchestration whose database and mail server are used.</param>
    /// <param name="cancellationToken">Cancels the startup.</param>
    /// <returns>The composed services, which the caller owns and must dispose.</returns>
    internal static async Task<OrchestratedMailFathomServices> StartAsync(
        MailFathomOrchestrationFixture orchestration,
        CancellationToken cancellationToken)
    {
        var builder = new HostApplicationBuilder();
        var account = new SyntheticMailAccount(orchestration.MailServer);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddOutboundResiliencePipelines(builder.Configuration.GetSection("Resilience"));
        builder.Services.AddSingleton<IImapAccountSettingsProvider>(account);
        builder.Services.AddSingleton<IMailOAuthSettingsProvider>(new UnconfiguredMailOAuthSettingsProvider());
        builder.Services.AddSingleton<IMailTransportSecurityPolicyReader>(account);
        builder.Services.AddSingleton<IMailSynchronizationWindowReader>(account);
        builder.Services.AddSingleton<IRemotelyDeletedEmailDispositionReader>(account);
        builder.Services.AddSingleton(new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = 50,
            MaxRawMimeBytes = 1024L * 1024L,
            MaxMetadataBatchesPerRun = 10,
        });
        builder.Services.AddSingleton(new EmailMimeExtractionOptions
        {
            MaxPartCount = 100,
            MaxNestingDepth = 10,
            MaxExtractedTextCharacters = 10_000,
        });
        builder.Services.AddSingleton(new EmailContentReadOptions { MaxBodyCharacters = 10_000 });
        builder.Services.AddSingleton(new PersistenceConcurrencyOptions { MaximumCommitAttempts = 3 });

        // Registered by a composition root rather than by AddInfrastructure, because persistence writes what the AI
        // boundary derives and may not reference it. Without this the chunk writer resolves nothing.
        builder.Services.AddLocalTextDerivations();
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(orchestration.DatabaseConnectionString, null, null),
            PostgresTextSearchConfiguration.Default);

        // The ring a composition root would build from the DataEncryption section. It is supplied here for the reason
        // every other bound setting above is: the suite does not start the host resource, so nothing else binds it, and
        // a store that seals would otherwise fail to resolve its encryptor rather than fail to seal.
        builder.Services.AddDataEncryption(_ => new DataEncryptionKeyRingSettings(
            DataEncryptionKeyId,
            [
                new DataEncryptionKeyReference(
                    DataEncryptionKeyId,
                    new ConfiguredSecret
                    {
                        Name = "integration-tests-data-key",
                        SecretReference = $"plaintext:{DataEncryptionKeyMaterial}",
                    }),
            ]));

        var host = builder.Build();
        await host.StartAsync(cancellationToken);

        return new OrchestratedMailFathomServices(host);
    }

    /// <summary>Runs one unit of work in its own dependency-injection scope, the way a worker does.</summary>
    /// <typeparam name="TResult">What the unit of work produces.</typeparam>
    /// <param name="work">The unit of work.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the unit of work produced.</returns>
    internal async Task<TResult> InScopeAsync<TResult>(
        Func<IServiceProvider, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        await using var scope = this.host.Services.CreateAsyncScope();

        return await work(scope.ServiceProvider, cancellationToken);
    }

    /// <summary>Runs one write in its own scope and session, and commits it the way a use case does.</summary>
    /// <param name="write">The repository calls that join the session.</param>
    /// <param name="cancellationToken">Cancels the write and the commit.</param>
    /// <returns>What the commit reported, so a caller can assert a conflict rather than only a success.</returns>
    /// <remarks>
    /// The session is disposed after the commit, which is the ordering a use case uses: a committed or conflicted
    /// session rolls nothing back, and a write that threw is rolled back by that disposal.
    /// </remarks>
    internal Task<PersistenceCommitResult> CommitAsync(
        Func<IServiceProvider, IPersistenceSession, CancellationToken, Task> write,
        CancellationToken cancellationToken) => this.InScopeAsync(
            async (scope, token) =>
            {
                await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                await write(scope, session, token);

                return await session.CommitAsync(token);
            },
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.host.StopAsync(CancellationToken.None);
        this.host.Dispose();
    }
}
