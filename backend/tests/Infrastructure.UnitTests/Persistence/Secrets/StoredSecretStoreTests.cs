// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Secrets;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Secrets;

public sealed class StoredSecretStoreTests
{
    [Fact]
    public async Task StoreAsync_ADeploymentWithNoKeyRing_RefusesBeforeJoiningTheSession()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var ring = new DataEncryptionKeyRing(
            () => new DataEncryptionKeyRingSettings(string.Empty, []),
            new ProvisionedMaterialResolver());
        var store = new StoredSecretStore(context, new FieldEncryptor(ring), ring, TimeProvider.System);
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
}
