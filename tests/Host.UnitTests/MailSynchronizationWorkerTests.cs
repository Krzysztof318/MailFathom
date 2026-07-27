// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using MailMcp.Host.Configuration;
using MailMcp.Host.Hosting;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        Assert.Equal(["INBOX", "Archive"], attemptedFolders);
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
                Arg.Any<MailFolderName>(),
                Arg.Any<MailTransportSecurityPolicy>(),
                Arg.Any<CancellationToken>())
            .Throws(call =>
            {
                var folderName = call.Arg<MailFolderName>().Value;
                attemptedFolders.Add(folderName);

                // The worker loops until it is stopped, and its timer never ticks under a fake clock, so the test
                // observes exactly one run by counting the folders it reached rather than by waiting on time.
                if (attemptedFolders.Count == expectedFolderCount)
                {
                    runFinished.TrySetResult();
                }

                return failureFor(folderName);
            });

        return sessionFactory;
    }

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
                Folders = [.. folders],
                Secrets = new MailAccountSecretOptions
                {
                    Password = new ConfiguredSecret { SecretReference = "systemd-credential:imap-primary-password" },
                },
            },
        ],
    };

    private static MailSynchronizationWorker CreateWorker(
        MailSynchronizationOptions options,
        IMailboxSessionFactory sessionFactory,
        out RecordingLogger<MailSynchronizationWorker> logger)
    {
        logger = new RecordingLogger<MailSynchronizationWorker>();
        var timeProvider = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(sessionFactory);
        services.AddSingleton<IMailTransportSecurityPolicyReader>(options);
        services.AddSingleton(Substitute.For<ISynchronizationCheckpointStore>());
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(Substitute.For<IEmailMetadataRepository>());
        services.AddSingleton(Substitute.For<IEmailContentStore>());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(new MailboxSynchronizationOptions());
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<MailboxSynchronizer>();

        var serviceProvider = services.BuildServiceProvider();

        return new MailSynchronizationWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            logger,
            timeProvider);
    }
}
