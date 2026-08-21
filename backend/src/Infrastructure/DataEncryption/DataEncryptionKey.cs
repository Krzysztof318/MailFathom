// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Common;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.Infrastructure.DataEncryption;

/// <summary>Why configured key material could not be turned into a key.</summary>
/// <remarks>It is the whole permitted vocabulary for reporting a bad key: neither the material nor its length reaches an operator's console, because either would narrow what an attacker reading the log has to guess.</remarks>
public enum DataEncryptionKeyMaterialFailure
{
    /// <summary>The material is not base64.</summary>
    NotBase64 = 0,

    /// <summary>The material is base64 but does not decode to the length an AES-256 key takes.</summary>
    WrongLength = 1,
}

/// <summary>An AES-256 key of the ring, owned by the operation that resolved it and erased when that operation ends.</summary>
/// <remarks>
/// <para>
/// The key exists as decoded bytes for the duration of one seal or open, and the identifier travels with it because the
/// two are used together: the identifier is stored beside the sealed value and is authenticated into it, so handing them
/// around separately would make it possible to seal under one key and record another.
/// </para>
/// <para>
/// Configuration carries the material as base64 rather than raw bytes because every channel that delivers it — a Compose
/// secret file, a Kubernetes <c>Secret</c> value, a systemd credential — is handled as text by the tools that write it,
/// and a raw 32-byte file acquires a trailing newline the first time anyone edits it. Decoding happens here, once, and
/// every intermediate buffer is erased rather than left for the collector.
/// </para>
/// </remarks>
public sealed class DataEncryptionKey : IDisposable
{
    private readonly ResolvedSecret material;

    private DataEncryptionKey(string keyId, ResolvedSecret material)
    {
        this.KeyId = keyId;
        this.material = material;
    }

    /// <summary>Gets the identifier stored beside every value this key seals.</summary>
    public string KeyId { get; }

    /// <summary>Decodes configured material into a key.</summary>
    /// <param name="keyId">The identifier the material is configured under.</param>
    /// <param name="material">The resolved material, which stays owned by the caller and is not disposed here.</param>
    /// <param name="failure">Why the material is unusable when it is; otherwise <see langword="null" />.</param>
    /// <returns>The decoded key, which the caller owns and must dispose, or <see langword="null" /> when the material is unusable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The decode buffer holds one byte more than a key, so material that is base64 but too long fails to fit rather
    /// than being silently truncated to the right length. Startup calls this and disposes the result, which is what
    /// turns a mistyped key into a refusal to start rather than into a failure at the first stored read.
    /// </remarks>
    public static DataEncryptionKey? Decode(
        string keyId,
        ResolvedSecret material,
        out DataEncryptionKeyMaterialFailure? failure)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        ArgumentNullException.ThrowIfNull(material);

        failure = null;

        // Both buffers hold the key, so both are pinned: an unpinned buffer can be relocated by the collector while it
        // holds live material, which leaves a copy behind that the erasure below never reaches. This is the same
        // allocation ResolvedSecret and every other reader of revealed material in this system uses, for that reason.
        var encoded = GC.AllocateArray<char>(material.TextLength, pinned: true);
        var decoded = GC.AllocateArray<byte>(AesGcmEnvelope.KeySizeInBytes + 1, pinned: true);

        try
        {
            material.RevealTextInto(encoded);

            if (!Convert.TryFromBase64Chars(encoded, decoded, out var decodedLength))
            {
                failure = DataEncryptionKeyMaterialFailure.NotBase64;

                return null;
            }

            if (decodedLength != AesGcmEnvelope.KeySizeInBytes)
            {
                failure = DataEncryptionKeyMaterialFailure.WrongLength;

                return null;
            }

            return new DataEncryptionKey(keyId, ResolvedSecret.FromBytes(decoded.AsSpan(0, decodedLength)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decoded);
            encoded.AsSpan().Clear();
        }
    }

    /// <summary>Seals a value bound to this key.</summary>
    /// <param name="binding">What the value belongs to.</param>
    /// <param name="plaintext">The value to seal.</param>
    /// <returns>The sealed value, naming this key.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the key has already been erased.</exception>
    internal SealedValue Seal(DataEncryptionBinding binding, ReadOnlySpan<byte> plaintext) =>
        new(this.KeyId, AesGcmEnvelope.Seal(this.material.RevealBytes(), plaintext, binding.ComposeAssociatedData(this.KeyId)));

    /// <summary>Derives a key for a purpose that signs rather than seals.</summary>
    /// <param name="purpose">What the derived key is for, which is bound in as the derivation's info label.</param>
    /// <param name="destination">Where the derived key is written, whose length decides how much material is produced.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the key has already been erased.</exception>
    /// <remarks>
    /// HKDF rather than the ring key itself, so the material that signs is never the material that seals: an attacker
    /// who obtained one subkey learns nothing about the other or about the key both came from. The derivation is
    /// deterministic and takes no salt, which is what lets a signature produced by one process be verified by another
    /// holding the same ring — the domain separation comes from the purpose alone, exactly as it does for a sealed value.
    /// <para>
    /// The caller owns the destination and is responsible for erasing it, the same way this type erases its own material
    /// on disposal.
    /// </para>
    /// </remarks>
    internal void DeriveKeyFor(DataEncryptionPurpose purpose, Span<byte> destination) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            this.material.RevealBytes(),
            destination,
            salt: default,
            info: Encoding.UTF8.GetBytes(purpose.Identity));

    /// <summary>Opens a value sealed under this key.</summary>
    /// <param name="binding">What the value belongs to.</param>
    /// <param name="sealedValue">The sealed value.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the key has already been erased.</exception>
    /// <exception cref="CryptographicException">Thrown when the value was sealed under another key or bound to something else.</exception>
    internal byte[] Open(DataEncryptionBinding binding, ReadOnlySpan<byte> sealedValue) =>
        AesGcmEnvelope.Open(this.material.RevealBytes(), sealedValue, binding.ComposeAssociatedData(this.KeyId));

    /// <inheritdoc />
    /// <remarks>Erases the decoded key. Call it as soon as the operation that needed it finishes, so the window in which a process dump could contain the key is bounded by an operation rather than by uptime.</remarks>
    public void Dispose() => this.material.Dispose();
}
