// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Common;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Secrets.Database;

public sealed class StoredSecretBindingTests
{
    private const string ActiveKeyId = "2026-08";

    private static readonly MailOwnerId Owner = MailOwnerId.Create(new Guid("7a7ff3f5-29d8-4f4a-a101-e8e59f0fe37d"));
    private static readonly MailOwnerId OtherOwner = MailOwnerId.Create(new Guid("1ef57a8c-8af9-4efe-b8e8-f863149eaf45"));
    private static readonly DatabaseSecretReference Reference =
        DatabaseSecretReference.Create(new Guid("019925df-96f4-7c6d-8f91-b9f6cf27f5b2"));
    private static readonly SecretName Name = SecretNamed("primary-password");
    private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("not-a-real-mailbox-password");

    [Fact]
    public async Task OpenAsync_AStoredSecretUnderItsMatchingBinding_ReturnsTheMaterial()
    {
        // Arrange
        var encryptor = CreateEncryptor();
        var binding = StoredSecretBinding.Create(Owner, Reference, Name);
        var sealedValue = await encryptor.SealAsync(
            binding,
            Plaintext,
            TestContext.Current.CancellationToken);

        // Act
        var opened = await encryptor.OpenAsync(binding, sealedValue, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Plaintext, opened);
        CryptographicOperations.ZeroMemory(opened);
    }

    [Fact]
    public async Task OpenAsync_AStoredSecretMovedToAnotherOwner_DoesNotOpen()
    {
        // Arrange
        var encryptor = CreateEncryptor();
        var sealedValue = await encryptor.SealAsync(
            StoredSecretBinding.Create(Owner, Reference, Name),
            Plaintext,
            TestContext.Current.CancellationToken);

        // Act
        var opening = async () => await encryptor.OpenAsync(
            StoredSecretBinding.Create(OtherOwner, Reference, Name),
            sealedValue,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<CryptographicException>(opening);
    }

    [Fact]
    public async Task OpenAsync_AStoredSecretPresentedForAnotherPurpose_DoesNotOpen()
    {
        // Arrange
        var encryptor = CreateEncryptor();
        var binding = StoredSecretBinding.Create(Owner, Reference, Name);
        var sealedValue = await encryptor.SealAsync(
            binding,
            Plaintext,
            TestContext.Current.CancellationToken);

        // Act
        var opening = async () => await encryptor.OpenAsync(
            DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, binding.Subject),
            sealedValue,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<CryptographicException>(opening);
    }

    private static FieldEncryptor CreateEncryptor()
    {
        var resolver = new ProvisionedMaterialResolver();
        resolver.ProvisionText(
            "plaintext:active",
            Convert.ToBase64String(Enumerable.Repeat((byte)0x11, AesGcmEnvelope.KeySizeInBytes).ToArray()));

        var settings = new DataEncryptionKeyRingSettings(
            ActiveKeyId,
            [new DataEncryptionKeyReference(
                ActiveKeyId,
                new ConfiguredSecret { Name = "data-key", SecretReference = "plaintext:active" })]);

        return new FieldEncryptor(new DataEncryptionKeyRing(() => settings, resolver));
    }

    private static SecretName SecretNamed(string value) =>
        SecretName.TryCreate(value, out var name)
            ? name
            : throw new InvalidOperationException("The test secret name is malformed.");
}
