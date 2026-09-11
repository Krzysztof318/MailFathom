// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Access.Sessions;
using MailFathom.Domain.Access;
using MailFathom.Host.Security.Sessions;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that a client signed in on one replica is accepted by another, and that one act ends their session on every replica at once.</summary>
/// <remarks>
/// <para>
/// This is the whole of what moving the sessions into PostgreSQL was for, and none of it is decidable below a real
/// server. Two replicas rather than two calls on one, because a process-held dictionary answers the second-call form
/// correctly and is exactly what this change replaced; each host composes its own service graph over the one
/// orchestrated database, which is what a second replica is.
/// </para>
/// <para>
/// The three refusals a signed-in client can meet are each a statement rather than a check in the process. A renewal
/// removes and re-inserts in one transaction, so one token presented twice leaves one live session whichever replica
/// each presentation reached. A mint and a renewal each take the user row and then the enabled credential row before
/// they write, so an operator's disable either waits for them and then removes what they wrote, or commits first and
/// leaves them matching no row — a version reading the flag instead of locking it passes every unit test and leaves a
/// live session behind exactly the act that was meant to end one. And the rows carry cascading foreign keys onto both
/// the user and the credential, which is what reaches a session whose endpoint required no credential at all.
/// </para>
/// <para>
/// A user of this class's own, provisioned and erased around each case, because the table keys onto the user record and
/// the suite shares one database.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedClientSessionTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many replicas present one token at once, enough that a caller reliably loses the race.</summary>
    private const int ContendingReplicas = 8;

    private const string StoredHash = "$mf1$stored$orchestrated$";

    /// <summary>The instant every replica's clock is set to, so a session's expiry is this class's rather than the wall clock's.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<MailFathomPermission> WholeMailSurface =
        MailFathomPermission.PublishedFor(ProtectedSurface.Mail);

    /// <summary>A session minted on one replica authenticates on another, which is what removes every need for session affinity.</summary>
    [Fact]
    public async Task VerifyAsync_ASessionAnotherReplicaMinted_AdmitsTheCallerItWasMintedFor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mintingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var servingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(mintingHost, user, cancellationToken);

        try
        {
            // Arrange
            var credential = await ProvisionCredentialAsync(mintingHost, user, "another-replica", cancellationToken);
            var minted = await (await SessionsOnAsync(mintingHost, cancellationToken))
                .MintAsync(Admitted(user, credential), cancellationToken);

            // Act
            var admitted = await (await SessionsOnAsync(servingHost, cancellationToken))
                .VerifyAsync(minted.Token?.Value, cancellationToken);

            // Assert
            Assert.Equal(ClientSessionMintOutcome.Minted, minted.Outcome);
            Assert.Equal(MailUserId.Create(user), admitted?.User);
            Assert.Equal(credential, admitted?.CredentialId);
            Assert.Equal(WholeMailSurface, admitted?.Permissions);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(mintingHost, user);
        }
    }

    /// <summary>Disabling a credential on one replica refuses the sessions it minted on every other, which is what makes an operator's act reach a client already signed in.</summary>
    [Fact]
    public async Task VerifyAsync_ACredentialDisabledOnAnotherReplica_RefusesTheSessionItMinted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mintingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var administeringHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(mintingHost, user, cancellationToken);

        try
        {
            // Arrange
            var credential = await ProvisionCredentialAsync(mintingHost, user, "disabled-elsewhere", cancellationToken);
            var minted = await (await SessionsOnAsync(mintingHost, cancellationToken))
                .MintAsync(Admitted(user, credential), cancellationToken);

            // Act
            var written = await SetEnabledAsync(administeringHost, user, credential, enabled: false, cancellationToken);

            // Assert
            Assert.Equal(UserCredentialWriteOutcome.Written, written);
            Assert.Null(await (await SessionsOnAsync(mintingHost, cancellationToken))
                .VerifyAsync(minted.Token?.Value, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(mintingHost, user);
        }
    }

    /// <summary>
    /// One token renewed by several replicas at once leaves one live session, settled by the removal and the
    /// replacement being one transaction rather than by a check any replica makes between two statements.
    /// </summary>
    /// <remarks>
    /// Every presentation is queued rather than awaited in turn, because a pair started one after the other runs the
    /// synchronous head of the first before the second begins and can be serialized by the scheduler alone. A renewal
    /// that read the row, judged it, and then removed it would pass that arrangement and would hand two clients a live
    /// token from one sign-in.
    /// </remarks>
    [Fact]
    public async Task RenewAsync_OneTokenPresentedByManyReplicasAtOnce_RenewsExactlyOnce()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var oneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var anotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(oneHost, user, cancellationToken);

        try
        {
            // Arrange
            var credential = await ProvisionCredentialAsync(oneHost, user, "renewed-at-once", cancellationToken);
            var oneReplica = await SessionsOnAsync(oneHost, cancellationToken);
            var anotherReplica = await SessionsOnAsync(anotherHost, cancellationToken);
            var minted = await oneReplica.MintAsync(Admitted(user, credential), cancellationToken);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                "Renewing one client session from several replicas at once",
                ContendingReplicas,
                (ordinal, token) => (ordinal % 2 == 0 ? oneReplica : anotherReplica)
                    .RenewAsync(minted.Token?.Value, token),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect(attempts.Results.Count(renewed => renewed is not null));
            Assert.Equal(1, await CountSessionsOfAsync(oneHost, credential, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(oneHost, user);
        }
    }

    /// <summary>A sign-in racing the disable that would end it leaves no live session, whichever of the two commits first.</summary>
    /// <remarks>
    /// The one claim the lock order exists for. The mint holds the user row and then the enabled credential row until
    /// it commits, so a disable arriving first leaves it matching no row, and one arriving second waits and then
    /// removes what it wrote. A mint that read the flag instead would answer a client with a token nothing was ever
    /// going to end.
    /// </remarks>
    [Fact]
    public async Task MintAsync_ASignInRacingTheDisableThatWouldEndIt_LeavesNoLiveSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var signingInHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var administeringHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(signingInHost, user, cancellationToken);

        try
        {
            // Arrange
            var credential = await ProvisionCredentialAsync(signingInHost, user, "signed-in-while-disabled", cancellationToken);
            var sessions = await SessionsOnAsync(signingInHost, cancellationToken);

            // Act
            var signingIn = Task.Run(() => sessions.MintAsync(Admitted(user, credential), cancellationToken), cancellationToken);
            var disabling = Task.Run(
                () => SetEnabledAsync(administeringHost, user, credential, enabled: false, cancellationToken),
                cancellationToken);

            await Task.WhenAll(signingIn, disabling);

            // Assert
            Assert.Equal(UserCredentialWriteOutcome.Written, await disabling);
            Assert.Equal(0, await CountSessionsOfAsync(signingInHost, credential, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(signingInHost, user);
        }
    }

    /// <summary>A renewal racing the same disable leaves no live session either, so a client cannot outlast the act by renewing across it.</summary>
    [Fact]
    public async Task RenewAsync_ARenewalRacingTheDisableThatWouldEndIt_LeavesNoLiveSession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var renewingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var administeringHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(renewingHost, user, cancellationToken);

        try
        {
            // Arrange
            var credential = await ProvisionCredentialAsync(renewingHost, user, "renewed-while-disabled", cancellationToken);
            var sessions = await SessionsOnAsync(renewingHost, cancellationToken);
            var minted = await sessions.MintAsync(Admitted(user, credential), cancellationToken);

            // Act
            var renewing = Task.Run(() => sessions.RenewAsync(minted.Token?.Value, cancellationToken), cancellationToken);
            var disabling = Task.Run(
                () => SetEnabledAsync(administeringHost, user, credential, enabled: false, cancellationToken),
                cancellationToken);

            await Task.WhenAll(renewing, disabling);

            // Assert
            Assert.Equal(UserCredentialWriteOutcome.Written, await disabling);
            Assert.Equal(0, await CountSessionsOfAsync(renewingHost, credential, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(renewingHost, user);
        }
    }

    /// <summary>Deleting a credential takes its live sessions with it, by the cascade rather than by a walk the deleting replica performs.</summary>
    [Fact]
    public async Task DeleteAsync_ACredentialHoldingALiveSession_TakesTheSessionWithIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mintingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var administeringHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(mintingHost, user, cancellationToken);

        try
        {
            // Arrange
            var credential = await ProvisionCredentialAsync(mintingHost, user, "deleted-elsewhere", cancellationToken);
            var minted = await (await SessionsOnAsync(mintingHost, cancellationToken))
                .MintAsync(Admitted(user, credential), cancellationToken);

            // Act
            var removed = await administeringHost.InScopeAsync(
                (scope, token) => Credentials(scope).DeleteAsync(MailUserId.Create(user), credential, token),
                cancellationToken);

            // Assert
            Assert.Equal(UserCredentialWriteOutcome.Written, removed);
            Assert.Null(await (await SessionsOnAsync(mintingHost, cancellationToken))
                .VerifyAsync(minted.Token?.Value, cancellationToken));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(mintingHost, user);
        }
    }

    /// <summary>Erasing a person takes a session no credential stands behind, which is the one the credential cascade cannot reach.</summary>
    /// <remarks>
    /// An endpoint requiring no credential still mints a session, and that row names the user and nothing else. The
    /// foreign key onto the user record is what removes it, which is why the session's credential is nullable rather
    /// than a sentinel no key could point at.
    /// </remarks>
    [Fact]
    public async Task EraseAsync_AUserWhoseSessionNamesNoCredential_TakesTheSessionWithThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var minted = await (await SessionsOnAsync(host, cancellationToken))
                .MintAsync(Admitted(user, credentialId: Guid.Empty), cancellationToken);

            // Act
            await OrchestratedForeignUser.EraseAsync(host, user);

            // Assert
            Assert.Equal(ClientSessionMintOutcome.Minted, minted.Outcome);
            Assert.Null(await (await SessionsOnAsync(host, cancellationToken))
                .VerifyAsync(minted.Token?.Value, cancellationToken));
        }
        finally
        {
            // Erased a second time on the ordinary path, which reports that there was nothing to erase rather than
            // failing. What the block is for is the mint above: a store that could not answer would otherwise leave a
            // second row in the one shared settings_accounts and break every class the orderer runs after this one.
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    private static AdmittedUserCredential Admitted(Guid user, Guid credentialId) =>
        new(credentialId, MailUserId.Create(user), WholeMailSurface);

    /// <summary>Mints and verifies through the host's own registered store, the way the exchange and the handler do.</summary>
    /// <remarks>Over a clock this class fixes rather than the wall clock, so a session's expiry and the sweep's threshold are this class's. Each replica gets its own instance at the same instant, as two processes would.</remarks>
    private static async Task<ClientSessionTokens> SessionsOnAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) =>
        new(
            await host.InScopeAsync(
                (scope, _) => Task.FromResult(scope.GetRequiredService<IClientSessionStore>()),
                cancellationToken),
            new FakeTimeProvider(Instant));

    private static IUserCredentialStore Credentials(IServiceProvider scope) =>
        scope.GetRequiredService<IUserCredentialStore>();

    private static async Task<Guid> ProvisionCredentialAsync(
        OrchestratedMailFathomServices host,
        Guid user,
        string lookup,
        CancellationToken cancellationToken)
    {
        var credentialId = Guid.CreateVersion7();

        var outcome = await host.InScopeAsync(
            (scope, token) => Credentials(scope).CreateAsync(
                credentialId,
                MailUserId.Create(user),
                UserCredentialMethod.Password,
                UserCredentialLookup.ForUsername(UserCredentialUsername.Create($"session-{lookup}")),
                StoredHash,
                WholeMailSurface,
                token),
            cancellationToken);

        Assert.Equal(UserCredentialWriteOutcome.Written, outcome);

        return credentialId;
    }

    private static Task<UserCredentialWriteOutcome> SetEnabledAsync(
        OrchestratedMailFathomServices host,
        Guid user,
        Guid credentialId,
        bool enabled,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, token) => Credentials(scope).SetEnabledAsync(MailUserId.Create(user), credentialId, enabled, token),
            cancellationToken);

    /// <summary>Counts what the deployment holds for one credential, which is what the racing cases are stated against.</summary>
    /// <remarks>Counted rather than read back through a token, because a mint that lost the race answers no token at all and the claim is about what the table is left holding either way.</remarks>
    private static Task<int> CountSessionsOfAsync(
        OrchestratedMailFathomServices host,
        Guid credentialId,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .ClientSessions
                .CountAsync(session => session.CredentialId == credentialId, token),
            cancellationToken);
}
