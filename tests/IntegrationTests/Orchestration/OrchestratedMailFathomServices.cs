// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Emails;
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

    /// <summary>The width of the space the deterministic generator produces vectors in for this suite.</summary>
    /// <remarks>Deliberately narrow. Nothing here measures a distance, so a wider space would only make every stored row larger.</remarks>
    internal const int DeterministicEmbeddingDimension = 8;

    /// <summary>The width a passage is cut to before the deterministic generator hashes it.</summary>
    internal const int DeterministicEmbeddingInputCharacterLimit = 8_000;

    private readonly IHost host;

    private OrchestratedMailFathomServices(IHost host) => this.host = host;

    /// <summary>Starts the composed services against the orchestrated infrastructure.</summary>
    /// <param name="orchestration">The running orchestration whose database and mail server are used.</param>
    /// <param name="cancellationToken">Cancels the startup.</param>
    /// <param name="remotelyDeletedEmailDisposition">
    /// What the account does locally with an email its server no longer holds. It is left at the suite's default
    /// everywhere but the one class proving that this setting does not decide the outcome of a deletion MailFathom
    /// itself performed.
    /// </param>
    /// <param name="auditTrailEnabled">
    /// Whether the account keeps a durable record of the changes MailFathom made to it. Off everywhere but the class
    /// proving what the trail holds, which is the deployed default and keeps every other test from accumulating a
    /// history it never asked about.
    /// </param>
    /// <returns>The composed services, which the caller owns and must dispose.</returns>
    internal static async Task<OrchestratedMailFathomServices> StartAsync(
        MailFathomOrchestrationFixture orchestration,
        CancellationToken cancellationToken,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition =
            RemotelyDeletedEmailDisposition.RetainTombstone,
        bool auditTrailEnabled = false)
    {
        var builder = new HostApplicationBuilder();
        var account = new SyntheticMailAccount(
            orchestration.MailServer,
            remotelyDeletedEmailDisposition,
            auditTrailEnabled);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddOutboundResiliencePipelines(builder.Configuration.GetSection("Resilience"));
        builder.Services.AddSingleton<IImapAccountSettingsProvider>(account);
        builder.Services.AddSingleton<IMailOAuthSettingsProvider>(new UnconfiguredMailOAuthSettingsProvider());
        builder.Services.AddSingleton<IMailTransportSecurityPolicyReader>(account);
        builder.Services.AddSingleton<IMailSynchronizationWindowReader>(account);
        builder.Services.AddSingleton<IRemotelyDeletedEmailDispositionReader>(account);
        builder.Services.AddSingleton<IAuthoredDeleteEmailDispositionReader>(account);
        builder.Services.AddSingleton<IMailboxMutationAuditSettingsReader>(account);
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
        // Registered by the composition root rather than by AddInfrastructure, so the write-connection pool would fail
        // to resolve here for the same reason every bound setting above is supplied: the suite does not start the host.
        builder.Services.AddSingleton(new MailboxWriteSessionOptions());
        builder.Services.AddSingleton(new MailboxMutationOptions());
        builder.Services.AddSingleton(new MailboxConvergenceOptions());
        builder.Services.AddSingleton(new PersistenceConcurrencyOptions { MaximumCommitAttempts = 3 });
        // The bound a composition root reads from the Embeddings section. The backlog itself is registered by
        // AddInfrastructure, because every committed message is offered into it whether or not this deployment embeds.
        builder.Services.AddSingleton(new EmailEmbeddingBacklogOptions());
        // The bounds a composition root reads from the EmbeddingBackfill section. Small here on purpose: a test that
        // proves a walk is bounded needs the bound to be reachable within the mail it stored.
        builder.Services.AddSingleton(new StoredEmailEmbeddingBackfillOptions
        {
            BatchSize = 2,
            MaxBatchesPerRun = 10,
        });

        // Registered by a composition root rather than by AddInfrastructure, because persistence writes what the AI
        // boundary derives and may not reference it. Without this the chunk writer resolves nothing.
        builder.Services.AddLocalTextDerivations();
        // The generator ADR 0006 requires to exist so that everything downstream of the provider boundary — the
        // schema, the worker, the backfill, the generation switch — is provable against a real database at zero
        // provider cost. A test that activates a profile built from its identity embeds for nothing.
        builder.Services.AddDeterministicTextEmbeddings(
            DeterministicEmbeddingDimension,
            DeterministicEmbeddingInputCharacterLimit);
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(orchestration.DatabaseConnectionString, null, null),
            PostgresTextSearchConfiguration.Default);
        // Registered by a composition root for the reason the generator above is: AddInfrastructure registers neither
        // the embedding generation nor the backfill, because both resolve a text embedding generator an instance that
        // declared no chain does not have. This suite declared one, so it registers both.
        builder.Services.AddEmailEmbeddingGeneration();

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

    /// <summary>Runs one unit of work across two independent scopes, the way two writers reach the database at once.</summary>
    /// <typeparam name="TResult">What the unit of work produces.</typeparam>
    /// <param name="work">The unit of work, given a scope each.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the unit of work produced.</returns>
    /// <remarks>
    /// A competing writer is a second scope rather than a second session. The session factory begins its transaction on
    /// the scoped <c>MailFathomDbContext</c>, so two sessions taken from one scope would share that context, its
    /// connection, and its change tracker, and the second would be refused by EF Core before it reached the database at
    /// all. Two scopes give each writer the context, connection, and transaction a worker or a request would have, which
    /// is what leaves a database constraint free to be the thing that decides the race.
    /// </remarks>
    internal async Task<TResult> InTwoScopesAsync<TResult>(
        Func<IServiceProvider, IServiceProvider, CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken)
    {
        await using var first = this.host.Services.CreateAsyncScope();
        await using var second = this.host.Services.CreateAsyncScope();

        return await work(first.ServiceProvider, second.ServiceProvider, cancellationToken);
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
