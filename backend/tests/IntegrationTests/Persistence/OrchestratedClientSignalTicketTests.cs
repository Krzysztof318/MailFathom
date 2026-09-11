// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Host.Signals;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves that a signal ticket one host minted opens a connection the other host answered, exactly once.</summary>
/// <remarks>
/// <para>
/// This is the property the store exists for and the one nothing below a real server can establish. The spend is one
/// composed <c>DELETE ... RETURNING</c> whose returned row is the answer, so a statement that returned a row it had not
/// removed, a column that drifted from the entity, or a bound counted per process would all pass a unit test and would
/// reach an operator as a live channel that stops working at the replica count.
/// </para>
/// <para>
/// Two hosts rather than two calls on one, because a per-process store passes the second-call form and is exactly what
/// this change replaced. Each host composes its own service graph over the one orchestrated database, which is what a
/// second replica is, and each wraps that graph in its own minting so the clocks are a process's own as well.
/// </para>
/// <para>
/// A user of this class's own, provisioned and erased around each case, because the table carries a foreign key onto
/// the user record and the suite shares one database.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedClientSignalTicketTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>How many replicas present one ticket at once, enough that a caller reliably loses the race and well under the in-flight bound a process keeps.</summary>
    private const int ContendingReplicas = 8;

    /// <summary>The instant both replicas' clocks are set to, so a ticket's expiry is this class's rather than the wall clock's.</summary>
    private static readonly DateTimeOffset Instant = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A ticket minted on one replica opens a connection on another, which is what removes every need for session affinity.</summary>
    [Fact]
    public async Task RedeemAsync_ATicketAnotherHostMinted_NamesTheUserItWasMintedFor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mintingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var connectingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(mintingHost, user, cancellationToken);

        try
        {
            // Arrange
            var minted = await (await TicketsOnAsync(mintingHost, cancellationToken)).MintAsync(MailUserId.Create(user), cancellationToken);

            // Act
            var admitted = await (await TicketsOnAsync(connectingHost, cancellationToken)).RedeemAsync(minted?.Value, cancellationToken);

            // Assert
            Assert.Equal(MailUserId.Create(user), admitted);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(mintingHost, user);
        }
    }

    /// <summary>
    /// One ticket opens one connection whichever replica each presentation reaches, settled by the statement that
    /// removes and returns together rather than by a check either replica makes between two statements.
    /// </summary>
    [Fact]
    public async Task RedeemAsync_TheSameTicketOnTwoHosts_AdmitsTheFirstAndRefusesTheSecond()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mintingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var connectingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(mintingHost, user, cancellationToken);

        try
        {
            // Arrange
            var minted = await (await TicketsOnAsync(mintingHost, cancellationToken)).MintAsync(MailUserId.Create(user), cancellationToken);

            // Act
            var first = await (await TicketsOnAsync(connectingHost, cancellationToken)).RedeemAsync(minted?.Value, cancellationToken);
            var replayed = await (await TicketsOnAsync(mintingHost, cancellationToken)).RedeemAsync(minted?.Value, cancellationToken);

            // Assert
            Assert.Equal(MailUserId.Create(user), first);
            Assert.Null(replayed);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(mintingHost, user);
        }
    }

    /// <summary>
    /// Several replicas presenting one ticket at the same instant leave one connection admitted and the rest refused,
    /// settled by PostgreSQL rather than by a check any of them makes between two statements.
    /// </summary>
    /// <remarks>
    /// Every presentation is queued rather than awaited in turn, because a pair started one after the other runs the
    /// synchronous head of the first — the scope, the resolution, the command — before the second begins, and can be
    /// serialized by the scheduler alone. An implementation that read the row and then removed it would pass that
    /// arrangement, which is the exact defect the composed statement exists to rule out.
    /// </remarks>
    [Fact]
    public async Task RedeemAsync_ManyHostsPresentingOneTicketAtOnce_AdmitsExactlyOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var oneHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var anotherHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(oneHost, user, cancellationToken);

        try
        {
            // Arrange
            var oneReplica = await TicketsOnAsync(oneHost, cancellationToken);
            var anotherReplica = await TicketsOnAsync(anotherHost, cancellationToken);
            var minted = await oneReplica.MintAsync(MailUserId.Create(user), cancellationToken);

            // Act
            var attempts = await ConcurrentIdempotency.RunAsync(
                "Presenting one signal ticket from several replicas at once",
                ContendingReplicas,
                (ordinal, token) => (ordinal % 2 == 0 ? oneReplica : anotherReplica).RedeemAsync(minted?.Value, token),
                cancellationToken);

            // Assert
            attempts.AssertSingleEffect(attempts.Results.Count(admitted => admitted is not null));
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(oneHost, user);
        }
    }

    /// <summary>
    /// A ticket nobody presented in time opens nothing, and a live identifier presented with the wrong proof opens
    /// nothing and is spent finding out — both against the real statement rather than a dictionary standing in for it.
    /// </summary>
    [Fact]
    public async Task RedeemAsync_AnExpiredTicketAndAWrongProof_AreBothRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var clock = new FakeTimeProvider(Instant);
            var tickets = new ClientSignalTickets(await StoreOfAsync(host, cancellationToken), clock);
            var expiring = await tickets.MintAsync(MailUserId.Create(user), cancellationToken);
            var guessedAt = await tickets.MintAsync(MailUserId.Create(user), cancellationToken);
            var identifier = guessedAt!.Value[..guessedAt.Value.IndexOf('.', StringComparison.Ordinal)];

            // Act
            var guessed = await tickets.RedeemAsync($"{identifier}.{new string('A', 43)}", cancellationToken);
            var respent = await tickets.RedeemAsync(guessedAt.Value, cancellationToken);
            clock.Advance(ClientSignalTickets.Lifetime + TimeSpan.FromSeconds(1));
            var expired = await tickets.RedeemAsync(expiring?.Value, cancellationToken);

            // Assert
            Assert.Null(guessed);
            Assert.Null(respent);
            Assert.Null(expired);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>
    /// The ceiling on unspent tickets is counted by the statement that writes one, so a deployment already holding as
    /// many as it will hold refuses the next mint whichever replica issues it.
    /// </summary>
    /// <remarks>
    /// The bound is passed to the port rather than held by it, so this case states one the table already meets instead
    /// of writing ten thousand rows. Nothing below a real server settles it: the refusal is the
    /// <c>WHERE (SELECT count(*) …) &lt; @mostOutstanding</c> clause of the insert, and a fake counting its own
    /// dictionary is a different mechanism in a different process — which is exactly the per-process bound this table
    /// replaced.
    /// </remarks>
    [Fact]
    public async Task TryMintAsync_WhenTheDeploymentAlreadyHoldsTheCeiling_RefusesTheMint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var mintingHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await using var secondHost = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(mintingHost, user, cancellationToken);

        try
        {
            // Arrange
            var deployment = await StoreOfAsync(mintingHost, cancellationToken);
            var secondReplica = await StoreOfAsync(secondHost, cancellationToken);
            var held = await CountUnspentTicketsAsync(mintingHost, cancellationToken);

            // Act
            var refused = await secondReplica.TryMintAsync(
                Guid.NewGuid().ToString("N"),
                MailUserId.Create(user),
                new byte[32],
                Instant + ClientSignalTickets.Lifetime,
                (int)held,
                cancellationToken);

            var admitted = await deployment.TryMintAsync(
                Guid.NewGuid().ToString("N"),
                MailUserId.Create(user),
                new byte[32],
                Instant + ClientSignalTickets.Lifetime,
                (int)held + 1,
                cancellationToken);

            // Assert
            Assert.False(refused);
            Assert.True(admitted);
        }
        finally
        {
            await OrchestratedForeignUser.EraseAsync(mintingHost, user);
        }
    }

    /// <summary>Erasing a user takes their unspent tickets with them, so nothing about a person outlives their record.</summary>
    [Fact]
    public async Task EraseAsync_AUserHoldingAnUnspentTicket_TakesTheTicketWithThem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var user = Guid.NewGuid();

        await OrchestratedForeignUser.ProvisionAsync(host, user, cancellationToken);

        try
        {
            // Arrange
            var minted = await (await TicketsOnAsync(host, cancellationToken)).MintAsync(MailUserId.Create(user), cancellationToken);

            // Act
            await OrchestratedForeignUser.EraseAsync(host, user);

            // Assert
            Assert.Null(await (await TicketsOnAsync(host, cancellationToken)).RedeemAsync(minted?.Value, cancellationToken));
        }
        finally
        {
            // Erased a second time on the ordinary path, which reports that there was nothing to erase rather than
            // failing. What the block is for is the mint above: a store that could not answer would otherwise leave a
            // second row in the one shared settings_accounts and break every class the orderer runs after this one.
            await OrchestratedForeignUser.EraseAsync(host, user);
        }
    }

    /// <summary>Mints and spends through the host's own registered store, the way the minting route and the hub do.</summary>
    /// <remarks>
    /// Over a clock this class fixes rather than the wall clock, because the lifetime a ticket is judged against is
    /// thirty seconds and what these cases measure is the statement rather than elapsed time: a contended runner
    /// between the mint and the redemption would otherwise report a ticket that expired as a spend that returned
    /// nothing. Each replica gets its own instance at the same instant, as two processes would.
    /// </remarks>
    private static async Task<ClientSignalTickets> TicketsOnAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) =>
        new(await StoreOfAsync(host, cancellationToken), new FakeTimeProvider(Instant));

    /// <summary>Counts what the deployment holds right now, which is what the ceiling above is stated against.</summary>
    /// <remarks>Read rather than assumed to be zero: the table is the whole deployment's and the collection this class joins leaves whatever the classes before it minted and did not spend.</remarks>
    private static Task<long> CountUnspentTicketsAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .ClientSignalTickets
                .LongCountAsync(token),
            cancellationToken);

    private static Task<IClientSignalTicketStore> StoreOfAsync(
        OrchestratedMailFathomServices host,
        CancellationToken cancellationToken) => host.InScopeAsync(
            (scope, _) => Task.FromResult(scope.GetRequiredService<IClientSignalTicketStore>()),
            cancellationToken);
}
