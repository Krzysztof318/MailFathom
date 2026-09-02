// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Common;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.DataEncryption;

/// <summary>Covers what the key ring guarantees about a value it sealed: who can open it, and under which key.</summary>
/// <remarks>
/// Every assertion here is about a value refusing to open somewhere it does not belong, because that refusal is the
/// whole reason one key ring can protect several kinds of value. A test that only proved a round trip would pass
/// against an implementation that authenticated nothing.
/// </remarks>
public sealed class FieldEncryptorTests
{
    private const string ActiveKeyId = "2026-08";
    private const string RetiredKeyId = "2026-02";

    private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("1//0eXAMPLErefreshtoken");

    [Fact]
    public async Task SealAsync_AValue_SealsItUnderTheActiveKey()
    {
        // Arrange
        var encryptor = CreateEncryptor(out _);
        var binding = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");

        // Act
        var sealedValue = await encryptor.SealAsync(binding, Plaintext, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ActiveKeyId, sealedValue.KeyId);
        Assert.NotEqual(Plaintext, sealedValue.Ciphertext.ToArray());
    }

    [Fact]
    public async Task OpenAsync_AValueSealedUnderTheSameBinding_YieldsTheValue()
    {
        // Arrange
        var encryptor = CreateEncryptor(out _);
        var binding = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");
        var sealedValue = await encryptor.SealAsync(binding, Plaintext, TestContext.Current.CancellationToken);

        // Act
        var opened = await encryptor.OpenAsync(binding, sealedValue, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Plaintext, opened);
    }

    [Fact]
    public async Task OpenAsync_AValueMovedToAnotherSubject_DoesNotOpen()
    {
        // Arrange — the case an operator would meet as a row copied between accounts, and an attacker as a row planted
        // under an account they control.
        var encryptor = CreateEncryptor(out _);
        var sealedValue = await encryptor.SealAsync(
            DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary"),
            Plaintext,
            TestContext.Current.CancellationToken);

        // Act
        var opening = async () => await encryptor.OpenAsync(
            DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "secondary"),
            sealedValue,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<CryptographicException>(opening);
    }

    [Fact]
    public async Task OpenAsync_AValueWhoseStoredKeyIdentifierWasAltered_DoesNotOpen()
    {
        // Arrange — the identifier beside the ciphertext is not a secret, so a database writer can change it. It is
        // authenticated into the value, which is what makes the change a failure rather than a redirection.
        var encryptor = CreateEncryptor(out _);
        var binding = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");
        var sealedValue = await encryptor.SealAsync(binding, Plaintext, TestContext.Current.CancellationToken);

        // Act
        var opening = async () => await encryptor.OpenAsync(
            binding,
            sealedValue with { KeyId = RetiredKeyId },
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAnyAsync<CryptographicException>(opening);
    }

    [Fact]
    public async Task OpenAsync_AValueNamingAKeyTheRingNoLongerConfigures_FailsNamingTheKey()
    {
        // Arrange — what retiring a key too early looks like from the reader's side.
        var encryptor = CreateEncryptor(out _);
        var binding = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");
        var sealedValue = new SealedValue("2025-01", new byte[64]);

        // Act
        var opening = async () => await encryptor.OpenAsync(binding, sealedValue, TestContext.Current.CancellationToken);

        // Assert
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(opening);
        Assert.Contains("2025-01", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_AValueStillUnderARetiredKey_OpensAndReportsThatItNeedsResealing()
    {
        // Arrange — the state a deployment is in between moving the active key and re-sealing what nothing has
        // rewritten since. Both halves have to hold: the value opens, and the store can tell it is behind.
        var encryptor = CreateEncryptor(out var resolver);
        var binding = DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary");

        var retiredRing = new DataEncryptionKeyRing(
            () => CreateSettings(RetiredKeyId),
            resolver);
        var sealedUnderRetiredKey = await new FieldEncryptor(retiredRing)
            .SealAsync(binding, Plaintext, TestContext.Current.CancellationToken);

        // Act
        var opened = await encryptor.OpenAsync(binding, sealedUnderRetiredKey, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Plaintext, opened);
        Assert.True(FieldEncryptor.NeedsResealing(sealedUnderRetiredKey, ActiveKeyId));
    }

    [Fact]
    public async Task NeedsResealing_AValueUnderTheActiveKey_ReportsNothingToDo()
    {
        // Arrange
        var encryptor = CreateEncryptor(out _);
        var sealedValue = await encryptor.SealAsync(
            DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary"),
            Plaintext,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(FieldEncryptor.NeedsResealing(sealedValue, ActiveKeyId));
    }

    [Fact]
    public async Task SealAsync_EveryResolvedKey_IsErasedWhenTheOperationEnds()
    {
        // Arrange
        var encryptor = CreateEncryptor(out var resolver);

        // Act
        await encryptor.SealAsync(
            DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, "primary"),
            Plaintext,
            TestContext.Current.CancellationToken);

        // Assert — the resolver hands out the configured base64, and the ring owns both that and the decoded key. What
        // is asserted is that nothing it issued survives the operation holding material.
        Assert.NotEmpty(resolver.IssuedMaterial);
        Assert.All(resolver.IssuedMaterial, material => Assert.Throws<ObjectDisposedException>(() => material.RevealBytes().Length));
    }

    private static FieldEncryptor CreateEncryptor(out ProvisionedMaterialResolver resolver)
    {
        resolver = CreateResolver();

        return new FieldEncryptor(new DataEncryptionKeyRing(() => CreateSettings(ActiveKeyId), resolver));
    }

    private static ProvisionedMaterialResolver CreateResolver()
    {
        var resolver = new ProvisionedMaterialResolver();

        // A trailing newline is what a Compose secret, a Kubernetes Secret file, and `LoadCredential=` all routinely
        // carry, so the material is provisioned with one rather than in the tidy form nothing produces.
        resolver.ProvisionText("plaintext:active", Convert.ToBase64String(KeyOf(0x11)) + "\n");
        resolver.ProvisionText("plaintext:retired", Convert.ToBase64String(KeyOf(0x22)));

        return resolver;
    }

    private static DataEncryptionKeyRingSettings CreateSettings(string activeKeyId) =>
        new(
            activeKeyId,
            [
                new DataEncryptionKeyReference(ActiveKeyId, Reference("active")),
                new DataEncryptionKeyReference(RetiredKeyId, Reference("retired")),
            ]);

    private static ConfiguredSecret Reference(string target) =>
        new() { Name = $"data-key-{target}", SecretReference = $"plaintext:{target}" };

    private static byte[] KeyOf(byte fill) => [.. Enumerable.Repeat(fill, AesGcmEnvelope.KeySizeInBytes)];
}
