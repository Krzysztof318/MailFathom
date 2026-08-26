// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Spam.Runs;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Composes the scoped graph a synchronization work unit resolves, without the real host around it.</summary>
/// <remarks>
/// The coordinator and its supervisors create a scope per folder and resolve the synchronizer from it, so a test of
/// either needs a real container. Everything the synchronizer would reach — the mail server, the database, the MIME
/// reader — is substituted, which leaves the scheduling, isolation, and bounding decisions as the only behavior under
/// test.
/// </remarks>
internal static class SynchronizationTestHost
{
    /// <summary>Configures one account whose folder aliases and remote paths are the same text, so a test names a folder once.</summary>
    internal static MailSynchronizationAccountOptions CreateAccount(string accountId, params string[] folders) => new()
    {
        AccountId = accountId,
        DisplayName = $"The {accountId} mailbox",
        Host = "imap.example.test",
        UserName = "mailfathom@example.test",
        Folders = [.. folders.Select(folder => new MailFolderMappingOptions { Alias = folder, RemotePath = folder })],
        Secrets = new MailAccountSecretOptions
        {
            Password = new ConfiguredSecret { SecretReference = $"systemd-credential:imap-{accountId}-password" },
        },
    };

    /// <summary>Configures the accounts a run is scheduled from, on the defaults every bound the tests do not exercise takes.</summary>
    internal static MailSynchronizationOptions CreateOptions(bool enabled, params MailSynchronizationAccountOptions[] accounts) => new()
    {
        Enabled = enabled,
        Interval = TimeSpan.FromMinutes(5),
        Accounts = [.. accounts],
    };

    /// <summary>Configures the single account most tests need, named <c>primary</c>.</summary>
    internal static MailSynchronizationOptions CreateSingleAccountOptions(bool enabled, params string[] folders) =>
        CreateOptions(enabled, CreateAccount("primary", folders));

