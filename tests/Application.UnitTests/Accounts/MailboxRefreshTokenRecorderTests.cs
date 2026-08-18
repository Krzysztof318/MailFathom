// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Accounts;

/// <summary>Covers what the deployment decides before a mailbox grant an operator sent is allowed into the store.</summary>
/// <remarks>
/// The check in front of the write is the whole of this use case, and it is a security rule rather than a convenience:
/// a grant accepted for an account no configuration names is a long-lived credential for a real mailbox owner that
/// nothing reads and nobody knows is stored. Both substitutes stand at architectural boundaries, and what is asserted
/// on the store is the interaction — that the write happened once with the account and the token given, or that it did
/// not happen at all.
/// </remarks>
public sealed class MailboxRefreshTokenRecorderTests
{
    private static readonly MailAccountId Workspace = MailAccountId.Create("workspace");

    private readonly IMailboxRefreshTokenStore store = Substitute.For<IMailboxRefreshTokenStore>();

    [Fact]
    public async Task RecordAsync_AServedAccount_StoresTheTokenAgainstIt()
    {
        // Arrange
        var recorder = this.RecorderServing(Workspace);
        using var refreshToken = MailboxRefreshToken.FromText("a-refresh-token");

        // Act
        await recorder.RecordAsync(Workspace, refreshToken, TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).SaveTokenAsync(Workspace, refreshToken, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_AnAccountThisDeploymentDoesNotServe_IsRefusedWithoutStoringAnything()
    {
        // Arrange
        var recorder = this.RecorderServing(Workspace);
        var unknown = MailAccountId.Create("archive");
        using var refreshToken = MailboxRefreshToken.FromText("a-refresh-token");

        // Act
        var failure = await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => recorder.RecordAsync(unknown, refreshToken, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailAccountSelector.For(unknown), failure.RequestedAccount);
        await this.store.DidNotReceive().SaveTokenAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailboxRefreshToken>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An empty account list is a configured state — synchronization switched off — and it serves nobody.</summary>
    [Fact]
    public async Task RecordAsync_ADeploymentServingNoAccount_RefusesEveryGrant()
    {
        // Arrange
        var recorder = this.RecorderServing();
        using var refreshToken = MailboxRefreshToken.FromText("a-refresh-token");

        // Act, Assert
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(
            () => recorder.RecordAsync(Workspace, refreshToken, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Re-authorizing after a revocation is the second write for one account, and it must reach the store as such:
    /// keeping the first would leave the account holding a credential nothing chose between.
    /// </summary>
    [Fact]
    public async Task RecordAsync_TheSameAccountTwice_WritesBothThroughToTheStoreThatReplaces()
    {
        // Arrange
        var recorder = this.RecorderServing(Workspace);
        using var first = MailboxRefreshToken.FromText("the-first-token");
        using var second = MailboxRefreshToken.FromText("the-second-token");

        // Act
        await recorder.RecordAsync(Workspace, first, TestContext.Current.CancellationToken);
        await recorder.RecordAsync(Workspace, second, TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).SaveTokenAsync(Workspace, first, Arg.Any<CancellationToken>());
        await this.store.Received(1).SaveTokenAsync(Workspace, second, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_NoToken_IsRejectedBeforeTheAccountIsEvenLookedUp()
    {
        // Arrange
        var recorder = this.RecorderServing(Workspace);

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => recorder.RecordAsync(Workspace, refreshToken: null!, TestContext.Current.CancellationToken));
    }

    /// <summary>The grant is the authority here rather than at the transport, so an entrypoint that passed no filter meets the same refusal.</summary>
    [Fact]
    public async Task RecordAsync_ACallerGrantedOnlyTheAdministrativeRead_IsRefusedWithTheTransportAbsent()
    {
        // Arrange
        var recorder = this.RecorderServing(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminRead),
            Workspace);
        using var refreshToken = MailboxRefreshToken.FromText("a-refresh-token");

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RecordAsync(Workspace, refreshToken, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
        await this.store.DidNotReceive().SaveTokenAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailboxRefreshToken>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The grant is asked before the account is, so a refused caller learns nothing about which accounts this deployment serves.</summary>
    [Fact]
    public async Task RecordAsync_ACallerHoldingNothing_IsRefusedBeforeTheAccountIsResolved()
    {
        // Arrange
        var recorder = this.RecorderServing(AccessAuthorizations.ForCallerGranted());
        using var refreshToken = MailboxRefreshToken.FromText("a-refresh-token");

        // Act
        var refusal = await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            recorder.RecordAsync(Workspace, refreshToken, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomPermission.AdminCredentialsWrite, refusal.RequiredPermission);
    }

    private MailboxRefreshTokenRecorder RecorderServing(params MailAccountId[] servedAccountIds) =>
        this.RecorderServing(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.AdminCredentialsWrite),
            servedAccountIds);

    private MailboxRefreshTokenRecorder RecorderServing(
        AccessAuthorization authorization,
        params MailAccountId[] servedAccountIds)
    {
        var catalog = Substitute.For<IMailAccountCatalog>();
        catalog.ServedAccounts.Returns([.. servedAccountIds.Select(SyntheticServedAccount.Of)]);

        return new MailboxRefreshTokenRecorder(catalog, this.store, authorization);
    }
}
