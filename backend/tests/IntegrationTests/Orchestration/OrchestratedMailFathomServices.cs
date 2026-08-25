// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI;
using MailFathom.AI.Chat;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Spam;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Spam;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

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

    /// <summary>How many jobs of one type may be waiting before this suite's store refuses to enqueue another.</summary>
    /// <remarks>Far below the deployed default, because a test proving that backpressure is expressed has to be able to fill the queue.</remarks>
    internal const int JobQueueDepthPerType = 5;

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
    /// <param name="foldersWithoutEmbeddings">
    /// The folders this account is configured to leave unembedded, empty everywhere but the class proving that a
    /// message stored in one is cut into no passages at all.
    /// </param>
    /// <param name="foldersHiddenFromTools">
    /// The folders this account withholds from every tool, empty everywhere but the class proving that the narrowing
    /// this produces is one PostgreSQL evaluates.
    /// </param>
    /// <param name="foldersNotMirrored">
    /// The folders this account has stopped mirroring, empty everywhere but the class proving that mail such a folder
    /// still holds reaches no rule pass.
    /// </param>
    /// <param name="spamClassification">
    /// What this deployment decided about classifying mail, or <see langword="null" /> for the shipped default of
    /// classifying nothing. Stated only by the class that classifies, because the switch decides whether the use case
    /// does anything at all.
    /// </param>
    /// <param name="spamScanner">
    /// The daemon a scanned classification is scored against, or <see langword="null" /> for a deployment that deployed
    /// no scanner. Absent everywhere but the class that scores, exactly as a deployment which never switched the scanner
    /// on registers no implementation of the port.
    /// </param>
    /// <param name="contactCollection">
    /// What this deployment decided about collecting contacts from arriving mail, or <see langword="null" /> for the
    /// shipped default of collecting nobody. Stated only by the class that collects, because every other class stores
    /// mail whose senders must not end up in the book it reads.
    /// </param>
    /// <param name="filesSentCopies">
    /// Whether this account files a copy of what it sends into a folder of its own, and therefore maps one to the sent
    /// role. Off everywhere but the two classes that need the copy to exist — the one proving it is appended once and
    /// comes back recognized, and the one proving a promoted draft leaves the drafts folder while the sent copy stays
    /// — because a deployment that files one appends a message on every send the collection makes.
    /// </param>
    /// <param name="keepsDrafts">
    /// Whether this account keeps drafts in a folder of its own, and therefore maps one to the drafts role. Off
    /// everywhere but the class proving that an edit leaves one draft and a promotion leaves none, because a deployment
    /// that maps the role would let any other class's draft reach a folder nothing else here maps.
    /// </param>
    /// <param name="storesContentInObjectStorage">
    /// Whether the composition selects the object backend for message content. Off by default, which is the deployment
    /// that writes payloads into its own tables; a test about the second backend turns it on and gets the orchestrated
    /// endpoint the fixture started.
    /// </param>
    /// <returns>The composed services, which the caller owns and must dispose.</returns>
    internal static async Task<OrchestratedMailFathomServices> StartAsync(
        MailFathomOrchestrationFixture orchestration,
        CancellationToken cancellationToken,
        RemotelyDeletedEmailDisposition remotelyDeletedEmailDisposition =
            RemotelyDeletedEmailDisposition.RetainTombstone,
        bool auditTrailEnabled = false,
        bool answeringAuditTrailEnabled = false,
        IChatClient? answeringChatClient = null,
        IReadOnlyList<MailFolderIdentity>? foldersWithoutEmbeddings = null,
        IReadOnlyList<MailFolderIdentity>? foldersHiddenFromTools = null,
        IReadOnlyList<MailFolderIdentity>? foldersNotMirrored = null,
        SpamClassificationSettings? spamClassification = null,
        SpamAssassinScannerProfile? spamScanner = null,
        ContactCollectionSettings? contactCollection = null,
        bool filesSentCopies = false,
        bool keepsDrafts = false,
        bool storesContentInObjectStorage = false)
    {
        var builder = new HostApplicationBuilder();
        var account = new SyntheticMailAccount(
            orchestration.MailServer,
            remotelyDeletedEmailDisposition,
            auditTrailEnabled,
            answeringAuditTrailEnabled,
            foldersWithoutEmbeddings,
            foldersHiddenFromTools,
            foldersNotMirrored,
            filesSentCopies,
            keepsDrafts);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSecretResolution(SecretValueInterpretation.ReferenceOnly);
        builder.Services.AddOutboundResiliencePipelines(builder.Configuration.GetSection("Resilience"));
        builder.Services.AddSingleton<IImapAccountSettingsProvider>(account);
        // Where the same mailbox's mail is submitted, registered by the composition root from the same options section.
        // The delivery session factory resolves it, so a harness without it would fail to compose rather than behave
        // like a deployment that configured no submission endpoint.
        builder.Services.AddSingleton<ISmtpAccountSettingsProvider>(new SyntheticSubmissionAccount(orchestration.MailServer));
        // The port every mailbox read resolves its scope through, registered by the composition root from the same
        // options section the account above comes from. Without it a search or a listing resolves nothing rather than
        // narrowing to this account, so it belongs here with the other host-bound ports.
        // Who the mailbox's own mail is written from, registered by the composition root from the same options
        // section. Every delivery attempt reads it before it opens anything, so an account without it delivers nothing
        // rather than delivering as somebody unnamed.
        builder.Services.AddSingleton<IOutgoingSenderIdentityReader>(account);
        // Whether an account files a copy of what it sends, registered by the composition root from the same options
        // section. Every outbox pass resolves it after an attempt settles, so a harness without it would fail to
        // compose rather than behave like a deployment that files nothing.
        builder.Services.AddSingleton<IOutgoingMailFilingPolicyReader>(account);
        // What this deployment may send at all, registered by the composition root from the Deployment section and the
        // account's own switch. The three below are one decision the outbox asks before it writes anything down, so a
        // harness without them would fail to compose rather than behave like a deployment that turned sending on: this
        // suite is the installation that did, writes to anybody, and declares no ceiling. A test about any of the three
        // states its own posture rather than reading it from here.
        builder.Services.AddSingleton<IOutgoingSendPermissionReader>(account);
        builder.Services.AddSingleton(OutgoingRecipientPolicy.Unrestricted);
        builder.Services.AddSingleton(OutgoingMailCeilings.Unbounded);
        // How large a message this deployment composes, which a composition root reads from the same section and
        // registers per scope. The shipped defaults, because nothing here is written to reach one of them: what a
        // refused bound does is decided without a database and is covered where that decision is.
        builder.Services.AddScoped(_ => new OutgoingEmailBounds
        {
            MaxRecipientCount = 50,
            MaxBodyCharacters = 100_000,
            MaxAttachmentCount = 10,
            MaxAttachmentBytes = 10L * 1024 * 1024,
            MaxMessageBytes = 25L * 1024 * 1024,
        });
        // The two bounds a caller meets, registered by the composition root from the same section. They exist only
        // where a caller does, which nothing composed here is, so the harness is the deployment that bounded no client
        // and admits a recipient it holds no record of; a test about either states its own posture.
        builder.Services.AddSingleton(AuthoredSendCeilings.Unbounded);
        builder.Services.AddSingleton(AuthoredSendSettings.Permissive);
        // What one pass over an account's outbox is allowed to do, which a composition root validates out of the
        // MailDelivery section. The values are the deployed defaults with the two the suite has to be able to reach
        // narrowed: a batch small enough for a test to fill, and a retry ceiling a test can exhaust without waiting.
        builder.Services.AddSingleton(MailOutboxSettings.Create(
            maxDeliveriesPerPass: 5,
            leaseDuration: TimeSpan.FromMinutes(10),
            attemptTimeout: TimeSpan.FromMinutes(1),
            maxAttempts: 2,
            retryBaseDelay: TimeSpan.FromMinutes(1),
            retryMaxDelay: TimeSpan.FromMinutes(5),
            allowedLateness: TimeSpan.FromHours(8)));
        // The queue an authored send is announced on. Nothing in this suite reads it — the pass is invoked directly —
        // so it is here because the outbox writes to it, and its depth is what a test reads to see that the write
        // announced anything at all.
        builder.Services.AddSingleton(new MailOutboxSignal(capacity: 16));
        builder.Services.AddSingleton<IDeploymentMailAccountCatalog>(account);
        // Whose accounts those are. A composed host settles this from its owner records while it starts and nothing here
        // starts one, so the harness states it; the caller-scoped catalog the infrastructure registers compares it
        // against the owner a caller is admitted for, which is what makes a mailbox read here run through the same
        // narrowing a deployment's does.
        builder.Services.AddSingleton<IDeploymentMailOwnerSource>(new OrchestratedDeploymentOwner());
        // The port every folder decision is read through, registered by the composition root from the same options
        // section the account above comes from. Chunking and every mailbox read resolve it, so a harness without it
        // would fail to compose rather than behave like a deployment that configured no folder switch.
        builder.Services.AddSingleton<IMailFolderParticipationReader>(account);
        // The port a folder named by its role is resolved through, registered by the composition root from the same
        // options section. Every mailbox read composes a reference resolver over it, so a harness without it would fail
        // to compose rather than behave like a deployment whose folders carry no role.
        builder.Services.AddSingleton<IMailFolderMappingReader>(account);
        // Which of an account's folders its server files junk into, read from the same mappings and registered by the
        // composition root for the same reason. Every mailbox scope, the classification, and the gate in front of
        // derived work resolve it, so a harness without it fails to compose rather than behaving like the deployment
        // that maps no junk folder — which is the deployment this account is.
        builder.Services.AddSingleton<IJunkMailFolderCatalog>(account);
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
        // Which server's sender-authentication statements an account believes, registered by the composition root from
        // the same options section. The MIME reader resolves it for every extraction, so a harness without it would fail
        // to compose rather than behave like the deployment that names no such server — which is the deployment this
        // account is.
        builder.Services.AddSingleton<ITrustedAuthenticationAuthorityReader>(account);
        // Beside it because the same MIME reader resolves both: the trusted authority decides who authenticated the
        // author, and this decides which authenticated authors the account recognizes. Registered here for the same
        // reason, too — a composition without it fails to resolve the reader rather than behaving like the account that
        // recognizes nobody, which is the account this suite composes.
        builder.Services.AddSingleton<ISenderTrustPolicyReader>(account);
        // The third thing that reader resolves, and the shipped default rather than a choice made here: assessing how a
        // message was written is on unless an operator turns it off, and the disabled profile records the same
        // not-assessed state a message with no readable body reaches. A composition without it fails to resolve the
        // reader, which reaches a test as every extraction in the class failing at once.
        builder.Services.AddSingleton(MachineAuthorshipProfile.Standard);
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
        // Where attachment links point and how long they live, which a composition root reads from the EmailContent
        // section. Declared here rather than left null so the suite exercises the path that mints a capability; the
        // address is synthetic because nothing in this suite fetches one over the network.
        builder.Services.AddSingleton(new AttachmentDownloadSettings(
            new Uri("https://mailfathom.integration.test/attachments/"),
            TimeSpan.FromMinutes(10)));
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
        // The capacity a composition root reads from the Jobs section, of which only the queue depth reaches this
        // suite: the store refuses an enqueue against it, and the concurrency ceilings belong to a worker nothing here
        // starts. The depth is small for the reason the batch sizes above are — a bound a test proves has to be one a
        // test can reach — and still comfortably above what any one test leaves waiting behind it.
        builder.Services.AddSingleton(JobCapacitySettings.Create(
            maxConcurrentJobs: 2,
            maxConcurrentJobsPerType: 1,
            JobQueueDepthPerType));
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
        // The extraction walk's own bounds, read by a composition root from the MailExtractionBackfill section. Left at
        // the shipped values, because a test that pages this walk states its batch size on the call and what matters
        // here is the rebuild switch: off, which is the deployment every test runs under but the one that constructs
        // the store itself to prove what turning it on re-derives.
        builder.Services.AddSingleton(new StoredEmailExtractionBackfillOptions());

        // Registered by a composition root rather than by AddInfrastructure, because persistence writes what the AI
        // boundary derives and may not reference it. Without this the chunk writer resolves nothing.
        builder.Services.AddLocalTextDerivations();
        // The generator ADR 0006 requires to exist so that everything downstream of the provider boundary — the
        // schema, the worker, the backfill, the generation switch — is provable against a real database at zero
        // provider cost. A test that activates a profile built from its identity embeds for nothing.
        builder.Services.AddDeterministicTextEmbeddings(
            DeterministicEmbeddingDimension,
            DeterministicEmbeddingInputCharacterLimit);
        // What a use case is told admitted the work. A composition root supplies it from the request being served, and
        // this suite serves none: every class it exercises is driven directly, which is work no caller requested and is
        // exactly what the process identity names, until a test acting as an agent states a caller for its own scope.
        builder.Services.AddScoped(_ => new StatedAuthorizedPrincipalSource());
        builder.Services.AddScoped<IAuthorizedPrincipalSource>(provider =>
            provider.GetRequiredService<StatedAuthorizedPrincipalSource>());
        builder.Services.AddInfrastructure(
            _ => new PostgresConnectionSettings(orchestration.DatabaseConnectionString, null, null),
            PostgresTextSearchConfiguration.Default,
            MailAnsweringBudget.Default);

        // The object backend, registered exactly the way a composition root registers it — which is what makes its
        // presence the selection: the content store asks the container for an object store and writes to the database
        // when it is absent, so a harness that composed one unconditionally would leave the database backend untested.
        if (storesContentInObjectStorage)
        {
            builder.Services.AddSingleton<IObjectStorageCredentialSource,
                OrchestratedObjectStorage.StatedCredentialSource>();
            builder.Services.AddObjectStorage(
                OrchestratedObjectStorage.EndpointAt(orchestration.ObjectStorage),
                OrchestratedObjectStorage.ReclamationBounds,
                configuredTrustAnchor: null);
        }
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

        // The port a composition root registers from the SpamClassification section, and the one place a harness has to
        // supply it: infrastructure registers the classifier, the classifier asks this what the operator decided, and a
        // composition without it would fail to resolve rather than behave like a deployment that classifies nothing.
        builder.Services.AddSingleton<ISpamClassificationSettingsReader>(
            new FixedSpamClassificationSettingsReader(spamClassification ?? SpamClassificationSettings.Disabled));

        // The same arrangement for the other decision a composition root reads out of the account's own section: the
        // synchronizer resolves the collector for every folder run, the collector asks this what the owner switched on,
        // and a composition without it would fail to resolve rather than behave like the deployment that collects
        // nobody — which is the deployment every test here composes unless it says otherwise.
        builder.Services.AddSingleton<IContactCollectionSettingsReader>(
            new FixedContactCollectionSettingsReader(contactCollection ?? ContactCollectionSettings.CollectingNothing));

        // Registered exactly as the composition root registers it, and only where a scanner was named — so a test that
        // states none composes the deployment that deployed no sidecar, where the classifier resolves no scanner and the
        // deterministic stage is the whole feature.
        if (spamScanner is not null)
        {
            builder.Services.AddSingleton(spamScanner);
            builder.Services.AddSpamAssassinScanning();
        }

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

    /// <summary>Runs one unit of work in its own scope, as an admitted caller holding exactly the permissions named.</summary>
    /// <typeparam name="TResult">What the unit of work produces.</typeparam>
    /// <param name="work">The unit of work.</param>
    /// <param name="grantedPermissions">What the entry that admitted the caller resolved to, which is empty for a caller granted nothing.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>What the unit of work produced.</returns>
    /// <remarks>
    /// For a use case an agent reaches rather than a worker. The scope is composed exactly as
    /// <see cref="InScopeAsync{TResult}" /> composes one and the caller is stated into it before the work runs, so what
    /// is exercised is the production graph answering a request rather than a substitute standing in for one.
    /// </remarks>
    internal Task<TResult> AsCallerInScopeAsync<TResult>(
        Func<IServiceProvider, CancellationToken, Task<TResult>> work,
        IEnumerable<MailFathomPermission> grantedPermissions,
        CancellationToken cancellationToken) => this.InScopeAsync(
            (scope, token) =>
            {
                scope.GetRequiredService<StatedAuthorizedPrincipalSource>()
                    .Assume(AuthorizedPrincipal.CallerActingFor(
                        OrchestratedDeploymentOwner.ServedOwner,
                        "orchestrated-caller",
                        grantedPermissions));

                return work(scope, token);
            },
            cancellationToken);

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

    /// <summary>Runs one write in its own scope and session, and hands back the value the write produced.</summary>
    /// <typeparam name="TResult">What the write produces, such as the identifier the repository assigned.</typeparam>
    /// <param name="write">The repository calls that join the session, answering with the value the caller needs.</param>
    /// <param name="cancellationToken">Cancels the write and the commit.</param>
    /// <returns>What the write produced, once its session committed.</returns>
    /// <remarks>
    /// The scope, the session, and the ordering are <see cref="CommitAsync" />'s. What differs is which of the two
    /// values reaches the caller: that method answers with the commit result, which is what a test asserting a conflict
    /// needs and what leaves a test needing the written identifier with nowhere to read it from. Here the commit is
    /// asserted instead, so an arrangement that silently conflicted fails where it happened rather than at whatever the
    /// caller asserts about the value next.
    /// </remarks>
    internal Task<TResult> CommitProducingAsync<TResult>(
        Func<IServiceProvider, IPersistenceSession, CancellationToken, Task<TResult>> write,
        CancellationToken cancellationToken) => this.InScopeAsync(
            async (scope, token) =>
            {
                await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                var produced = await write(scope, session, token);

                Assert.Equal(PersistenceCommitResult.Committed, await session.CommitAsync(token));

                return produced;
            },
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this.host.StopAsync(CancellationToken.None);
        this.host.Dispose();
    }
}
