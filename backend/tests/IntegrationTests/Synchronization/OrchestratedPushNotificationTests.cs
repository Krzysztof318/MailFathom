// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Application.Resilience;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Resilience;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using MailKit.Net.Imap;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Proves that push mode reaches a real mail server, which no substituted session factory can establish.</summary>
/// <remarks>
/// <para>
/// The unit suite drives the whole push state machine — degradation, renewal, recycling, the subscription bounds —
/// against a substituted <see cref="IMailboxNotificationSessionFactory" />, so what it proves is that the caller reacts
/// to a change that was reported to it. Whether MailKit is ever told about one is the part that only an IMAP server can
/// answer, and it is the part the IMAP behavior suite scoped and then deferred until push synchronization existed to
/// verify.
/// </para>
/// <para>
/// Two tests, because there are two answers a server can give. The first covers the whole life of a session that the
/// server does serve: the mode it reports, an ordinary elapsed wait, a delivery it observes, and the <c>\Seen</c> flag
/// it must not have touched to observe one. The second covers the server that declines, and it is the reason the first
/// one is not self-confirming — an effective mode read out of a factory that always answered <c>Push</c> would be no
/// evidence at all.
/// </para>
/// <para>
/// The order inside the first test is what makes it free of a sleep and free of a race. Opening the session selects the
/// folder, which snapshots the message count the server will report a change against, so mail delivered afterwards is a
/// change whether it arrives while the session is idling or between two waits. GreenMail polls rather than pushes its
/// own notifications, which is recorded with the rest of that server's behavior, so the wait is bounded generously
/// rather than tightly.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedPushNotificationTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The folder this class owns, so no mail another test delivers to the inbox ends a wait here.</summary>
    private const string WatchedFolderName = "PushWatched";

    /// <summary>How long a wait that is expected to observe a delivery may take on a loaded machine.</summary>
    /// <remarks>Generous because GreenMail's notification is polled; the test's own cancellation bounds it regardless.</remarks>
    private static readonly TimeSpan DeliveryWait = TimeSpan.FromSeconds(60);

    /// <summary>How long a wait that is expected to observe nothing runs for.</summary>
    /// <remarks>Short, because what it establishes is that an elapsed wait returns rather than how long one may run.</remarks>
    private static readonly TimeSpan QuietWait = TimeSpan.FromSeconds(2);

    private static readonly MailFolderResolution WatchedFolder = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("push-watched"),
        RemoteFolderPath.Create(WatchedFolderName, hierarchyDelimiter: '.'));

    [Fact]
    public async Task OpenAsync_AgainstAServerAdvertisingIdle_WatchesTheFolderAndObservesADeliveryWithoutReadingIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        await mailbox.RecreateFolderAsync(WatchedFolderName, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act, Assert
        await services.InScopeAsync(
            async (scope, token) =>
            {
                var result = await scope.GetRequiredService<IMailboxNotificationSessionFactory>().OpenAsync(
                    SyntheticMailAccount.AccountId,
                    WatchedFolder,
                    PolicyOf(scope),
                    token);

                Assert.Equal(MailSynchronizationMode.Push, result.EffectiveMode);
                Assert.NotNull(result.Session);

                await using (result.Session)
                {
                    // An untouched folder reports nothing and leaves the session ready to be re-entered, which is what
                    // renewal depends on. Asserted before the delivery so the observation below cannot be a wait that
                    // simply always returns a change.
                    Assert.Equal(
                        MailboxNotificationOutcome.WaitElapsed,
                        await result.Session.WaitForFolderChangeAsync(QuietWait, token));

                    // Delivered after the session selected the folder, so the count it snapshotted is what the server
                    // reports the change against and no ordering between the append and the wait can lose it.
                    await mailbox.AppendAsync(WatchedFolderName, "push-observed-delivery", token);

                    Assert.Equal(
                        MailboxNotificationOutcome.FolderChanged,
                        await result.Session.WaitForFolderChangeAsync(DeliveryWait, token));
                }

                return true;
            },
            cancellationToken);

        // The push session reports that a folder changed and never what changed, so nothing it did may have set the
        // flag. This is the read-only invariant in the one place a second fetch path could have hidden.
        RemoteSeenFlagAssertion.AssertNoneIsSeen(
            await mailbox.ReadAsync(WatchedFolderName, cancellationToken),
            "A push notification session observing a delivery");
    }

    /// <summary>Hides <c>IDLE</c> from a real connection and proves the folder is left to be polled.</summary>
    /// <remarks>
    /// The factory is constructed here rather than resolved, for the reason the relocation test constructs its write
    /// pool: the capability mask is the one thing this test controls, and the production registration hands the factory
    /// a plain <see cref="ImapClient" />. Everything else comes out of the composed container, so the connection, the
    /// resilience pipelines, and the authentication are the production ones.
    /// </remarks>
    [Fact]
    public async Task OpenAsync_AgainstAServerHidingIdle_ReportsPollingAndHoldsNoConnectionOpen()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);

        // Recreated rather than created, because the two tests of this class share the folder and neither may depend on
        // having run first: CREATE against a folder the other one already made is refused by the server.
        await mailbox.RecreateFolderAsync(WatchedFolderName, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);

        // Act
        var result = await services.InScopeAsync(
            (scope, token) => WithoutIdleExtension(scope).OpenAsync(
                SyntheticMailAccount.AccountId,
                WatchedFolder,
                PolicyOf(scope),
                token),
            cancellationToken);

        // Assert
        Assert.Equal(MailSynchronizationMode.Polling, result.EffectiveMode);
        Assert.Null(result.Session);
    }

    /// <summary>Builds the production factory over a client that reports every capability but <c>IDLE</c>.</summary>
    private static MailKitImapNotificationSessionFactory WithoutIdleExtension(IServiceProvider scope) =>
        new(
            () => CapabilityMaskedImapClient.HidingCapabilities(ImapCapabilities.Idle),
            scope.GetRequiredService<IImapAccountSettingsProvider>(),
            scope.GetRequiredService<IMailAccessTokenSource>(),
            scope.GetRequiredService<OutboundOperationExecutor>(),
            scope.GetRequiredService<ITransientFailureClassifier>(),
            scope.GetRequiredService<MailServerConnectionBudget>(),
            MailKitImapChangeSubscription.RequestFolderNotificationsAsync,
            TimeProvider.System);

    private static MailTransportSecurityPolicy PolicyOf(IServiceProvider scope) =>
        scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(SyntheticMailAccount.AccountId);
}
