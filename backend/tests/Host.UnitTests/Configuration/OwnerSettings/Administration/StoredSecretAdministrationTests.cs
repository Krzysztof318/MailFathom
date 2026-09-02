// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.OwnerSettings.Administration;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.OwnerSettings.Administration;

public sealed class StoredSecretAdministrationTests
{
    [Fact]
    public async Task StoreAsync_AnExistingOwnerAndConfiguredRing_CommitsAndReturnsTheStoreReference()
    {
        // Arrange
        var owner = SyntheticMailOwner.Deployment;
        var storedReference = DatabaseSecretReference.Create(
            new Guid("019925df-96f4-7c6d-8f91-b9f6cf27f5b2"));
        var owners = Substitute.For<IOwnerSettingsDocumentReader>();
        owners.ReadAsync(owner, Arg.Any<CancellationToken>()).Returns(
            new OwnerSettingsDocument(owner, "owner", "{}", 1, WrittenAtRuntime: true));
        var store = Substitute.For<IStoredSecretStore>();
        store.CanStore.Returns(true);
        store.StoreAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<DatabaseSecretReference>(),
                owner,
                Arg.Any<SecretName>(),
                Arg.Any<ResolvedSecret>(),
                Arg.Any<CancellationToken>())
            .Returns(storedReference);
        var service = CreateService(owners, store);
        Assert.True(SecretName.TryCreate("primary-password", out var name));
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");

        // Act
        var result = await service.StoreAsync(owner, name, material, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredSecretProvisioningOutcome.Stored, result.Outcome);
        Assert.Equal(storedReference, result.Reference);
    }

    [Fact]
    public async Task StoreAsync_ADeploymentWithoutAKeyRing_RefusesBeforeReadingTheOwner()
    {
        // Arrange
        var owners = Substitute.For<IOwnerSettingsDocumentReader>();
        var store = Substitute.For<IStoredSecretStore>();
        store.CanStore.Returns(false);
        var service = CreateService(owners, store);
        Assert.True(SecretName.TryCreate("primary-password", out var name));
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");

        // Act
        var result = await service.StoreAsync(
            SyntheticMailOwner.Deployment,
            name,
            material,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredSecretProvisioningOutcome.KeyRingUnavailable, result.Outcome);
        await owners.DidNotReceiveWithAnyArgs().ReadAsync(default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StoreAsync_ACallerHoldingOnlyTheAdministrativeRead_IsRefused()
    {
        // Arrange
        var owners = Substitute.For<IOwnerSettingsDocumentReader>();
        var store = Substitute.For<IStoredSecretStore>();
        var service = CreateService(owners, store, [MailFathomPermission.AdminRead]);
        Assert.True(SecretName.TryCreate("primary-password", out var name));
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");

        // Act & Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => service.StoreAsync(
                SyntheticMailOwner.Deployment,
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
    public async Task StoreAsync_AnUnknownOwner_IsRefusedBeforeTheStoreIsReached()
    {
        // Arrange
        var owners = Substitute.For<IOwnerSettingsDocumentReader>();
        var store = Substitute.For<IStoredSecretStore>();
        store.CanStore.Returns(true);
        var service = CreateService(owners, store);
        Assert.True(SecretName.TryCreate("primary-password", out var name));
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");

        // Act
        var result = await service.StoreAsync(
            SyntheticMailOwner.Deployment,
            name,
            material,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StoredSecretProvisioningOutcome.UnknownOwner, result.Outcome);
        await store.DidNotReceiveWithAnyArgs().StoreAsync(
            default!,
            default,
            default,
            default,
            default!,
            TestContext.Current.CancellationToken);
    }

    private static StoredSecretAdministration CreateService(
        IOwnerSettingsDocumentReader owners,
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
            owners,
            store,
            new OptimisticConcurrencyRetryPolicy(
                sessions,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                TimeProvider.System));
    }
}
