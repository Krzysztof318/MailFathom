// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Domain.Accounts;

/// <summary>The long-lived credential MailFathom holds on one mailbox owner's behalf, owned by the operation that read it.</summary>
/// <remarks>
/// <para>
/// The token acts for a named person, so it is personal data by this repository's own classification: it is never
/// logged, never rendered by a synthesized <see cref="object.ToString" />, and never held longer than the operation that
/// needs it. The material lives in a pinned buffer that <see cref="Dispose" /> erases, for the reason a resolved secret
/// does — a <see cref="string" /> is immutable, unpinned, and copied again on every compaction, so erasing one is not
/// well defined.
/// </para>
/// <para>
/// This is a domain value rather than the infrastructure's resolved-secret type, and the difference is what each one
/// means rather than how it is held. A resolved secret is material an operator provisioned behind a reference; this is a
/// credential MailFathom itself stores, rotates, and re-seals under its own key. The two meet only where the adapter
/// seeds one from the other, which is exactly where the seeding path belongs.
/// </para>
/// </remarks>
public sealed class MailboxRefreshToken : IDisposable
{
    private readonly byte[] material;
    private bool disposed;

    private MailboxRefreshToken(byte[] material) => this.material = material;

    /// <summary>Takes ownership of a copy of the token's bytes.</summary>
    /// <param name="material">The token, which is left untouched.</param>
    /// <returns>The owned token.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="material" /> is empty, which no authorization server issues and which would seal to a value nothing could use.</exception>
    public static MailboxRefreshToken Create(ReadOnlySpan<byte> material)
    {
        if (material.IsEmpty)
        {
            throw new ArgumentException("A mailbox refresh token cannot be empty.", nameof(material));
        }

        var buffer = GC.AllocateArray<byte>(material.Length, pinned: true);
        material.CopyTo(buffer);

        return new MailboxRefreshToken(buffer);
    }

    /// <summary>Encodes a token the authorization server issued as text.</summary>
    /// <param name="material">The token as it arrived in the token response.</param>
    /// <returns>The owned token.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="material" /> is empty or blank.</exception>
    /// <remarks>The source string cannot be erased, so this overload is reachable only where the value already arrived as one — a JSON response body is the case that exists.</remarks>
    public static MailboxRefreshToken FromText(string material)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(material);

        var buffer = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(material), pinned: true);
        Encoding.UTF8.GetBytes(material, buffer);

        return new MailboxRefreshToken(buffer);
    }

    /// <summary>Reveals the token's bytes, which is what a store seals.</summary>
    /// <returns>The material.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the material has already been erased.</exception>
    public ReadOnlySpan<byte> RevealBytes()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.material;
    }

    /// <summary>Reveals the token as its UTF-8 text view.</summary>
    /// <returns>The decoded token.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the material has already been erased.</exception>
    /// <remarks>
    /// The returned string cannot be erased and persists until the collector reclaims it, so this accessor exists for the
    /// one contract that takes a <see cref="string" />: the form field of the token request. Call it there and nowhere
    /// else, and never store, log, or pass on the result.
    /// </remarks>
    public string RevealAsString()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return Encoding.UTF8.GetString(this.material);
    }

    /// <inheritdoc />
    /// <remarks>Erasure is idempotent, and every accessor throws afterwards rather than returning empty material, because a use after erasure is a defect.</remarks>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (this.disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(this.material);
        this.disposed = true;
    }

    /// <inheritdoc />
    /// <remarks>Redacted by construction, so a record or a log template that meets this value cannot print the token.</remarks>
    public override string ToString() => "***";

    /// <summary>Gets whether the owned buffer holds nothing but zeroes.</summary>
    /// <remarks>Exposed to unit tests so the erasure guarantee is asserted directly rather than inferred from the accessors throwing.</remarks>
    internal bool IsMaterialErased => !this.material.AsSpan().ContainsAnyExcept((byte)0);
}
