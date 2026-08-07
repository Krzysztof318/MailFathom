// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Secrets.Discovery;
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
        services.AddSingleton(CreateMimeReaderThatExtractsEverything());
        services.AddSingleton(CreateReconciliationStoreWithNothingToDo());
        services.AddSingleton(CreateMutationStoreWithNothingRecorded());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(new MailboxSynchronizationOptions());
        services.AddSingleton(timeProvider);
        // Every committed message is offered for embedding, so a synchronizer cannot be composed without somewhere to
        // offer it. Nothing here reads the backlog back; these tests are about the run, not about what embeds afterwards.
        services.AddSingleton<IEmailEmbeddingBacklog>(new ScriptedEmailEmbeddingBacklog());

        var (catalog, resolutionStore) = CreateResolvedFolders(options, unadvertisedAliases);
        services.AddSingleton(remoteFolderCatalog ?? catalog);
        services.AddSingleton(resolutionStore);
        services.AddSingleton(Substitute.For<IMailFolderMappingChangeAuditor>());
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
        services.AddScoped<IMailboxMutationPerformer, MailboxMutationPerformer>();
        services.AddScoped<MailboxMutationConverger>();
        services.AddLogging();

        // Each run hands its own snapshot to the scopes it opens, and every per-account reader answers from that
        // snapshot rather than from the one the container was composed with. The host wires it exactly this way, which
        // is what lets a test replace the published snapshot and watch the next run pick the new account list up.
        services.AddSingleton(publishedSettings);
        services.AddScoped<ScopedMailSynchronizationSettings>();
        services.AddScoped(provider => provider.GetRequiredService<ScopedMailSynchronizationSettings>().Current);
        services.AddScoped<IMailTransportSecurityPolicyReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
        services.AddScoped<IMailSynchronizationWindowReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());
        services.AddScoped<IRemotelyDeletedEmailDispositionReader>(provider => provider.GetRequiredService<MailSynchronizationOptions>());

        return services.BuildServiceProvider();
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
            .GetCurrentResolutionAsync(Arg.Any<MailAccountId>(), Arg.Any<MailFolderAlias>(), Arg.Any<CancellationToken>())
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
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingReconciliation>>([]));

        return reconciliationStore;
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
            .ReadOutstandingAsync(Arg.Any<MailAccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OutstandingMailboxMutation>>([]));
        recordStore
            .ReadLifecycleCountsAsync(Arg.Any<MailAccountId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxMutationLifecycleCount>>([]));

        return recordStore;
    }

    private static IMailboxMutationReconciliationStore CreateMutationStoreWithNothingRecorded()
    {
        var mutationStore = Substitute.For<IMailboxMutationReconciliationStore>();
        mutationStore
            .ReadPlacementsAtAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<RemoteFolderPath>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<IReadOnlyCollection<ImapUid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxMutationRecord>>([]));
        mutationStore
            .ReadMutationsRemovingAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<IReadOnlyCollection<ImapUid>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<MailboxMutationRecord>>([]));
        mutationStore
            .ReadSeenStateChangesOnAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<IReadOnlyCollection<ImapUid>>(),
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
                ExtractedEmailText.NoTextualBody))));

        return mimeReader;
    }
}