    /// <summary>Builds the container a coordinator or a supervisor resolves its work-unit scopes from.</summary>
    /// <param name="options">The snapshot the scoped graph is configured from.</param>
    /// <param name="publishedSettings">The snapshot holder a scope falls back to when no run handed one down.</param>
    /// <param name="sessionFactory">Stands in for the mail server.</param>
    /// <param name="timeProvider">The clock the scoped graph shares with the code under test.</param>
    /// <param name="notificationSessionFactory">Stands in for the server's push mechanism; a server that advertises none is the default.</param>
    /// <param name="remoteFolderCatalog">Replaces the catalog that advertises exactly the configured folders.</param>
    /// <param name="mutationRecordStore">Replaces the record store that reports nothing outstanding to converge.</param>
    /// <param name="folderMirrorStore">Replaces the store an unmirrored folder's local copy would be erased through, which no run reaches.</param>
    /// <param name="ruleEvaluationStore">Replaces the store a rule pass reads its candidates from; one with nothing to evaluate is the default.</param>
    /// <param name="classificationRunStore">Replaces the store the classification pass reads its outstanding run from; one with nothing outstanding is the default.</param>
    /// <param name="chunkingStore">Replaces the store the cut reads its candidates from; one with nothing awaiting passages is the default.</param>
    /// <param name="unadvertisedAliases">Aliases the modelled server does not advertise.</param>
    /// <returns>A provider whose scopes resolve a synchronizer over substituted infrastructure.</returns>
    internal static ServiceProvider BuildServiceProvider(
        MailSynchronizationOptions options,
        ISettingsSnapshot<MailSynchronizationOptions> publishedSettings,
        IMailboxSessionFactory sessionFactory,
        TimeProvider timeProvider,
        IMailboxNotificationSessionFactory? notificationSessionFactory = null,
        IRemoteFolderCatalog? remoteFolderCatalog = null,
        IMailboxMutationRecordStore? mutationRecordStore = null,
        IStoredMailFolderMirrorStore? folderMirrorStore = null,
        IMailRuleEvaluationStore? ruleEvaluationStore = null,
        ISpamClassificationRunStore? classificationRunStore = null,
        IStoredEmailChunkingStore? chunkingStore = null,
        params string[] unadvertisedAliases)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sessionFactory);
        services.AddSingleton(
            notificationSessionFactory ?? new FakeMailboxNotificationSessionFactory(timeProvider) { AdvertisesPush = false });
        services.AddSingleton(Substitute.For<ISynchronizationCheckpointStore>());
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(Substitute.For<IEmailMetadataRepository>());
        services.AddSingleton(Substitute.For<IEmailContentStore>());
        services.AddSingleton(Substitute.For<IStoredEmailContentInventory>());
        services.AddSingleton<IOwnerStoredContentLedger>(new InMemoryOwnerStoredContentLedger());
        services.AddSingleton<IMailOwnership>(new StubMailOwnership());
        // Bounded generously, so no test here waits on a budget it never meant to exercise: what these tests are about
        // is the supervisor's scheduling and failure isolation, and the budget itself is asserted where it lives.
        services.AddSingleton(new RawMimeMemoryBudget(long.MaxValue));
        services.AddSingleton(new StoredContentCeiling(ceilingBytes: null));
        services.AddSingleton(CreateMimeReaderThatExtractsEverything());
        services.AddSingleton(CreateReconciliationStoreWithNothingToDo());
        services.AddSingleton(CreateMutationStoreWithNothingRecorded());
        services.AddSingleton(CreateFilingStoreWithNothingFiled());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(new MailboxSynchronizationOptions());
        services.AddSingleton(timeProvider);
        // The run's cut offers each message it cuts for embedding, so the pass cannot be composed without somewhere to
        // offer it. Nothing here reads the backlog back; these tests are about the run, not about what embeds afterwards.
        services.AddSingleton<IEmailEmbeddingBacklog>(new ScriptedEmailEmbeddingBacklog());
        services.AddSingleton<IDerivedWorkGateTelemetry>(new RecordingDerivedWorkGateTelemetry());
        // The classifier below reaches this to take a junk message's passages away again, which is the one thing the
        // port does; nothing in these tests scores a message, so it is composed and never called.
        services.AddSingleton(Substitute.For<IEmailChunkStore>());
        // The gate's own two dependencies are registered with the classification pass further down rather than again
        // here — the container resolves the last registration of a type, so a second pair would leave these the ones
        // nothing reads. Classification is off there, which is what every account these tests configure runs with: the
        // gate then admits everything and the run behaves exactly as it did before it existed.
        services.AddScoped<DerivedWorkGate>();

        var (catalog, resolutionStore) = CreateResolvedFolders(options, unadvertisedAliases);
        services.AddSingleton(remoteFolderCatalog ?? catalog);
        services.AddSingleton(resolutionStore);
        services.AddSingleton(Substitute.For<IMailFolderMappingChangeAuditor>());
        // Registered so a resolver can be composed at all, and never configured to answer: no mapping these tests build
        // asks for its folder to be created, so a call reaching it would mean resolution had started creating folders
        // for a mapping that never asked.
        services.AddSingleton(Substitute.For<IRemoteFolderCreator>());
        services.AddScoped<MailFolderResolver>();
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<MailboxSynchronizer>();
        services.AddScoped<MailboxReconciler>();

        // Every account run begins by converging what the account has asked a mail server for and not seen finished,
        // so a supervisor resolves these from its scope exactly as it resolves the synchronizer. The record store
        // answers that there is nothing outstanding, which is the state a test that is about folders wants.
        services.AddSingleton(mutationRecordStore ?? CreateRecordStoreWithNothingOutstanding());
        services.AddSingleton(new MailboxMutationOptions());
        services.AddSingleton(new MailboxConvergenceOptions());
        services.AddSingleton(Substitute.For<IMailboxWriteSessionFactory>());
        services.AddSingleton<MailboxConvergenceTelemetry>();
        services.AddSingleton<MailboxContentVolumeTelemetry>();
        // Resolved by the coordinator and handed to every supervisor it starts, so it is composed here rather than
        // constructed per harness: a supervisor built with one instance and a coordinator with another would publish
        // two sets of the levels that describe the process.
        services.AddSingleton<MailSynchronizationTelemetry>();
        services.AddSingleton<IMailSynchronizationPhaseTelemetry>(provider =>
            provider.GetRequiredService<MailSynchronizationTelemetry>());
        services.AddScoped<IMailboxMutationPerformer, MailboxMutationPerformer>();
        services.AddScoped<MailboxMutationConverger>();

        // The trail and its retention pass are composed here for the same reason convergence is: a supervisor resolves
        // both from its scope. Neither is what these tests are about — the trail is off for every account they
        // configure, so both answer that there is nothing to keep and nothing to erase.
        services.AddSingleton(CreateTrailThatKeepsNothing());
        services.AddSingleton(CreateAuditStoreWithNothingToErase());
        services.AddScoped<IMailboxMutationAuditSettingsReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>().Readers.MutationAuditSettings);
        services.AddScoped<MailboxMutationAuditTrailRetention>();

        // No run erases what a folder the account has stopped mirroring stored, and both of these are registered so
        // that assertion can fail rather than pass by accident: a run that reached for the eraser again would compose,
        // resolve, and record into the store below instead of throwing into the supervisor's own catch and being
        // logged as an unexpected failure nobody asserted on.
        services.AddSingleton(folderMirrorStore ?? new RecordingMailFolderMirrorStore());
        services.AddScoped<UnmirroredMailFolderEraser>();

        // The step in front of the cut, which is what a run reaches after every folder it scheduled. The default rule
        // set declares nothing and the default store holds nothing, so a test that is not about rules pays for one
        // query that finds no mail and no outstanding run.
        services.AddSingleton(ruleEvaluationStore ?? CreateRuleStoreWithNothingToEvaluate());
        services.AddSingleton(CreateRunStoreWithNothingOutstanding());
        services.AddSingleton(CreateSourceOfAnEmptyRuleSet());
        services.AddSingleton(new MailRuleSetEvaluator(timeProvider));
        services.AddScoped(_ => new MailRuleEvaluationOptions());
        services.AddScoped<IAuthoredDeleteEmailDispositionReader>(
            provider => provider.GetRequiredService<MailSynchronizationOptions>().Readers.AuthoredDeleteEmailDispositions);
        services.AddScoped<IMailRuleActionPermissionReader>(
            provider => provider.GetRequiredService<MailSynchronizationOptions>().Readers.RuleActionPermissions);

        // A rule may name its destination by the role a folder plays, and a condition may ask what role the folder an
        // email is in plays, so both the recorder and the pass read the account's mappings through this port. The host
        // answers it from the same snapshot every other per-account reader answers from.
        services.AddScoped<IMailFolderMappingReader>(
            provider => provider.GetRequiredService<MailSynchronizationOptions>().Readers.FolderMappings);
        services.AddScoped<MailFolderReferenceResolver>();
        services.AddScoped<MailRuleActionRecorder>();
        // A rule may file into a folder the account maps and does not mirror, which nothing binds until a change needs
        // it, so the pass resolves its destinations through the same service the host registers.
        services.AddScoped<MailboxDestinationResolver>();
        // One instance across every scope a run opens, so what several scopes appended is readable as one history.
        var ruleHistory = new MailRuleExecutionRecordingStore();
        services.AddSingleton(ruleHistory);
        services.AddSingleton<IMailRuleExecutionStore>(ruleHistory);
        services.AddScoped<MailRuleEvaluationPass>();

        // The classification walk rides the same run, one step before the rules, and a supervisor resolves it from the
        // same scope. These tests ask for no run over any mailbox, so the store below answers that the account has none
        // outstanding and the pass returns without reading a message — which is what an account nobody asked costs.
        services.AddSingleton(classificationRunStore ?? CreateClassificationRunStoreWithNothingOutstanding());
        services.AddSingleton(Substitute.For<IClassifiableEmailReader>());
        services.AddSingleton(Substitute.For<IEmailSpamClassificationStore>());
        services.AddSingleton(Substitute.For<IEmailSpamHeaderReader>());
        // A stub rather than a substitute, because the gate resolved above enumerates the folder list: a substituted
        // IReadOnlyList happens to enumerate as empty today, which is the right answer reached by accident.
        services.AddSingleton<IJunkMailFolderCatalog>(StubJunkMailFolderCatalog.None);
        services.AddSingleton(Substitute.For<ISpamActionOccurrenceReader>());
        services.AddScoped(_ => new SpamClassificationRunOptions());
        services.AddScoped(_ => CreateClassificationSettingsReader());
        services.AddScoped(_ => CreateSpamActionSettingsReader());
        services.AddScoped<DeterministicSpamClassifier>();
        services.AddScoped<EmailSpamClassifier>();
        services.AddScoped<SpamActionRecorder>();
        services.AddScoped<SpamClassificationPass>();
        // The arrival trigger the run reaches after each message it commits, and the queue it writes through. Every
        // account these tests configure runs with classification off, so the trigger reads one property per stored
        // message and the substituted queue is never asked for anything.
        services.AddSingleton(Substitute.For<IJobStore>());
        services.AddScoped<SpamClassificationArrivals>();

        // The other thing the run reaches after each message it commits. Every account these tests configure leaves
        // contact collection off, so the collector reads one property per stored message and neither the book nor the
        // tally below is ever asked anything; both are composed because a pass that could not resolve one would fail
        // the folder rather than the assertion.
        services.AddSingleton(Substitute.For<IContactStore>());
        services.AddSingleton(Substitute.For<IContactDirectory>());
        services.AddSingleton(Substitute.For<IAuthoredMailTally>());
        services.AddSingleton(Substitute.For<IContactCollectionTelemetry>());
        services.AddScoped<IContactCollectionSettingsReader>(provider =>
            provider.GetRequiredService<MailSynchronizationOptions>().Readers.ContactCollection);
        services.AddScoped(_ => AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));

        // Whose book the run writes into. The process identity acts for nobody, so the resolution answers with the
        // owner the deployment serves, exactly as it does on a deployment where every account comes from configuration.
        services.AddScoped(provider =>
            ContactBookOwnerships.For(provider.GetRequiredService<AccessAuthorization>()));
        services.AddScoped<ContactBook>();
        services.AddScoped<MailContactCollector>();

        // The cut is the run's last local step, after the rules for the ordering the arrival pipeline is built on, and a
        // supervisor resolves it from the same scope. The store answers that no message is awaiting passages, which is
        // the state a test about folders wants: the pass issues one query and returns.
        services.AddSingleton(chunkingStore ?? CreateChunkingStoreWithNothingToCut());
        services.AddScoped<MailChunkingPass>();

        // The history's retention pass rides the same run, and a supervisor resolves it from the same scope. These
        // tests configure no mail to evaluate, so it answers that there is nothing to erase.
        services.AddScoped<MailRuleHistoryRetention>();
        services.AddLogging();

        // Each run hands its own snapshot to the scopes it opens, and every per-account reader answers from that
        // snapshot rather than from the one the container was composed with. The host wires it exactly this way, which
        // is what lets a test replace the published snapshot and watch the next run pick the new account list up.
        services.AddSingleton(publishedSettings);
        services.AddScoped<ScopedMailSynchronizationSettings>();
        services.AddScoped(provider => provider.GetRequiredService<ScopedMailSynchronizationSettings>().Current);
        services.AddScoped<IMailTransportSecurityPolicyReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>().Readers.TransportSecurityPolicies);
        services.AddScoped<IMailSynchronizationWindowReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>().Readers.SynchronizationWindows);
        services.AddScoped<IRemotelyDeletedEmailDispositionReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>().Readers.RemotelyDeletedEmailDispositions);
        // Composed off the scoped snapshot rather than the container's own options, exactly as the composition root
        // composes it, so the account list a supervision pass reads is the one the latest reload published. The owner
        // is supplied rather than configured, because a configured account block names none.
        services.AddSingleton<IDeploymentMailOwnerSource, StubDeploymentMailOwnerSource>();
        services.AddScoped<IDeploymentMailAccountCatalog>(provider => new ConfiguredMailAccountCatalog(
            provider.GetRequiredService<MailSynchronizationOptions>(),
            provider.GetRequiredService<IDeploymentMailOwnerSource>()));

        return services.BuildServiceProvider();
    }

    /// <summary>Answers every rule query with nothing, which is what an account whose mail is all evaluated looks like.</summary>
    private static IMailRuleEvaluationStore CreateRuleStoreWithNothingToEvaluate()
    {
        var store = Substitute.For<IMailRuleEvaluationStore>();

        store.GetEmailsAwaitingFirstEvaluationAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>>([]));
        store.GetStoredEmailsAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingRuleEvaluation>>([]));

        return store;
    }

    /// <summary>Answers that no message is awaiting passages, which is what a run these tests configure produces.</summary>
    private static IStoredEmailChunkingStore CreateChunkingStoreWithNothingToCut()
    {
        var store = Substitute.For<IStoredEmailChunkingStore>();

        store.GetEmailsAwaitingChunkingAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingChunking>>([]));

        return store;
    }

    /// <summary>Answers with a rule set that declares nothing, which is what a deployment configuring no rule has.</summary>
    private static IMailRuleSetSource CreateSourceOfAnEmptyRuleSet()
    {
        var ruleSetSource = Substitute.For<IMailRuleSetSource>();

        ruleSetSource.Current.Returns(
            MailRuleSet.Create([], MailRuleSetRevision.Create([]), MailRuleConditionBounds.Default));

        return ruleSetSource;
    }

    /// <summary>Answers that the account has never been asked to have its whole mailbox classified.</summary>
    private static ISpamClassificationRunStore CreateClassificationRunStoreWithNothingOutstanding()
    {
        var runStore = Substitute.For<ISpamClassificationRunStore>();

        runStore.FindOutstandingAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SpamClassificationRun?>(null));

        return runStore;
    }

    /// <summary>Answers with classification switched off, which is what a deployment configuring none of it runs with.</summary>
    private static ISpamClassificationSettingsReader CreateClassificationSettingsReader()
    {
        var reader = Substitute.For<ISpamClassificationSettingsReader>();
        reader.Settings.Returns(SpamClassificationSettings.Disabled);

        return reader;
    }

    /// <summary>Answers with neither junk switch on, so a verdict here could ask a mailbox for nothing.</summary>
    private static ISpamActionSettingsReader CreateSpamActionSettingsReader()
    {
        var reader = Substitute.For<ISpamActionSettingsReader>();
        reader.Actions.Returns(SpamActionSettings.None);

        return reader;
    }

    private static IMailRuleEvaluationRunStore CreateRunStoreWithNothingOutstanding()
    {
        var runStore = Substitute.For<IMailRuleEvaluationRunStore>();

        runStore.FindOutstandingAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MailRuleEvaluationRun?>(null));

        return runStore;
    }

    /// <summary>Advances a fake clock until the awaited signal arrives, or until the test's own guard gives up.</summary>
    /// <param name="timeProvider">The clock every scheduled wait was registered against.</param>
    /// <param name="completion">The signal the code under test raises.</param>
    /// <param name="step">How far one advance moves the clock.</param>
    /// <param name="deadlockGuard">How long the test waits on real time before it reports a hang.</param>
    /// <returns>A task that completes once the signal has arrived.</returns>
    /// <remarks>
    /// A scheduled wait completes while the clock is advanced, but the continuation behind it runs on the thread pool
    /// afterwards, so the yield between advances is what lets the code under test reach its next wait. Advancing past
    /// a wait that has already completed costs nothing, which is why the loop can advance blindly rather than having
    /// to observe where the code under test currently is. What it may not do is stop advancing on a count: the next
    /// wait is registered only once the work before it finishes, so a loop that had already spent its advances would
    /// leave that wait pending forever. The guard is therefore what ends it, on real time, in both outcomes.
    /// </remarks>
    internal static async Task AdvanceUntilAsync(
        FakeTimeProvider timeProvider,
        Task completion,
        TimeSpan step,
        TimeSpan deadlockGuard)
    {
        var guardedCompletion = completion.WaitAsync(deadlockGuard, TestContext.Current.CancellationToken);

        while (!guardedCompletion.IsCompleted)
        {
            timeProvider.Advance(step);

            await Task.Yield();
        }

        await guardedCompletion;
    }

    /// <summary>Models a server that advertises exactly the configured folders, each already bound to its alias.</summary>
    /// <remarks>
    /// Binding them up front keeps these tests about what a supervisor does with a folder's outcome. Resolution and
    /// rebinding are covered where they live, in the application layer.
    /// </remarks>
    private static (IRemoteFolderCatalog Catalog, IMailFolderResolutionStore ResolutionStore) CreateResolvedFolders(
        MailSynchronizationOptions options,
        IReadOnlyCollection<string> unadvertisedAliases)
    {
        // A folder the supervisor itself cannot turn into a mapping is not one the modelled server can advertise either.
        var pathsByAlias = options.Accounts
            .SelectMany(account => account.EffectiveFolders)
            .Select(TryCreateMapping)
            .OfType<MailFolderMapping>()
            .Where(mapping => mapping.RemotePath is not null && !unadvertisedAliases.Contains(mapping.Alias.Value))
            .DistinctBy(mapping => mapping.Alias.Value, StringComparer.Ordinal)
            .ToDictionary(mapping => mapping.Alias.Value, mapping => mapping.RemotePath!.Value, StringComparer.Ordinal);

        var catalog = Substitute.For<IRemoteFolderCatalog>();
        catalog
            .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>(
                [.. pathsByAlias.Values.Select(path => new RemoteFolder(path, []))]));

        var resolutionStore = Substitute.For<IMailFolderResolutionStore>();
        resolutionStore
            .GetCurrentResolutionAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<MailFolderAlias>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var alias = call.Arg<MailFolderAlias>();
                MailFolderResolution? binding = pathsByAlias.TryGetValue(alias.Value, out var path)
                    ? MailFolderResolution.FirstBindingOf(alias, path)
                    : null;

                return Task.FromResult(binding);
            });

        return (catalog, resolutionStore);
    }

    private static MailFolderMapping? TryCreateMapping(MailFolderMappingOptions configuredFolder)
    {
        try
        {
            return configuredFolder.CreateMapping();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Builds a store whose folders hold nothing awaiting reconciliation, so a run's backward pass reaches no server.</summary>
    /// <remarks>
    /// These tests are about how a supervisor schedules and isolates folder work units, not about what reconciliation
    /// finds, and an unconfigured substitute would answer the window query with a null task the run then faults on.
    /// </remarks>
    private static IStoredEmailReconciliationStore CreateReconciliationStoreWithNothingToDo()
    {
        var reconciliationStore = Substitute.For<IStoredEmailReconciliationStore>();
        reconciliationStore
            .GetReconciliationWindowAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingReconciliation>>([]));

        return reconciliationStore;
    }

    /// <summary>Builds a store in which this deployment has filed no copy, so every message a run meets is somebody else's.</summary>
    /// <remarks>
    /// A run asks this of every batch it discovers, to tell a copy it filed itself from mail that arrived. These tests
    /// send nothing, so the answer is always empty — and an unconfigured substitute would answer with a null task the
    /// folder's own work unit then faults on, which surfaces as a supervision that never signals.
    /// </remarks>
    private static IOutgoingMailFilingStore CreateFilingStoreWithNothingFiled()
    {
        var filingStore = Substitute.For<IOutgoingMailFilingStore>();
        filingStore
            .ReadFilingsAtAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<RemoteFolderPath>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<IReadOnlyCollection<ImapUid>>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutgoingMailFilingRecord>>([]));

        return filingStore;
    }

    /// <summary>Builds a store holding no mutations, so nothing a run discovers is one MailFathom itself made.</summary>
    /// <remarks>
    /// These tests are about how a supervisor schedules and isolates folder work units. An unconfigured substitute would
    /// answer every read with a null task the run then faults on, which surfaces as a supervision that never signals.
    /// </remarks>
    /// <summary>Answers a convergence pass that the account has asked for nothing that has not finished.</summary>
    private static IMailboxMutationRecordStore CreateRecordStoreWithNothingOutstanding()
    {
        var recordStore = Substitute.For<IMailboxMutationRecordStore>();
        recordStore
            .ReadOutstandingAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutstandingMailboxMutation>>([]));
        recordStore
            .ReadLifecycleCountsAsync(Arg.Any<MailAccountIdentity>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxMutationLifecycleCount>>([]));

        return recordStore;
    }

    /// <summary>Answers every append, because the accounts these tests configure keep no trail and owe none.</summary>
    private static IMailboxMutationAuditTrail CreateTrailThatKeepsNothing()
    {
        var auditTrail = Substitute.For<IMailboxMutationAuditTrail>();
        auditTrail
            .RecordAsync(
                Arg.Any<MailboxMutationRecord>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        return auditTrail;
    }

    /// <summary>Answers a retention pass that the account's trail holds nothing that has outlived its window.</summary>
    private static IMailboxMutationAuditEntryStore CreateAuditStoreWithNothingToErase()
    {
        var auditStore = Substitute.For<IMailboxMutationAuditEntryStore>();
        auditStore
            .EraseCompletedBeforeAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(0));

        return auditStore;
    }

    private static IMailboxMutationReconciliationStore CreateMutationStoreWithNothingRecorded()
    {
        var mutationStore = Substitute.For<IMailboxMutationReconciliationStore>();
        mutationStore
            .ReadPlacementsAtAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<RemoteFolderPath>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<IReadOnlyCollection<ImapUid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxMutationRecord>>([]));
        mutationStore
            .ReadMutationsRemovingAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<IReadOnlyCollection<ImapUid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxMutationRecord>>([]));
        mutationStore
            .ReadFlagChangesOnAsync(
                Arg.Any<MailAccountIdentity>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<IReadOnlyCollection<ImapUid>>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxMutationRecord>>([]));

        return mutationStore;
    }

    /// <summary>Builds a reader whose messages all parse, because these tests are about how a supervisor isolates folders.</summary>
    private static IEmailMimeReader CreateMimeReaderThatExtractsEverything()
    {
        var mimeReader = Substitute.For<IEmailMimeReader>();
        mimeReader
            .ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmailMimeExtractionResult.Extracted(new ExtractedEmailMetadata(
                call.Arg<RemoteEmailContent>()!.OccurrenceId,
                Subject: null,
                SentAt: null,
                ReceivedAt: null,
                Participants: [],
                EmailThreadReferences.None,
                EmailAttachmentSummary.None,
                ExtractedEmailText.NoTextualBody,
                SenderAuthentication.NotEstablished()))));

        return mimeReader;
    }
}
