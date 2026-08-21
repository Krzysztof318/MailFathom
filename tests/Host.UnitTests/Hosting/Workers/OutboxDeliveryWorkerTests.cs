// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Transport;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers what the loop decides, which is when to run a pass and what one pass's failure may cost the next.</summary>
/// <remarks>
/// What a pass does with a claimed send belongs to the pass and is covered where that lives. The claims here are the
/// worker's own: that a signal is what wakes it, that a batch it filled is asked for again, and that neither a pass
/// that threw nor an account that cannot be served stops the account behind it.
/// </remarks>
public sealed class OutboxDeliveryWorkerTests
{
    /// <summary>Guards against a hung loop. No assertion depends on how long the run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailAccountId Personal = MailAccountId.Create("personal");

    /// <summary>A signalled account is the only thing that starts a pass, so an idle deployment claims nothing.</summary>
    [Fact]
    public async Task ExecuteAsync_NothingSignalled_ClaimsNothing()
    {
        // Arrange
        var context = new WorkerContext();

        // Act
        await context.RunUntilAsync(() => Task.CompletedTask);

        // Assert
        await context.OutgoingEmails.DidNotReceiveWithAnyArgs()
            .ClaimAsync(Arg.Any<OutgoingEmailClaimRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A signalled account has its outbox claimed, which is the whole reason the loop exists.</summary>
    [Fact]
    public async Task ExecuteAsync_AccountSignalled_TakesAPassOverItsOutbox()
    {
        // Arrange
        var context = new WorkerContext();

        // Act
        await context.RunUntilAsync(async () =>
        {
            context.Signal.Signal(Work);
            await context.WaitForClaimsAsync(1);
        });

        // Assert
        var claim = Assert.Single(context.Claims);
        Assert.Equal(Work, claim.AccountId);
    }

    /// <summary>
    /// A pass that took everything it was allowed left more behind it, so the account is asked for again rather than
    /// left until its synchronization run notices.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_PassFilledItsBatch_SignalsTheAccountAgain()
    {
        // Arrange
        var context = new WorkerContext();
        context.QueueClaimableSends(WorkerContext.MaxDeliveriesPerPass);

        // Act
        await context.RunUntilAsync(async () =>
        {
            context.Signal.Signal(Work);
            await context.WaitForClaimsAsync(2);
        });

        // Assert
        Assert.True(context.Claims.Count >= 2);
        Assert.All(context.Claims, claim => Assert.Equal(Work, claim.AccountId));
    }

    /// <summary>One account's pass ending unexpectedly leaves the accounts behind it served, which is the isolation.</summary>
    [Fact]
    public async Task ExecuteAsync_PassForOneAccountFails_KeepsServingTheNextAccount()
    {
        // Arrange
        var context = new WorkerContext();
        context.OutgoingEmails
            .ClaimAsync(
                Arg.Is<OutgoingEmailClaimRequest>(request => request != null && request.AccountId == Work),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The claim could not be issued."));

        // Act
        await context.RunUntilAsync(async () =>
        {
            context.Signal.Signal(Work);
            context.Signal.Signal(Personal);

            // The account whose claim throws records none, so the one claim to wait for is the account behind it. One
            // loop takes the accounts in turn, so a claim for the second proves the first has already ended.
            await context.WaitForClaimsAsync(1);
        });

        // Assert
        Assert.Equal(Personal, Assert.Single(context.Claims).AccountId);
        Assert.Contains(context.Logger.Messages, message => message.Contains(
            "The outbox pass for account work failed",
            StringComparison.Ordinal));
    }

    /// <summary>An account that submits nowhere is answered by an empty pass rather than by a claim it could not attempt.</summary>
    [Fact]
    public async Task ExecuteAsync_AccountWithNoSubmissionEndpoint_ClaimsNothingForIt()
    {
        // Arrange
        var context = new WorkerContext(submits: false);

        // Act
        await context.RunUntilAsync(async () =>
        {
            context.Signal.Signal(Work);

            // The policy read is what the pass does first and is the point at which the account is decided against, so
            // waiting for it proves the loop reached this account rather than proving only that time passed.
            await context.WaitForPolicyReadAsync();
        });

        // Assert
        Assert.Empty(context.Claims);
    }

    /// <summary>Assembles the worker over a scoped pass whose store and policy the test writes.</summary>
    private sealed class WorkerContext
    {
        /// <summary>How much one pass of this worker claims, which is what a test fills to make a batch report full.</summary>
        internal const int MaxDeliveriesPerPass = 2;

        private readonly ServiceProvider services;
        private readonly List<ClaimedOutgoingEmail> claimable = [];
        private readonly TaskCompletionSource claimsReached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource policyRead = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int awaitedClaimCount = int.MaxValue;

        internal WorkerContext(bool submits = true)
        {
            this.OutgoingEmails.ClaimAsync(Arg.Any<OutgoingEmailClaimRequest>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    lock (this.Claims)
                    {
                        this.Claims.Add(callInfo.ArgAt<OutgoingEmailClaimRequest>(0));

                        if (this.Claims.Count >= this.awaitedClaimCount)
                        {
                            this.claimsReached.TrySetResult();
                        }
                    }

                    return Task.FromResult<IReadOnlyList<ClaimedOutgoingEmail>>([.. this.claimable]);
                });

            var policyReader = Substitute.For<IMailTransportSecurityPolicyReader>();
            policyReader.GetDeliveryPolicy(Arg.Any<MailAccountId>()).Returns(_ =>
            {
                this.policyRead.TrySetResult();

                return submits ? TransportSecurityPolicy() : null;
            });

            var collection = new ServiceCollection();
            collection.AddSingleton<TimeProvider>(new FakeTimeProvider());
            collection.AddSingleton(this.OutgoingEmails);
            collection.AddSingleton(policyReader);
            collection.AddSingleton(Substitute.For<IEmailContentStore>());
            collection.AddSingleton(Substitute.For<IMailDeliverySessionFactory>());
            collection.AddSingleton(Substitute.For<IOutgoingSenderIdentityReader>());
            collection.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
            collection.AddSingleton(new PersistenceConcurrencyOptions());
            collection.AddSingleton(MailOutboxSettings.Create(
                MaxDeliveriesPerPass,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(1),
                maxAttempts: 3,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromHours(8)));
            collection.AddScoped<OptimisticConcurrencyRetryPolicy>();
            collection.AddScoped<MailOutboxDelivery>();

            // A pass files the copies of whatever it settles, so it cannot be composed without the filing side of it.
            // Every one of these substitutes answers that the account maps no folder to file into and asks for no sent
            // copy, which is the arrangement these tests are written against: what the loop decides is when to take a
            // pass, and where a copy ends up belongs to the pass's own tests.
            collection.AddSingleton(Substitute.For<IMailFolderMappingReader>());
            collection.AddSingleton(Substitute.For<IMailFolderResolutionStore>());
            collection.AddSingleton(Substitute.For<IMailFolderMappingChangeAuditor>());
            collection.AddSingleton(Substitute.For<IRemoteFolderCatalog>());
            collection.AddSingleton(Substitute.For<IRemoteFolderCreator>());
            collection.AddSingleton(Substitute.For<IMailboxWriteSessionFactory>());
            collection.AddSingleton(Substitute.For<IOutgoingMailFilingStore>());
            collection.AddSingleton(Substitute.For<IOutgoingMailFilingPolicyReader>());
            collection.AddScoped<MailFolderReferenceResolver>();
            collection.AddScoped<MailFolderResolver>();
            collection.AddScoped<MailboxDestinationResolver>();
            collection.AddScoped<MailboxCopyAppender>();
            collection.AddScoped<OutgoingMailFiler>();
            collection.AddScoped<OutgoingMailFilingPass>();

            // A pass settles the account's drafts before it claims anything, so it cannot be composed without the
            // drafts side either. The store answers that nothing is outstanding, which is the arrangement these tests
            // are written against for the same reason the filing substitutes above are.
            var drafts = Substitute.For<IMailDraftStore>();
            drafts.ReadOutstandingAsync(Arg.Any<MailAccountId>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns([]);
            collection.AddSingleton(drafts);
            collection.AddScoped<MailDraftFiler>();
            collection.AddScoped<MailDraftPass>();

            collection.AddScoped<MailOutboxPass>();

            this.services = collection.BuildServiceProvider();
        }

        internal IOutgoingEmailStore OutgoingEmails { get; } = Substitute.For<IOutgoingEmailStore>();

        internal MailOutboxSignal Signal { get; } = new(capacity: 8);

        internal List<OutgoingEmailClaimRequest> Claims { get; } = [];

        internal RecordingLogger<OutboxDeliveryWorker> Logger { get; } = new();

        /// <summary>Makes a pass find a full batch, so the report says there is more waiting behind it.</summary>
        /// <remarks>
        /// Every claimed send has no sending address, so the attempt ends before it opens anything. What the pass does
        /// with a send is not this class's subject; how many it claimed is.
        /// </remarks>
        internal void QueueClaimableSends(int count) => this.claimable.AddRange(
            Enumerable.Range(0, count).Select(_ => new ClaimedOutgoingEmail(
                RecordFor(Work),
                new OutgoingEmailLease(Guid.CreateVersion7(), DateTimeOffset.UnixEpoch.AddYears(1)))));

        /// <summary>Waits for the loop to have issued a number of claims, or fails rather than hanging.</summary>
        internal Task WaitForClaimsAsync(int claimCount)
        {
            lock (this.Claims)
            {
                this.awaitedClaimCount = claimCount;

                if (this.Claims.Count >= claimCount)
                {
                    this.claimsReached.TrySetResult();
                }
            }

            return this.claimsReached.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        }

        /// <summary>Waits for a pass to have asked whether the account submits at all, or fails rather than hanging.</summary>
        internal Task WaitForPolicyReadAsync() =>
            this.policyRead.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        /// <summary>Runs the worker, drives the scenario, and stops it the way a host shutdown does.</summary>
        internal async Task RunUntilAsync(Func<Task> driveAsync)
        {
            using var worker = new OutboxDeliveryWorker(
                this.services.GetRequiredService<IServiceScopeFactory>(),
                this.Signal,
                new MailDeliveryTelemetry(TimeProvider.System),
                this.Logger);

            await worker.StartAsync(TestContext.Current.CancellationToken);

            try
            {
                await driveAsync();
            }
            finally
            {
                // Bounded rather than awaited outright: a worker that stopped observing its stopping token would
                // otherwise hold the whole suite open instead of failing here, where the reason is readable.
                await worker.StopAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
                await this.services.DisposeAsync();
            }
        }

        private static OutgoingEmailRecord RecordFor(MailAccountId accountId)
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

            return new OutgoingEmailRecord
            {
                Id = OutgoingEmailId.Create(Guid.CreateVersion7()),
                AccountId = accountId,
                Requester = OutgoingEmailRequester.Command($"mfctl-{Guid.CreateVersion7()}"),
                Principal = OutgoingEmailPrincipal.Of("caller"),
                Recipients = [OutgoingRecipientOutcome.Unanswered(
                    OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To))],
                Stage = OutgoingEmailStage.Recorded,
                MimeByteLength = 64,
                AttemptCount = 1,
                RecordedAt = DateTimeOffset.UnixEpoch,
                StageChangedAt = DateTimeOffset.UnixEpoch,
                AvailableAt = DateTimeOffset.UnixEpoch,
                DueAt = null,
                LastFailure = null,
                LastReplyCode = null,
                Filings = [],
                LastFilingFailure = null,
            };
        }

        private static MailTransportSecurityPolicy TransportSecurityPolicy() => MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.StartTlsRequired,
            MailAuthenticationPolicy.Create(
                [MailAuthenticationMechanism.ScramSha256],
                allowInsecureConnection: false,
                allowClearTextAuthenticationOverUnencryptedConnection: false),
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null);
    }
}
