// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Security.Cryptography;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Host.Signals;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers what a connection ticket admits, what it refuses, and how long it stays worth presenting.</summary>
public sealed class ClientSignalTicketsTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A ticket names the user the credential that minted it named, and nobody else.</summary>
    [Fact]
    public async Task RedeemAsync_AFreshlyMintedTicket_NamesTheUserItWasMintedFor()
    {
        // Arrange
        var tickets = TicketsOver(new InMemoryClientSignalTicketStore());
        var minted = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);

        // Act
        var user = await tickets.RedeemAsync(minted?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, user);
    }

    /// <summary>Two users minting at once each get their own, so one connection can never be opened as the other person.</summary>
    [Fact]
    public async Task RedeemAsync_TicketsMintedForTwoUsers_NamesEachUsersTicketAsTheirs()
    {
        // Arrange
        var tickets = TicketsOver(new InMemoryClientSignalTicketStore());
        var mine = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);
        var theirs = await tickets.MintAsync(SyntheticMailUser.Another, TestContext.Current.CancellationToken);

        // Act
        var firstUser = await tickets.RedeemAsync(mine?.Value, TestContext.Current.CancellationToken);
        var secondUser = await tickets.RedeemAsync(theirs?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, firstUser);
        Assert.Equal(SyntheticMailUser.Another, secondUser);
    }

    /// <summary>A ticket opens one connection, so one read out of a log or a browser's history opens none.</summary>
    [Fact]
    public async Task RedeemAsync_TheSameTicketTwice_AdmitsTheFirstAndRefusesTheSecond()
    {
        // Arrange
        var tickets = TicketsOver(new InMemoryClientSignalTicketStore());
        var minted = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);

        // Act
        var first = await tickets.RedeemAsync(minted?.Value, TestContext.Current.CancellationToken);
        var second = await tickets.RedeemAsync(minted?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, first);
        Assert.Null(second);
    }

    /// <summary>
    /// A ticket one replica minted opens a connection another replica answered, which is the whole reason the tickets
    /// left this process: the load balancer places the mint and the connection independently.
    /// </summary>
    [Fact]
    public async Task RedeemAsync_ATicketAnotherReplicaMinted_NamesTheUserItWasMintedFor()
    {
        // Arrange
        var deployment = new InMemoryClientSignalTicketStore();
        var mintingReplica = TicketsOver(deployment);
        var connectingReplica = TicketsOver(deployment);
        var minted = await mintingReplica.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);

        // Act
        var user = await connectingReplica.RedeemAsync(minted?.Value, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailUser.Deployment, user);
    }

    /// <summary>A ticket nobody presented in time stops working, which is what bounds one left where it should not be.</summary>
    [Fact]
    public async Task RedeemAsync_PastTheTicketsLifetime_RefusesIt()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var tickets = new ClientSignalTickets(new InMemoryClientSignalTicketStore(), clock);
        var minted = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);

        // Act
        clock.Advance(ClientSignalTickets.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.Null(await tickets.RedeemAsync(minted?.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>A value a caller wrote is refused however it is malformed, and none of the refusals says which.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-separator")]
    [InlineData(".proof-without-an-identifier")]
    [InlineData("identifier-without-a-proof.")]
    [InlineData("unknown.aGVsbG8")]
    public async Task RedeemAsync_AValueThatIsNotALiveTicket_RefusesIt(string? presented)
    {
        // Arrange
        var tickets = TicketsOver(new InMemoryClientSignalTicketStore());
        await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(await tickets.RedeemAsync(presented, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A value without the shape of a minted ticket costs the deployment no database work, which is what keeps a flood
    /// of handshakes off a store the hub reaches before anything has authenticated.
    /// </summary>
    [Theory]
    [InlineData("no-separator")]
    [InlineData("identifier.a-proof-outside-the-alphabet!")]
    [InlineData("identifier.A")]
    public async Task RedeemAsync_AValueWithoutTheShapeOfATicket_RefusesItWithoutAskingTheStore(string presented)
    {
        // Arrange
        var tickets = new ClientSignalTickets(
            new UnreachableClientSignalTicketStore(),
            new FakeTimeProvider(Instant));

        // Assert
        Assert.Null(await tickets.RedeemAsync(presented, TestContext.Current.CancellationToken));
    }

    /// <summary>A live identifier presented with somebody else's proof is refused, and the identifier is spent finding out.</summary>
    [Fact]
    public async Task RedeemAsync_ALiveIdentifierWithTheWrongProof_RefusesItAndSpendsTheTicket()
    {
        // Arrange
        var tickets = TicketsOver(new InMemoryClientSignalTicketStore());
        var minted = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);
        var identifier = minted!.Value[..minted.Value.IndexOf('.', StringComparison.Ordinal)];

        // Act
        var guessed = await tickets.RedeemAsync(
            $"{identifier}.{new string('A', 43)}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(guessed);
        Assert.Null(await tickets.RedeemAsync(minted.Value, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A store that cannot say whether a ticket stands leaves its caller unable to admit, rather than answering as
    /// though the ticket were unknown — the two are the same refusal to a client and a different one to an operator.
    /// </summary>
    [Fact]
    public async Task RedeemAsync_WhenTheStoreCannotBeReached_ReportsTheOutageRatherThanRefusingQuietly()
    {
        // Arrange
        var tickets = new ClientSignalTickets(
            new UnreachableClientSignalTicketStore(),
            new FakeTimeProvider(Instant));

        // Assert
        await Assert.ThrowsAsync<ClientSignalTicketStoreUnavailableException>(
            () => tickets.RedeemAsync($"identifier.{new string('A', 43)}", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A handshake arriving while this process is already redeeming as many as it will at once is refused unread, so a
    /// flood of them holds that many connections rather than the pool the rest of the deployment reads mail through.
    /// </summary>
    [Fact]
    public async Task RedeemAsync_WhileTheInFlightBoundIsFull_RefusesTheNextWithoutAskingTheStore()
    {
        // Arrange
        var deployment = new HeldClientSignalTicketStore();
        var tickets = TicketsOver(deployment);
        var minted = new List<string>();

        for (var ticket = 0; ticket <= ClientSignalTickets.MostRedemptionsInFlight; ticket++)
        {
            minted.Add((await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken))!.Value);
        }

        deployment.HoldRedemptions();

        var inFlight = minted
            .Take(ClientSignalTickets.MostRedemptionsInFlight)
            .Select(ticket => tickets.RedeemAsync(ticket, TestContext.Current.CancellationToken))
            .ToArray();

        await deployment.WaitUntilRedeemingAsync(ClientSignalTickets.MostRedemptionsInFlight);

        // Act
        var refused = await tickets.RedeemAsync(minted[^1], TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refused);
        Assert.Equal(ClientSignalTickets.MostRedemptionsInFlight, deployment.RedemptionCount);

        deployment.ReleaseRedemptions();
        Assert.All(await Task.WhenAll(inFlight), user => Assert.Equal(SyntheticMailUser.Deployment, user));

        // The bound is a ceiling on what is in flight rather than a quota spent for good, so the ticket refused while it
        // was full opens a connection on the next attempt.
        Assert.Equal(
            SyntheticMailUser.Deployment,
            await tickets.RedeemAsync(minted[^1], TestContext.Current.CancellationToken));
    }

    /// <summary>Every ticket is drawn afresh, so seeing one says nothing about the next.</summary>
    [Fact]
    public async Task MintAsync_CalledRepeatedly_ProducesADistinctValueEachTime()
    {
        // Arrange
        var tickets = TicketsOver(new InMemoryClientSignalTicketStore());
        var minted = new List<string>();

        // Act
        for (var ticket = 0; ticket < 50; ticket++)
        {
            minted.Add((await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken))!.Value);
        }

        // Assert
        Assert.Equal(minted.Count, minted.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The secret never reaches the deployment's store, so a row read out of the database or a backup opens nothing.</summary>
    [Fact]
    public async Task MintAsync_ForAUser_HoldsADigestOfTheSecretRatherThanTheSecret()
    {
        // Arrange
        var deployment = new InMemoryClientSignalTicketStore();
        var tickets = TicketsOver(deployment);

        // Act
        var minted = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);
        var identifier = minted!.Value[..minted.Value.IndexOf('.', StringComparison.Ordinal)];
        var secret = Base64Url.DecodeFromChars(minted.Value.AsSpan(identifier.Length + 1));
        var held = await deployment.RedeemAsync(identifier, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(held);
        Assert.Equal(SHA256.HashData(secret), held.SecretDigest.ToArray());
        Assert.NotEqual(secret, held.SecretDigest.ToArray());
    }

    /// <summary>A ticket says when it stops working, so a client mints another rather than retrying one that cannot open a connection.</summary>
    [Fact]
    public async Task MintAsync_ForAUser_ReportsWhenPresentingItStopsWorking()
    {
        // Arrange
        var tickets = TicketsOver(new InMemoryClientSignalTicketStore());

        // Act
        var minted = await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Instant + ClientSignalTickets.Lifetime, minted?.ExpiresAt);
    }

    /// <summary>
    /// The bound counts the deployment rather than a process, so two replicas minting against one store refuse where
    /// one would have — which is what a ceiling naming the deployment has to mean.
    /// </summary>
    [Fact]
    public async Task MintAsync_WhenAnotherReplicaHoldsTheBound_RefusesRatherThanMintingItsOwnShare()
    {
        // Arrange
        var deployment = new InMemoryClientSignalTicketStore();
        var mintingReplica = TicketsOver(deployment);
        var secondReplica = TicketsOver(deployment);

        for (var minted = 0; minted < ClientSignalTickets.MostOutstandingTickets; minted++)
        {
            await mintingReplica.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.Null(await secondReplica.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken));
    }

    /// <summary>Expired tickets stop counting against the bound, so a quiet deployment never runs out of them.</summary>
    [Fact]
    public async Task MintAsync_AfterOutstandingTicketsExpired_MintsAgainRatherThanRefusing()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var tickets = new ClientSignalTickets(new InMemoryClientSignalTicketStore(), clock);

        for (var minted = 0; minted < ClientSignalTickets.MostOutstandingTickets; minted++)
        {
            await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);
        }

        Assert.Null(await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken));

        // Act
        clock.Advance(ClientSignalTickets.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken));
    }

    /// <summary>The sweep runs on the minting path at most once per lifetime, so a busy deployment issues one delete rather than one per connection.</summary>
    [Fact]
    public async Task MintAsync_SeveralTimesWithinOneLifetime_SweepsOnceRatherThanPerMint()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var deployment = new InMemoryClientSignalTicketStore();
        var tickets = new ClientSignalTickets(deployment, clock);

        clock.Advance(ClientSignalTickets.Lifetime);

        // Act
        for (var minted = 0; minted < 5; minted++)
        {
            await tickets.MintAsync(SyntheticMailUser.Deployment, TestContext.Current.CancellationToken);
        }

        // Assert
        Assert.Equal(1, deployment.RemovalCount);
    }

    private static ClientSignalTickets TicketsOver(IClientSignalTicketStore store) =>
        new(store, new FakeTimeProvider(Instant));

    /// <summary>A store whose redemptions stand still until a test lets them finish, so the in-flight bound is reachable.</summary>
    private sealed class HeldClientSignalTicketStore : IClientSignalTicketStore
    {
        private readonly InMemoryClientSignalTicketStore deployment = new();
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private volatile bool holding;
        private int redeeming;

        /// <summary>Gets how many redemptions the store has been asked for.</summary>
        internal int RedemptionCount => this.redeeming;

        internal void HoldRedemptions() => this.holding = true;

        internal void ReleaseRedemptions() => this.released.TrySetResult();

        /// <summary>Waits until the store is holding the given number of redemptions, so the bound is full before the next arrives.</summary>
        internal async Task WaitUntilRedeemingAsync(int count)
        {
            while (Volatile.Read(ref this.redeeming) < count)
            {
                await Task.Yield();
            }
        }

        public Task<bool> TryMintAsync(
            string identifier,
            MailUserId user,
            ReadOnlyMemory<byte> secretDigest,
            DateTimeOffset expiresAt,
            int mostOutstanding,
            CancellationToken cancellationToken) =>
            this.deployment.TryMintAsync(identifier, user, secretDigest, expiresAt, mostOutstanding, cancellationToken);

        public async Task<RedeemedClientSignalTicket?> RedeemAsync(string identifier, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.redeeming);

            if (this.holding)
            {
                await this.released.Task;
            }

            return await this.deployment.RedeemAsync(identifier, cancellationToken);
        }

        public Task RemoveExpiredAsync(DateTimeOffset removableFrom, CancellationToken cancellationToken) =>
            this.deployment.RemoveExpiredAsync(removableFrom, cancellationToken);
    }
}
