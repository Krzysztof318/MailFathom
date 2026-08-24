// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
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

    /// <summary>Each account reaches the wire as its two names, its state, and the instant its copy last moved.</summary>
    [Fact]
    public void For_AnAccountThatHasSynchronized_CarriesBothNamesTheStateAndTheInstant()
    {
        // Arrange
        var directory = new MailAccountFreshnessDirectory(
            SynchronizationEnabled: true,
            [new(Work, MailAccountSynchronizationState.Synchronized, SynchronizedAt)]);

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.True(response.SynchronizationEnabled);
        Assert.Equal(
            new ClientMailAccountResponse(
                Work.Id.Value,
                Work.DisplayName.Value,
                nameof(MailAccountSynchronizationState.Synchronized),
                SynchronizedAt),
            Assert.Single(response.Accounts));
    }

    /// <summary>Each state reaches the wire as its own name, which is what lets a client tell a stale copy from a failing account.</summary>
    [Theory]
    [InlineData(MailAccountSynchronizationState.NeverSynchronized)]
    [InlineData(MailAccountSynchronizationState.Synchronized)]
    [InlineData(MailAccountSynchronizationState.Failing)]
    public void For_AnyState_PublishesItUnderItsOwnName(MailAccountSynchronizationState state)
    {
        // Arrange
        var directory = new MailAccountFreshnessDirectory(
            SynchronizationEnabled: true,
            [new(Work, state, SynchronizedAt)]);

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Equal(state.ToString(), Assert.Single(response.Accounts).SynchronizationState);
    }

    /// <summary>An account nothing has ever synchronized carries no instant rather than one nobody can read.</summary>
    [Fact]
    public void For_AnAccountWithNoProgress_CarriesNoInstant()
    {
        // Arrange
        var directory = new MailAccountFreshnessDirectory(
            SynchronizationEnabled: true,
            [new(Work, MailAccountSynchronizationState.NeverSynchronized, LastSynchronizedAt: null)]);

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
        var directory = new MailAccountFreshnessDirectory(
            SynchronizationEnabled: true,
            [
                new(Private, MailAccountSynchronizationState.Failing, SynchronizedAt),
                new(Work, MailAccountSynchronizationState.Synchronized, SynchronizedAt),
            ]);

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

    /// <summary>The deployment-wide switch is reported beside the accounts, because no per-account value carries it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void For_ADeploymentThatSwitchedSynchronizationOff_SaysSoBesideTheAccounts(bool synchronizationEnabled)
    {
        // Arrange
        var directory = new MailAccountFreshnessDirectory(
            synchronizationEnabled,
            [new(Work, MailAccountSynchronizationState.Synchronized, SynchronizedAt)]);

        // Act
        var response = ClientMailAccountsResponse.For(directory);

        // Assert
        Assert.Equal(synchronizationEnabled, response.SynchronizationEnabled);
    }
}
