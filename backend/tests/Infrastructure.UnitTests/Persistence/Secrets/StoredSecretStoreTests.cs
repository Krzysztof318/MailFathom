// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Persistence;
using MailFathom.Common;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Secrets;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Secrets;

public sealed class StoredSecretStoreTests
{
    private const string ActiveKeyId = "2026-08";

    private static readonly MailOwnerId Owner =
        MailOwnerId.Create(new Guid("7a7ff3f5-29d8-4f4a-a101-e8e59f0fe37d"));

    private static readonly MailOwnerId OtherOwner =
        MailOwnerId.Create(new Guid("1ef57a8c-8af9-4efe-b8e8-f863149eaf45"));

    [Fact]
    public async Task StoreAsync_ADeploymentWithNoKeyRing_RefusesBeforeJoiningTheSession()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var ring = new DataEncryptionKeyRing(
            () => new DataEncryptionKeyRingSettings(string.Empty, []),
            new ProvisionedMaterialResolver());
        var store = new StoredSecretStore(context, ring, TimeProvider.System);
        var session = Substitute.For<IPersistenceSession>();
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");
        Assert.True(SecretName.TryCreate("primary-password", out var name));

        // Act
        var storing = async () => await store.StoreAsync(
            session,
            DatabaseSecretReference.Create(new Guid("019925df-96f4-7c6d-8f91-b9f6cf27f5b2")),
            MailOwnerId.Create(new Guid("7a7ff3f5-29d8-4f4a-a101-e8e59f0fe37d")),
            name,
            material,
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(storing);
        Assert.Contains("DataEncryption", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreAsync_TheOwnerAndNameAlreadyPending_RotatesTheSameReference()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var existingId = new Guid("019925df-96f4-7c6d-8f91-b9f6cf27f5b2");
        var existing = Stored(existingId);
        context.StoredSecrets.Add(existing);
        var (store, encryptor) = CreateStore(context);
        await using var session = new TestPersistenceSession(context);
        using var material = ResolvedSecret.FromText("the-rotated-material");
        Assert.True(SecretName.TryCreate(existing.Name, out var name));

        // Act
        var reference = await store.StoreAsync(
            session,
            DatabaseSecretReference.Create(new Guid("c5902058-d701-4ba3-99b5-88d9d2bb1d86")),
            Owner,
            name,
            material,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DatabaseSecretReference.Create(existingId), reference);
        Assert.Equal(ActiveKeyId, existing.DataEncryptionKeyId);
        var opened = await encryptor.OpenAsync(
            StoredSecretBinding.Create(Owner, reference, name),
            new SealedValue(existing.DataEncryptionKeyId, existing.SealedMaterial),
            TestContext.Current.CancellationToken);
        Assert.Equal("the-rotated-material", Encoding.UTF8.GetString(opened));
        CryptographicOperations.ZeroMemory(opened);
    }

    [Fact]
    public async Task StoreAsync_AConfiguredKey_IsResolvedBeforeTheSessionIsJoined()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        await using var session = new TestPersistenceSession(context);
        var reference = DatabaseSecretReference.Create(new Guid("019925df-96f4-7c6d-8f91-b9f6cf27f5b2"));
        context.StoredSecrets.Add(Stored(reference.Id));
        var resolver = Substitute.For<ISecretReferenceResolver>();
        resolver.ResolveAsync("plaintext:active", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Assert.False(session.Joined);
            return SecretResolutionResult.Resolved(
                ResolvedSecret.FromText(
                    Convert.ToBase64String(Enumerable.Repeat((byte)0x11, AesGcmEnvelope.KeySizeInBytes).ToArray())),
                SecretMaterialSource.InlineValue);
        });
        var ring = new DataEncryptionKeyRing(
            () => new DataEncryptionKeyRingSettings(
                ActiveKeyId,
                [new DataEncryptionKeyReference(
                    ActiveKeyId,
                    new() { Name = "data-key", SecretReference = "plaintext:active" })]),
            resolver);
        var store = new StoredSecretStore(context, ring, TimeProvider.System);
        using var material = ResolvedSecret.FromText("not-a-real-mailbox-password");
        Assert.True(SecretName.TryCreate("primary-password", out var name));

        // Act
        await store.StoreAsync(
            session,
            reference,
            Owner,
            name,
            material,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.Joined);
    }

    [Fact]
    public async Task RemoveAsync_ASecretPendingInTheSession_StagesItsRemoval()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var reference = DatabaseSecretReference.Create(new Guid("019925df-96f4-7c6d-8f91-b9f6cf27f5b2"));
        var existing = Stored(reference.Id);
        context.StoredSecrets.Add(existing);
        var (store, _) = CreateStore(context);
        await using var session = new TestPersistenceSession(context);

        // Act
        var removed = await store.RemoveAsync(
            session,
            reference,
            Owner,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(removed);
        Assert.DoesNotContain(existing, context.StoredSecrets.Local);
    }

    [Fact]
    public async Task RemoveAsync_AReferenceOwnedByAnotherOwner_LeavesTheRowPending()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var reference = DatabaseSecretReference.Create(new Guid("019925df-96f4-7c6d-8f91-b9f6cf27f5b2"));
        var existing = Stored(reference.Id);
        context.StoredSecrets.Add(existing);
        var (store, _) = CreateStore(context);
        await using var session = new TestPersistenceSession(context);

        // Act
        var removed = await store.RemoveAsync(
            session,
            reference,
            OtherOwner,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(removed);
        Assert.Contains(existing, context.StoredSecrets.Local);
    }

    private static (StoredSecretStore Store, FieldEncryptor Encryptor) CreateStore(MailFathomDbContext context)
    {
        var resolver = new ProvisionedMaterialResolver();
        resolver.ProvisionText(
            "plaintext:active",
            Convert.ToBase64String(Enumerable.Repeat((byte)0x11, AesGcmEnvelope.KeySizeInBytes).ToArray()));
        var ring = new DataEncryptionKeyRing(
            () => new DataEncryptionKeyRingSettings(
                ActiveKeyId,
                [new DataEncryptionKeyReference(
                    ActiveKeyId,
                    new() { Name = "data-key", SecretReference = "plaintext:active" })]),
            resolver);

        var encryptor = new FieldEncryptor(ring);
        return (new StoredSecretStore(context, ring, TimeProvider.System), encryptor);
    }

    private static StoredSecretEntity Stored(Guid id) => new()
    {
        Id = id,
        OwnerId = Owner.Value,
        Name = "primary-password",
        SealedMaterial = new byte[StoredSecretEntity.MinimumSealedMaterialByteCount],
        DataEncryptionKeyId = "old-key",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class TestPersistenceSession(MailFathomDbContext context)
        : IPersistenceSession, IEfCorePersistenceSession
    {
        internal bool Joined { get; private set; }

        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<MailFathomDbContext> JoinAsync(CancellationToken cancellationToken)
        {
            this.Joined = true;
            return Task.FromResult(context);
        }

        public void MeasureOnEnding(ISessionScopedMeasurement measurement)
        {
        }

        public void ReleaseOnCommit(IReadOnlyCollection<string> objectLocators)
        {
        }
    }
}
