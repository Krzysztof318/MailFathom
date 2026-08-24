// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Says where one raw MIME payload was put, and what was measured over it while it was being put there.</summary>
/// <remarks>
/// <para>
/// This is what a placement hands back and what a write then records, and it exists because the two happen at different
/// moments. The object write has to run with no database transaction open across it, and every caller has a transaction
/// open by the time it reaches the port — so the payload is placed before the caller opens its unit of work, and this
/// value is what survives the gap between the two.
/// </para>
/// <para>
/// A caller passes it along and reads nothing out of it. Which backend answered is not a use case's business, which is
/// the port's whole promise, and the four properties here are for the adapter that stages the row.
/// </para>
/// <para>
/// The payload is mail content and personal data by default. Nothing here reaches a log, and neither the length nor the
/// digest is a permissible stand-in for naming a message in one.
/// </para>
/// </remarks>
public sealed record PlacedEmailContent
{
    private PlacedEmailContent(
        ContentStorageBackend backend,
        string? objectLocator,
        ReadOnlyMemory<byte> rawMime,
        long byteLength,
        ReadOnlyMemory<byte> sha256Hash)
    {
        this.Backend = backend;
        this.ObjectLocator = objectLocator;
        this.RawMime = rawMime;
        this.ByteLength = byteLength;
        this.Sha256Hash = sha256Hash;
    }

    /// <summary>Gets which store holds the payload.</summary>
    public ContentStorageBackend Backend { get; }

    /// <summary>Gets the whole key the object was written under, or <see langword="null" /> when the database holds the payload.</summary>
    /// <remarks>
    /// The row stores this exactly as it is. Nothing recomputes a key from the identity of the owning row, because
    /// nothing can: the key was minted before that row existed, which is what let the object be written outside the
    /// caller's transaction.
    /// </remarks>
    public string? ObjectLocator { get; }

    /// <summary>Gets the bytes the row itself must carry, which is empty when the object backend already holds them.</summary>
    public ReadOnlyMemory<byte> RawMime { get; }

    /// <summary>Gets how many bytes were placed.</summary>
    public long ByteLength { get; }

    /// <summary>Gets the SHA-256 digest computed over the placed bytes.</summary>
    /// <remarks>
    /// Under the object backend this is also what the endpoint was asked to verify the upload against, so a row
    /// carrying it describes an object the endpoint agreed it had received intact rather than one this process merely
    /// believes it sent.
    /// </remarks>
    public ReadOnlyMemory<byte> Sha256Hash { get; }

    /// <summary>Places a payload in the database, which stores nothing until the caller's unit of work commits.</summary>
    /// <param name="rawMime">The raw RFC 822 bytes.</param>
    /// <returns>The placement, carrying the bytes for the row to hold.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="rawMime" /> is empty.</exception>
    /// <remarks>
    /// It reaches no store, which is what keeps the database backend exactly as fast and exactly as atomic as it was
    /// before a second backend existed: everything it does still happens inside the caller's transaction.
    /// </remarks>
    public static PlacedEmailContent InDatabase(ReadOnlyMemory<byte> rawMime)
    {
        if (rawMime.IsEmpty)
        {
            throw new ArgumentException("Raw MIME content to place cannot be empty.", nameof(rawMime));
        }

        return new PlacedEmailContent(
            ContentStorageBackend.Database,
            objectLocator: null,
            rawMime,
            rawMime.Length,
            SHA256.HashData(rawMime.Span));
    }

    /// <summary>Records that a payload was written to the object backend under one key.</summary>
    /// <param name="objectLocator">The whole key the object was written under.</param>
    /// <param name="byteLength">How many bytes were written.</param>
    /// <param name="sha256Hash">The digest computed over them and sent with the request.</param>
    /// <returns>The placement, carrying no bytes because the object holds them.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="objectLocator" /> is blank or <paramref name="sha256Hash" /> is not a SHA-256 digest.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="byteLength" /> is not positive.</exception>
    public static PlacedEmailContent InObjectStorage(
        string objectLocator,
        long byteLength,
        ReadOnlyMemory<byte> sha256Hash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectLocator);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(byteLength, 0);

        if (sha256Hash.Length != SHA256.HashSizeInBytes)
        {
            throw new ArgumentException(
                $"A placed payload's digest is {SHA256.HashSizeInBytes} bytes of SHA-256.",
                nameof(sha256Hash));
        }

        return new PlacedEmailContent(
            ContentStorageBackend.ObjectStorage,
            objectLocator,
            rawMime: ReadOnlyMemory<byte>.Empty,
            byteLength,
            sha256Hash);
    }
}
