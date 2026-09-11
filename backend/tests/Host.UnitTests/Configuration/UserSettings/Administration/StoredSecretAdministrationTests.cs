// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.UserSettings.Administration;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.UserSettings.Administration;

public sealed class StoredSecretAdministrationTests
{
    [Fact]
    public async Task StoreAsync_AnExistingUserAndConfiguredRing_CommitsAndReturnsTheStoreReference()
    {
        // Arrange
        var user = SyntheticMailUser.Deployment;
        var storedReference = DatabaseSecretReference.Create(
            new Guid("019925df-96f4-7c6d-8f91-b9f6cf27f5b2"));
        var users = Substitute.For<IUserSettingsDocumentReader>();
        users.ReadAsync(user, Arg.Any<CancellationToken>()).Returns(
            new UserSettingsDocument(user, "user", "{}", 1));
        var store = Substitute.For<IStoredSecretStore>();
        store.CanStore.Returns(true);
        store.StoreAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<DatabaseSecretReference>(),
                user,
                Arg.Any<SecretName>(),
                Arg.Any<ResolvedSecret>(),
                Arg.Any<CancellationToken>())
            .Returns(storedReference);
        var service = CreateService(users, store);
        Assert.True(SecretName.TryCreate("primary-password", out var name));
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");

        // Act
        var result = await service.StoreAsync(user, name, material, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredSecretProvisioningOutcome.Stored, result.Outcome);
        Assert.Equal(storedReference, result.Reference);
    }

    [Fact]
    public async Task StoreAsync_ADeploymentWithoutAKeyRing_RefusesBeforeReadingTheUser()
    {
        // Arrange
        var users = Substitute.For<IUserSettingsDocumentReader>();
        var store = Substitute.For<IStoredSecretStore>();
        store.CanStore.Returns(false);
        var service = CreateService(users, store);
        Assert.True(SecretName.TryCreate("primary-password", out var name));
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");

        // Act
        var result = await service.StoreAsync(
            SyntheticMailUser.Deployment,
            name,
            material,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredSecretProvisioningOutcome.KeyRingUnavailable, result.Outcome);
        await users.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StoreAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var users = Substitute.For<IUserSettingsDocumentReader>();
        var store = Substitute.For<IStoredSecretStore>();
        var service = CreateService(users, store, [MailFathomPermission.AdminRead]);
        Assert.True(SecretName.TryCreate("primary-password", out var name));
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => service.StoreAsync(
                SyntheticMailUser.Deployment,
                name,
                material,
                TestContext.Current.CancellationToken));
        await store.DidNotReceiveWithAnyArgs().StoreAsync(
            default!,
            default,
            default,
            default,
            default!,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StoreAsync_AnUnknownUser_IsRefusedBeforeTheStoreIsReached()
    {
        // Arrange
        var users = Substitute.For<IUserSettingsDocumentReader>();
        var store = Substitute.For<IStoredSecretStore>();
        store.CanStore.Returns(true);
        var service = CreateService(users, store);
        Assert.True(SecretName.TryCreate("primary-password", out var name));
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");

        // Act
        var result = await service.StoreAsync(
            SyntheticMailUser.Deployment,
            name,
            material,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredSecretProvisioningOutcome.UnknownUser, result.Outcome);
        await store.DidNotReceiveWithAnyArgs().StoreAsync(
            default!,
            default,
            default,
            default,
            default!,
            TestContext.Current.CancellationToken);
    }

    private static StoredSecretAdministration CreateService(
        IUserSettingsDocumentReader users,
        IStoredSecretStore store,
        IReadOnlyList<MailFathomPermission>? granted = null)
    {
        var principals = Substitute.For<IAuthorizedPrincipalSource>();
        principals.Current.Returns(AuthorizedPrincipal.Caller(
            "operations",
            granted ?? [MailFathomPermission.AdminConfigurationWrite]));
        var session = Substitute.For<IPersistenceSession>();
        session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(session);

        return new StoredSecretAdministration(
            new AccessAuthorization(principals),
            users,
            store,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                TimeProvider.System));
    }
}
