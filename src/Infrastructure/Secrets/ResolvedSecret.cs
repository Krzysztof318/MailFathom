// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Security.Cryptography;
using System.Text;

namespace MailMcp.Infrastructure.Secrets;

/// <summary>Secret material owned by the operation that resolved it and erased when that operation ends.</summary>
/// <remarks>
/// <para>
/// Material is held in a byte buffer rather than in a <see cref="string" />: a string is immutable, cannot be scheduled
/// for deletion, and — because its memory is not pinned — is copied again whenever the garbage collector compacts, so
/// erasing one is not well defined. The buffer is allocated pinned so the collector cannot relocate it and leave an
/// un-erased copy behind, and it is erased with <see cref="CryptographicOperations.ZeroMemory" />, which is specified to
/// survive runtime optimizations that could drop a write no read follows. Pooled buffers are never used, because a
/// buffer returned uncleared hands the material to the next unrelated caller. <c>SecureString</c> is deliberately not
/// used: Microsoft recommends against it for new development and it does not encrypt its storage on non-Windows
/// platforms, which is every environment MailMcp targets.
/// </para>
/// <para>
/// The instance is owned by whoever resolved it. Dispose it as soon as the operation that needed the material finishes,
/// so the window in which a process dump could contain the secret is bounded by an operation rather than by uptime.
/// </para>
/// </remarks>
public sealed class ResolvedSecret : IDisposable
{
    private readonly byte[] material;
    private bool disposed;

    private ResolvedSecret(byte[] material) => this.material = material;

    /// <summary>Copies binary material into an owned pinned buffer.</summary>
    /// <param name="material">The material, which is left untouched.</param>
    /// <returns>The owned secret.</returns>
    public static ResolvedSecret FromBytes(ReadOnlySpan<byte> material)
    {
        var buffer = GC.AllocateArray<byte>(material.Length, pinned: true);
        material.CopyTo(buffer);

        return new ResolvedSecret(buffer);
    }

    /// <summary>Encodes text material as UTF-8 into an owned pinned buffer.</summary>
    /// <param name="material">The material.</param>
    /// <returns>The owned secret.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="material" /> is <see langword="null" />.</exception>
    /// <remarks>The source string itself cannot be erased, so this overload is reachable only where the platform already handed the value over as a string.</remarks>
    public static ResolvedSecret FromText(string material)
    {
        ArgumentNullException.ThrowIfNull(material);

        var buffer = GC.AllocateArray<byte>(Encoding.UTF8.GetByteCount(material), pinned: true);
        Encoding.UTF8.GetBytes(material, buffer);

        return new ResolvedSecret(buffer);
    }

    /// <summary>Reveals the material unchanged.</summary>
    /// <returns>The material, including any trailing newline the source carried.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the material has already been erased.</exception>
    /// <remarks>This is the primary accessor. A PKCS#12 bundle or DER-encoded certificate survives it byte for byte.</remarks>
    public ReadOnlySpan<byte> RevealBytes()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return this.material;
    }

    /// <summary>Reveals the material as its UTF-8 text view, with one trailing newline removed.</summary>
    /// <returns>The decoded text.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the material has already been erased.</exception>
    /// <remarks>
    /// The returned string cannot be erased and persists until the collector reclaims it, so this accessor exists only
    /// for framework contracts that take a <see cref="string" /> — the IMAP client's authentication call and the
    /// PostgreSQL connection-string password. Call it at that boundary, as late as possible, and never store, log, or
    /// pass on the result. The newline trim belongs to the text view alone: <c>LoadCredential=</c>, Compose secrets, and
    /// Kubernetes Secret files routinely end with one, and an untrimmed byte presents as a wrong password.
    /// </remarks>
    public string RevealAsString()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        return Encoding.UTF8.GetString(WithoutOneTrailingNewline(this.material));
    }

    /// <summary>Gets the number of characters <see cref="RevealTextInto" /> writes.</summary>
    /// <exception cref="ObjectDisposedException">Thrown when the material has already been erased.</exception>
    public int TextLength
    {
        get
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            return Encoding.UTF8.GetCharCount(WithoutOneTrailingNewline(this.material));
        }
    }

    /// <summary>Decodes the material as UTF-8 text into a caller-owned buffer, with one trailing newline removed.</summary>
    /// <param name="destination">A buffer of at least <see cref="TextLength" /> characters.</param>
    /// <exception cref="ObjectDisposedException">Thrown when the material has already been erased.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="destination" /> is too small.</exception>
    /// <remarks>
    /// This is the erasable counterpart to <see cref="RevealAsString" />, for a framework contract that accepts a
    /// <see cref="ReadOnlySpan{T}" /> of characters rather than a <see cref="string" /> — loading a password-protected
    /// PKCS#12 bundle is the case that exists today. The caller owns the buffer and must erase it, which is what keeps
    /// a secret out of an un-erasable string wherever the platform makes that avoidable.
    /// </remarks>
    public void RevealTextInto(Span<char> destination)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        Encoding.UTF8.GetChars(WithoutOneTrailingNewline(this.material), destination);
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
    /// <remarks>Redacted by construction, so a record that carries this value cannot print the material through its synthesized printing.</remarks>
    public override string ToString() => "***";

    /// <summary>Gets whether the owned buffer holds nothing but zeroes.</summary>
    /// <remarks>Exposed to unit tests so the erasure guarantee is asserted directly rather than inferred from the accessors throwing.</remarks>
    internal bool IsMaterialErased => !this.material.AsSpan().ContainsAnyExcept((byte)0);

    private static ReadOnlySpan<byte> WithoutOneTrailingNewline(ReadOnlySpan<byte> material)
    {
        if (material.EndsWith("\r\n"u8))
        {
            return material[..^2];
        }

        return material.EndsWith("\n"u8) ? material[..^1] : material;
    }
}
