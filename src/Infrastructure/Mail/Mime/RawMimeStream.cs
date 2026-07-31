// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Opens stored raw MIME for reading without copying it.</summary>
/// <remarks>
/// Every path that reads a message opens it twice — once to check its structure and once to parse it — and each of
/// those passes would otherwise duplicate a payload the size limit exists to bound. Reading over the buffer in place
/// keeps the message in memory once no matter how many passes it takes.
/// </remarks>
internal static class RawMimeStream
{
    /// <summary>Opens a read-only stream over the raw MIME.</summary>
    /// <param name="rawMime">The stored bytes.</param>
    /// <returns>A seekable read-only stream positioned at the first byte, which the caller disposes.</returns>
    /// <remarks>
    /// The copy in the fallback branch is what a memory that does not sit on an array leaves no alternative to; nothing
    /// in this repository produces one, and the branch exists so that a future source of stored content cannot fail
    /// here instead of merely costing an allocation.
    /// </remarks>
    public static MemoryStream Open(ReadOnlyMemory<byte> rawMime) =>
        MemoryMarshal.TryGetArray(rawMime, out var segment) && segment.Array is { } buffer
            ? new MemoryStream(buffer, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(rawMime.ToArray(), writable: false);
}
