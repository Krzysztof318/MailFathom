// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Infrastructure.DataEncryption;

/// <summary>Seals and opens one stored value under the deployment's key ring.</summary>
/// <remarks>
/// <para>
/// This is the reusable half of storing something sealed: it owns key selection and the binding, and knows nothing about
/// what is being sealed or which table it lives in. A store is its consumer, and a second sealed column later needs a
/// purpose rather than anything here.
/// </para>
/// <para>
/// Sealing always uses the active key and opening always uses the key the value names, which together are the whole of
/// the rotation model. A value read under a retired key is reported as needing re-sealing rather than re-sealed here,
/// because writing is the store's decision and its transaction: an encryptor that wrote would be writing outside any
/// unit of work the caller opened.
/// </para>
/// <para>
/// No key, plaintext, or ciphertext is ever logged, and this type logs nothing at all — a failure carries the binding,
/// which names MailFathom's own account identifier and the purpose, and nothing else.
/// </para>
/// </remarks>
internal sealed class FieldEncryptor
{
    private readonly DataEncryptionKeyRing keyRing;

    /// <summary>Initializes an encryptor over the deployment's key ring.</summary>
    /// <param name="keyRing">Resolves the keys values are sealed and opened with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="keyRing" /> is <see langword="null" />.</exception>
    public FieldEncryptor(DataEncryptionKeyRing keyRing)
    {
        ArgumentNullException.ThrowIfNull(keyRing);

        this.keyRing = keyRing;
    }

    /// <summary>Seals a value under the active key.</summary>
    /// <param name="binding">What the value belongs to.</param>
    /// <param name="plaintext">The value to seal.</param>
    /// <param name="cancellationToken">Cancels resolving the key.</param>
    /// <returns>The sealed value and the identifier of the key that sealed it, both of which the store persists.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the active key cannot be resolved.</exception>
    public async Task<SealedValue> SealAsync(
        DataEncryptionBinding binding,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
    {
        using var key = await this.keyRing.ResolveActiveKeyAsync(cancellationToken);

        return key.Seal(binding, plaintext.Span);
    }

    /// <summary>Opens a stored value.</summary>
    /// <param name="binding">What the value belongs to, which must be exactly what it was sealed with.</param>
    /// <param name="sealedValue">The stored ciphertext and the key it names.</param>
    /// <param name="cancellationToken">Cancels resolving the key.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sealedValue" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the named key is no longer configured or cannot be resolved.</exception>
    /// <exception cref="CryptographicException">
    /// Thrown when the value does not open. A value moved between accounts or purposes, a row restored from another
    /// deployment, and an altered ciphertext are one outcome on purpose: distinguishing them would tell an attacker
    /// which part of a forgery was wrong.
    /// </exception>
    public async Task<byte[]> OpenAsync(
        DataEncryptionBinding binding,
        SealedValue sealedValue,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sealedValue);

        using var key = await this.keyRing.ResolveKeyAsync(sealedValue.KeyId, cancellationToken);

        return key.Open(binding, sealedValue.Ciphertext.Span);
    }

    /// <summary>Gets whether a stored value was sealed under a key that is no longer the active one.</summary>
    /// <param name="sealedValue">The stored value.</param>
    /// <param name="activeKeyId">The identifier of the key new values are sealed under.</param>
    /// <returns><see langword="true" /> when the next write should re-seal the value.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// This is what makes a rotation progress without a flag day, and also what makes it incomplete on its own: a value
    /// nothing writes again keeps its original key indefinitely, so retiring that key needs the deliberate re-sealing
    /// pass tracked separately.
    /// </remarks>
    public static bool NeedsResealing(SealedValue sealedValue, string activeKeyId)
    {
        ArgumentNullException.ThrowIfNull(sealedValue);
        ArgumentNullException.ThrowIfNull(activeKeyId);

        return !string.Equals(sealedValue.KeyId, activeKeyId, StringComparison.Ordinal);
    }
}
