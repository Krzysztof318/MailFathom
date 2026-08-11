// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI;
using MailFathom.AI.Chat;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
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
using Microsoft.Extensions.AI;
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

    /// <summary>How much of one message's text this suite cuts into passages.</summary>
    /// <remarks>
    /// Small on purpose, for the reason the backfill's batch sizes are: a test that proves a message is cut to the
    /// ceiling needs the ceiling to be reachable inside a body it can store. It stays well above the bodies the other
    /// tests write, so their cuts are unchanged by it.
    /// </remarks>
    internal const int EmbeddingInputCharacterCeiling = 5_000;

    /// <summary>The declaration an answering run of this suite is conducted under.</summary>
    /// <remarks>
    /// The address is in the reserved <c>.invalid</c> domain and nothing ever dials it: the provider is scripted, and
    /// the plan exists because the composition maps it onto the generation parameters every turn carries. The
    /// conversation bounds are generous enough that a run making several lookups is never stopped by the bound a single
    /// request carries, which is a different ceiling and not what these tests are about.
    /// </remarks>
    private static readonly ChatGenerationPlan AnsweringChatPlan = ChatGenerationPlan.Create(
        new ChatEndpoint(
            "integration-answering",
            new Uri("https://provider.invalid/v1/", UriKind.Absolute),
            "a-scripted-chat-model",
            ChatProviderApi.ChatCompletions),
        maximumOutputTokens: 256,
        temperature: null,
        topP: null,
        reasoningEffort: null,
        maximumMessagesPerRequest: 32,
        maximumRequestCharacters: 200_000,
        requestTimeout: TimeSpan.FromSeconds(30));

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
    /// <param name="answeringAuditTrailEnabled">
    /// Whether the account keeps a durable record of the questions answered from its mailbox. Off everywhere but the
    /// class proving what that record holds, which is the deployed default and keeps every other test from
    /// accumulating one it never asked about.
    /// </param>
    /// <param name="auditTrailEnabled">
    /// Whether the account keeps a durable record of the changes MailFathom made to it. Off everywhere but the class
    /// proving what the trail holds, which is the deployed default and keeps every other test from accumulating a
    /// history it never asked about.
    /// </param>
    /// <param name="answeringChatClient">
    /// The provider an answering run is conducted through, or <see langword="null" /> for a deployment that answers no
    /// questions. Absent everywhere but the class that asks one, because a registered answerer is what makes the
    /// capability report that this instance answers at all.
    /// </param>
    /// <returns>The composed services, which the caller owns and must dispose.</returns>
    internal static async Task<OrchestratedMailFathomServices> StartAsync(
        MailFathomOrchestrationFixture orchestration,
        CancellationToken cancellationToken,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition =
            RemotelyDeletedEmailDisposition.RetainTombstone,
        bool auditTrailEnabled = false,
        bool answeringAuditTrailEnabled = false,
        IChatClient? answeringChatClient = null)
    {
        var builder = new HostApplicationBuilder();
        var account = new SyntheticMailAccount(
            orchestration.MailServer,
            remotelyDeletedEmailDisposition,
            auditTrailEnabled,
            answeringAuditTrailEnabled);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddOutboundResiliencePipelines(builder.Configuration.GetSection("Resilience"));
        builder.Services.AddSingleton<IImapAccountSettingsProvider>(account);
        // The port every mailbox read resolves its scope through, registered by the composition root from the same
        // options section the account above comes from. Without it a search or a listing resolves nothing rather than
        // narrowing to this account, so it belongs here with the other host-bound ports.
        builder.Services.AddSingleton<IMailAccountCatalog>(account);
        // How much of a message's body one search result may show, which a composition root composes from the
        // MailboxSearch section. It is the deployment's control on what a query draws out of a mailbox rather than a
        // request's, so the shipped default is what this suite searches under.
        builder.Services.AddSingleton(EmailSearchSnippetBounds.Default);
        builder.Services.AddSingleton<IMailOAuthSettingsProvider>(new UnconfiguredMailOAuthSettingsProvider());
        builder.Services.AddSingleton<IMailTransportSecurityPolicyReader>(account);
        builder.Services.AddSingleton<IMailSynchronizationWindowReader>(account);
        builder.Services.AddSingleton<IRemotelyDeletedEmailDispositionReader>(account);
        builder.Services.AddSingleton<IAuthoredDeleteEmailDispositionReader>(account);
        builder.Services.AddSingleton<IMailboxMutationAuditSettingsReader>(account);
        builder.Services.AddSingleton<IMailAnsweringAuditSettingsReader>(account);
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
        // The process-wide buffer bound, registered by the composition root for the same reason. It is generous here
        // because the suite runs one work unit at a time and nothing it asserts is about waiting for the budget.
        builder.Services.AddSingleton(new RawMimeMemoryBudget(64L * 1024L * 1024L));
        builder.Services.AddSingleton(new StoredContentCeiling(ceilingBytes: null));
        builder.Services.AddSingleton(new MailboxMutationOptions());
        builder.Services.AddSingleton(new MailboxConvergenceOptions());
        builder.Services.AddSingleton(new PersistenceConcurrencyOptions { MaximumCommitAttempts = 3 });
        // The bound a composition root reads from the Embeddings section. The backlog itself is registered by
        // AddInfrastructure, because every committed message is offered into it whether or not this deployment embeds.
        builder.Services.AddSingleton(new EmailEmbeddingBacklogOptions());
        // The three ceilings a composition root reads from the same section. The suite embeds against a deterministic
        // in-repository generator that reaches no provider, so two of them are nothing to bound here: the budget bounds
        // nothing and the pacer delays nothing, which keeps a suite that starts a container from also waiting out a
        // rate. The per-message bound is reachable rather than shipped, for the reason its constant states.
        builder.Services.AddSingleton(EmbeddingInputBound.Create(EmbeddingInputCharacterCeiling));
        builder.Services.AddSingleton(EmbeddingSpendBudget.Unbounded);
        builder.Services.AddSingleton(EmbeddingRequestPacer.Create(
            maxRequestsPerMinute: 0,
            TimeProvider.System));
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
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);
        // Registered by a composition root for the reason the generator above is: AddInfrastructure registers neither
        // the embedding generation nor the backfill, because both resolve a text embedding generator an instance that
        // declared no chain does not have. This suite declared one, so it registers both.
        builder.Services.AddEmailEmbeddingGeneration();

        // The answering half, registered only for a test that asks a question. A composition root adds the answerer
        // exactly where a chat endpoint was declared, and its absence is what makes every other test's deployment
        // report that it answers none — which is the shipped default and what those tests are written against.
        if (answeringChatClient is { } chatClient)
        {
            builder.Services.AddScoped<IMailQuestionAnswerer>(provider => new ComposedMailQuestionAnswerer(
                provider.GetRequiredService<IEmailKnowledgeSearch>(),
                provider.GetRequiredService<MailAnsweringRunBounds>(),
                AnsweringChatPlan,
                chatClient));
        }

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
