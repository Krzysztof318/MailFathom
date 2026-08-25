// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the accounts route puts on the wire, which is what a client renders its mailbox list from.</summary>
/// <remarks>
/// The use case's own decisions — whose accounts these are, what a state means, and what a caller without the grant is
/// answered — are covered where they are taken. What is asserted here is the translation: that every fact the use case
/// produced reaches the response, and that nothing of the mailbox travels beside them.
/// </remarks>
public sealed class ClientMailAccountsEndpointTests
{
    private static readonly DateTimeOffset SynchronizedAt = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly ServedMailAccount Work = SyntheticServedAccount.Of("work");

    private static readonly ServedMailAccount Private = SyntheticServedAccount.Of("private");

    /// <summary>The path a client appends to the address it was configured with, pinned because the client composes it from a constant of its own.</summary>
    [Fact]
    public void MailAccountsRoute_IsThePathAClientComposes() =>
        Assert.Equal("/accounts", ClientMailAccountsEndpoint.MailAccountsRoute);

    /// <summary>Each account reaches the wire as its two names, its state, the instant its copy last moved, and whether more is known to be coming.</summary>
    [Fact]
    public void For_AnAccountThatHasSynchronized_CarriesBothNamesTheStateAndTheInstant()
    {
        // Arrange
        var directory = Directory(Freshness(Work, MailSynchronizationState.Synchronized, SynchronizedAt));

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.True(response.SynchronizationEnabled);
        Assert.Equal(
            new ClientMailAccountResponse(
                Work.Id.Value,
                Work.DisplayName.Value,
                nameof(MailSynchronizationState.Synchronized),
                SynchronizedAt,
                Behind: false),
            Assert.Single(response.Accounts));
    }

    /// <summary>Each state reaches the wire as its own name, which is what lets a client tell a stale copy from a failing account and both from an unreachable one.</summary>
    [Theory]
    [InlineData(MailSynchronizationState.NeverSynchronized)]
    [InlineData(MailSynchronizationState.Synchronized)]
    [InlineData(MailSynchronizationState.Failing)]
    [InlineData(MailSynchronizationState.Unreachable)]
    public void For_AnyState_PublishesItUnderItsOwnName(MailSynchronizationState state)
    {
        // Arrange
        var directory = Directory(Freshness(Work, state, SynchronizedAt));

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Equal(state.ToString(), Assert.Single(response.Accounts).SynchronizationState);
    }

    /// <summary>An account still catching up says so beside its state, because a working refresh and a failing one both leave a mailbox behind.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_AnAccountWithMailStillToTakeIn_SaysSoBesideItsState(bool behind)
    {
        // Arrange
        var directory = Directory(
            Freshness(Work, MailSynchronizationState.Synchronized, SynchronizedAt, behind));

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Equal(behind, Assert.Single(response.Accounts).Behind);
    }

    /// <summary>An account nothing has ever synchronized carries no instant rather than one nobody can read.</summary>
    [Fact]
    public void For_AnAccountWithNoProgress_CarriesNoInstant()
    {
        // Arrange
        var directory = Directory(
            Freshness(Work, MailSynchronizationState.NeverSynchronized, lastSynchronizedAt: null));

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Null(Assert.Single(response.Accounts).LastSynchronizedAt);
    }

    /// <summary>The order the use case answered in is the order a client renders, so nothing here sorts it again.</summary>
    [Fact]
    public void For_SeveralAccounts_KeepsTheOrderTheUseCaseAnsweredIn()
    {
        // Arrange
        var directory = Directory(
            Freshness(Private, MailSynchronizationState.Failing, SynchronizedAt),
            Freshness(Work, MailSynchronizationState.Synchronized, SynchronizedAt));

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Equal(
            [Private.Id.Value, Work.Id.Value],
            response.Accounts.Select(account => account.Id));
    }

    /// <summary>An owner with no account reads an empty collection, which is a state a client renders rather than an error.</summary>
    [Fact]
    public void For_AnOwnerWithNoAccount_CarriesAnEmptyCollection()
    {
        // Arrange
        var directory = new MailAccountFreshnessDirectory(SynchronizationEnabled: true, []);

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Empty(response.Accounts);
    }

    /// <summary>
    /// The folders the use case carries are the folder route's business, and the mailbox list stays the answer that
    /// grows with nothing but the account count: an account whose folders are known reaches the wire as the same five
    /// fields an account whose folders are not does.
    /// </summary>
    [Fact]
    public void For_AnAccountWhoseFoldersAreKnown_PublishesNothingOfThem()
    {
        // Arrange
        var directory = Directory(new MailAccountFreshness(
            Work,
            MailSynchronizationState.Synchronized,
            SynchronizedAt,
            IsBehind: false,
            [
                new(
                    MailFolderAlias.Create("inbox"),
                    MailSynchronizationState.Synchronized,
                    SynchronizedAt,
                    IsBehind: false),
            ]));

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Equal(
            new ClientMailAccountResponse(
                Work.Id.Value,
                Work.DisplayName.Value,
                nameof(MailSynchronizationState.Synchronized),
                SynchronizedAt,
                Behind: false),
            Assert.Single(response.Accounts));
    }

    /// <summary>The deployment-wide switch is reported beside the accounts, because no per-account value carries it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_ADeploymentThatSwitchedSynchronizationOff_SaysSoBesideTheAccounts(bool synchronizationEnabled)
    {
        // Arrange
        var directory = new MailAccountFreshnessDirectory(
            synchronizationEnabled,
            [Freshness(Work, MailSynchronizationState.Synchronized, SynchronizedAt)]);

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Equal(synchronizationEnabled, response.SynchronizationEnabled);
    }

    private static MailAccountFreshnessDirectory Directory(params MailAccountFreshness[] accounts) =>
        new(SynchronizationEnabled: true, accounts);

    private static MailAccountFreshness Freshness(
        ServedMailAccount account,
        MailSynchronizationState state,
        DateTimeOffset? lastSynchronizedAt,
        bool isBehind = false) =>
        new(account, state, lastSynchronizedAt, isBehind, []);
}
