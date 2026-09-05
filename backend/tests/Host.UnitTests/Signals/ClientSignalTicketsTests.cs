// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Signals;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Signals;

/// <summary>Covers what a connection ticket admits, what it refuses, and how long it stays worth presenting.</summary>
public sealed class ClientSignalTicketsTests
{
    private static readonly DateTimeOffset Instant = new(2026, 9, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A ticket names the owner the credential that minted it named, and nobody else.</summary>
    [Fact]
    public void Redeem_AFreshlyMintedTicket_NamesTheOwnerItWasMintedFor()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));
        var minted = tickets.Mint(SyntheticMailOwner.Deployment);

        // Act
        var owner = tickets.Redeem(minted?.Value);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, owner);
    }

    /// <summary>Two owners minting at once each get their own, so one connection can never be opened as the other person.</summary>
    [Fact]
    public void Redeem_TicketsMintedForTwoOwners_NamesEachOwnersTicketAsTheirs()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));
        var mine = tickets.Mint(SyntheticMailOwner.Deployment);
        var theirs = tickets.Mint(SyntheticMailOwner.Another);

        // Act
        var firstOwner = tickets.Redeem(mine?.Value);
        var secondOwner = tickets.Redeem(theirs?.Value);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, firstOwner);
        Assert.Equal(SyntheticMailOwner.Another, secondOwner);
    }

    /// <summary>A ticket opens one connection, so one read out of a log or a browser's history opens none.</summary>
    [Fact]
    public void Redeem_TheSameTicketTwice_AdmitsTheFirstAndRefusesTheSecond()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));
        var minted = tickets.Mint(SyntheticMailOwner.Deployment);

        // Act
        var first = tickets.Redeem(minted?.Value);
        var second = tickets.Redeem(minted?.Value);

        // Assert
        Assert.Equal(SyntheticMailOwner.Deployment, first);
        Assert.Null(second);
    }

    /// <summary>A ticket nobody presented in time stops working, which is what bounds one left where it should not be.</summary>
    [Fact]
    public void Redeem_PastTheTicketsLifetime_RefusesIt()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var tickets = new ClientSignalTickets(clock);
        var minted = tickets.Mint(SyntheticMailOwner.Deployment);

        // Act
        clock.Advance(ClientSignalTickets.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.Null(tickets.Redeem(minted?.Value));
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
    public void Redeem_AValueThatIsNotALiveTicket_RefusesIt(string? presented)
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));
        tickets.Mint(SyntheticMailOwner.Deployment);

        // Assert
        Assert.Null(tickets.Redeem(presented));
    }

    /// <summary>A live identifier presented with somebody else's proof is refused, and the identifier is spent finding out.</summary>
    [Fact]
    public void Redeem_ALiveIdentifierWithTheWrongProof_RefusesItAndSpendsTheTicket()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));
        var minted = tickets.Mint(SyntheticMailOwner.Deployment);
        var identifier = minted!.Value[..minted.Value.IndexOf('.', StringComparison.Ordinal)];

        // Act
        var guessed = tickets.Redeem($"{identifier}.{new string('A', 43)}");

        // Assert
        Assert.Null(guessed);
        Assert.Null(tickets.Redeem(minted.Value));
    }

    /// <summary>Every ticket is drawn afresh, so seeing one says nothing about the next.</summary>
    [Fact]
    public void Mint_CalledRepeatedly_ProducesADistinctValueEachTime()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));

        // Act
        var minted = Enumerable
            .Range(0, 50)
            .Select(_ => tickets.Mint(SyntheticMailOwner.Deployment)!.Value)
            .ToArray();

        // Assert
        Assert.Equal(minted.Length, minted.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>A ticket says when it stops working, so a client mints another rather than retrying one that cannot open a connection.</summary>
    [Fact]
    public void Mint_ForAnOwner_ReportsWhenPresentingItStopsWorking()
    {
        // Arrange
        var tickets = new ClientSignalTickets(new FakeTimeProvider(Instant));

        // Act
        var minted = tickets.Mint(SyntheticMailOwner.Deployment);

        // Assert
        Assert.Equal(Instant + ClientSignalTickets.Lifetime, minted?.ExpiresAt);
    }

    /// <summary>Expired tickets stop counting against the bound, so a quiet deployment never runs out of them.</summary>
    [Fact]
    public void Mint_AfterOutstandingTicketsExpired_MintsAgainRatherThanRefusing()
    {
        // Arrange
        var clock = new FakeTimeProvider(Instant);
        var tickets = new ClientSignalTickets(clock);

        for (var minted = 0; minted < ClientSignalTickets.MostOutstandingTickets; minted++)
        {
            tickets.Mint(SyntheticMailOwner.Deployment);
        }

        Assert.Null(tickets.Mint(SyntheticMailOwner.Deployment));

        // Act
        clock.Advance(ClientSignalTickets.Lifetime + TimeSpan.FromSeconds(1));

        // Assert
        Assert.NotNull(tickets.Mint(SyntheticMailOwner.Deployment));
    }
}
