// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Emails;
using MailMcp.Application.Folders;
using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using MailMcp.Host.Configuration;
using MailMcp.Host.Hosting;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailMcp.Host.UnitTests;

public sealed class MailSynchronizationWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long the run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task ExecuteAsync_SynchronizationDisabled_NeverOpensAMailbox()
    {
        // Arrange
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var options = CreateOptions(enabled: false, "INBOX");
        using var worker = CreateWorker(options, sessionFactory, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;

        // Assert
        await sessionFactory.DidNotReceiveWithAnyArgs().OpenReadOnlyAsync(default!, default!, default!, CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_FirstFolderFails_StillSynchronizesTheRemainingFolder()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 2,
            _ => new InvalidOperationException("connect failed"));
        var options = CreateOptions(enabled: true, "INBOX", "Archive");
        using var worker = CreateWorker(options, sessionFactory, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await lastFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["INBOX", "ARCHIVE"], attemptedFolders);
    }

    [Fact]
    public async Task ExecuteAsync_FolderDefersAfterAConcurrencyConflict_LogsTheDeferralAndContinues()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 2,
            folderName => folderName == "INBOX"
                ? new PersistenceConcurrencyConflictException("A competing writer won the race.")
                : new InvalidOperationException("connect failed"));
        var options = CreateOptions(enabled: true, "INBOX", "Archive");
        using var worker = CreateWorker(options, sessionFactory, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await lastFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(logger.Messages, message => message.Contains("Deferred IMAP folder synchronization for primary/INBOX", StringComparison.Ordinal));
    }

    /// <summary>A struggling mail server and a host that is shutting down must not read as the same event.</summary>
    [Fact]
    public async Task ExecuteAsync_MailServerRefusesTheFolder_LogsItAsAServerDeferralAndContinues()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 2,
            folderName => folderName == "INBOX"
                ? new MailboxUnavailableException(
                    MailAccountId.Create("primary"),
                    MailFolderAlias.Create("INBOX"),
                    new TimeoutException("The server stopped answering."))
                : new InvalidOperationException("connect failed"));
        var options = CreateOptions(enabled: true, "INBOX", "Archive");
        using var worker = CreateWorker(options, sessionFactory, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await lastFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["INBOX", "ARCHIVE"], attemptedFolders);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("primary/INBOX because the mail server did not serve it", StringComparison.Ordinal));
    }

    /// <summary>An alias the server advertises no folder for is a configuration mistake, not a failed run.</summary>
    [Fact]
    public async Task ExecuteAsync_AliasMatchesNoAdvertisedFolder_LogsItAndSynchronizesTheRemainingFolder()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        var options = CreateOptions(enabled: true, "Archive", "INBOX");
        using var worker = CreateWorker(options, sessionFactory, out var logger, unadvertisedAliases: ["ARCHIVE"]);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await lastFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["INBOX"], attemptedFolders);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("Folder alias primary/ARCHIVE matched no folder", StringComparison.Ordinal));
    }

    /// <summary>An ambiguous role and an alias that matches nothing need different remedies, so they are logged as different things.</summary>
    [Fact]
    public async Task ExecuteAsync_AliasMatchesSeveralAdvertisedFolders_LogsTheAmbiguityAndTheRemedy()
    {
        // Arrange
        var listingRequested = new TaskCompletionSource();
        var catalog = Substitute.For<IRemoteFolderCatalog>();
        catalog
            .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                listingRequested.TrySetResult();

                return Task.FromResult<IReadOnlyList<RemoteFolder>>(
                [
                    new RemoteFolder(RemoteFolderPath.Create("Archief", '/'), [MailFolderSpecialUse.Archive]),
                    new RemoteFolder(RemoteFolderPath.Create("Archive", '/'), [MailFolderSpecialUse.Archive]),
                ]);
            });
        var options = CreateOptions(enabled: true);
        options.Accounts[0].Folders = [new MailFolderMappingOptions { Alias = "archive", SpecialUse = "Archive" }];
        using var worker = CreateWorker(options, Substitute.For<IMailboxSessionFactory>(), out var logger, catalog);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await listingRequested.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            logger.Messages,
            message => message.Contains("primary/ARCHIVE matched several folders", StringComparison.Ordinal)
                && message.Contains("configure its RemotePath", StringComparison.Ordinal));
    }

    /// <summary>Options validation should have caught it, but a folder that reaches the worker unusable must not end the loop.</summary>
    [Fact]
    public async Task ExecuteAsync_ConfiguredFolderCannotBecomeAMapping_FailsThatFolderAndContinues()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        var options = CreateOptions(enabled: true, "INBOX");
        options.Accounts[0].Folders.Insert(0, new MailFolderMappingOptions { Alias = "  ", RemotePath = "Archive" });
        using var worker = CreateWorker(options, sessionFactory, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await lastFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(["INBOX"], attemptedFolders);
        Assert.Contains(logger.Messages, message => message.Contains("IMAP synchronization failed for primary/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_FolderFails_LogsNeitherTheUserNameNorTheSecretReference()
    {
        // Arrange
        var attemptedFolders = new List<string>();
        var lastFolderAttempted = new TaskCompletionSource();
        var sessionFactory = CreateFailingSessionFactory(
            attemptedFolders,
            lastFolderAttempted,
            expectedFolderCount: 1,
            _ => new InvalidOperationException("connect failed"));
        var options = CreateOptions(enabled: true, "INBOX");
        using var worker = CreateWorker(options, sessionFactory, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await lastFolderAttempted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var logged = string.Join(' ', logger.Messages);
        Assert.DoesNotContain("mailmcp@example.test", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("imap-primary-password", logged, StringComparison.Ordinal);
    }

    private static IMailboxSessionFactory CreateFailingSessionFactory(
        List<string> attemptedFolders,
        TaskCompletionSource runFinished,
        int expectedFolderCount,
        Func<string, Exception> failureFor)
    {
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        sessionFactory
            .OpenReadOnlyAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolution>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Throws(call =>
            {
                var folderAlias = call.Arg<MailFolderResolution>()!.Alias.Value;
                attemptedFolders.Add(folderAlias);

                // The worker loops until it is stopped, and its timer never ticks under a fake clock, so the test
                // observes exactly one run by counting the folders it reached rather than by waiting on time.
                if (attemptedFolders.Count == expectedFolderCount)
                {
                    runFinished.TrySetResult();
                }

                return failureFor(folderAlias);
            });

        return sessionFactory;
    }

    /// <summary>Configures folders whose alias and remote path are the same text, so a test names one folder once.</summary>
    private static MailSynchronizationOptions CreateOptions(bool enabled, params string[] folders) => new()
    {
        Enabled = enabled,
        Interval = TimeSpan.FromMinutes(5),
        Accounts =
        [
            new MailSynchronizationAccountOptions
            {
                AccountId = "primary",
                Host = "imap.example.test",
                UserName = "mailmcp@example.test",
                Folders = [.. folders.Select(folder => new MailFolderMappingOptions { Alias = folder, RemotePath = folder })],
                Secrets = new MailAccountSecretOptions
                {
                    Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
                },
            },
        ],
    };

    /// <summary>Models a server that advertises exactly the configured folders, each already bound to its alias.</summary>
    /// <remarks>
    /// Binding them up front keeps these tests about what the worker does with a folder's outcome. Resolution and
    /// rebinding are covered where they live, in the application layer.
    /// </remarks>
    private static (IRemoteFolderCatalog Catalog, IMailFolderResolutionStore ResolutionStore) CreateResolvedFolders(
        MailSynchronizationOptions options,
        IReadOnlyCollection<string> unadvertisedAliases)
    {
        // A folder the worker itself cannot turn into a mapping is not one the modelled server can advertise either.
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

    /// <summary>Builds a reader whose messages all parse, because these tests are about how the worker isolates folders.</summary>
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

    private static MailSynchronizationWorker CreateWorker(
        MailSynchronizationOptions options,
        IMailboxSessionFactory sessionFactory,
        out RecordingLogger<MailSynchronizationWorker> logger,
        IRemoteFolderCatalog? remoteFolderCatalog = null,
        params string[] unadvertisedAliases)
    {
        logger = new RecordingLogger<MailSynchronizationWorker>();
        var timeProvider = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(sessionFactory);
        services.AddSingleton<IMailTransportSecurityPolicyReader>(options);
        services.AddSingleton<IMailSynchronizationWindowReader>(options);
        services.AddSingleton(Substitute.For<ISynchronizationCheckpointStore>());
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(Substitute.For<IEmailMetadataRepository>());
        services.AddSingleton(Substitute.For<IEmailContentStore>());
        services.AddSingleton(CreateMimeReaderThatExtractsEverything());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(new MailboxSynchronizationOptions());

        var (catalog, resolutionStore) = CreateResolvedFolders(options, unadvertisedAliases);
        services.AddSingleton(remoteFolderCatalog ?? catalog);
        services.AddSingleton(resolutionStore);
        services.AddSingleton(Substitute.For<IMailFolderMappingChangeAuditor>());
        services.AddScoped<MailFolderResolver>();
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<MailboxSynchronizer>();
        // The worker hands its run snapshot to each work-unit scope, so the scope has to be able to receive one.
        services.AddSingleton<ISettingsSnapshot<MailSynchronizationOptions>>(new StubSettingsSnapshot<MailSynchronizationOptions>(options));
        services.AddScoped<ScopedMailSynchronizationSettings>();

        var serviceProvider = services.BuildServiceProvider();

        return new MailSynchronizationWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            new StubSettingsSnapshot<MailSynchronizationOptions>(options),
            logger,
            timeProvider);
    }
}
