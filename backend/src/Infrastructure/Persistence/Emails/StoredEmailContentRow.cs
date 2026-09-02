// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The columns one stored-content read projects, before they become the application's own value.</summary>
/// <param name="RawMime">The bytes this row itself carries, projected only where the database is the authoritative store for them.</param>
/// <param name="MimeByteLength">The length recorded when those bytes were written.</param>
/// <param name="Sha256Hash">The digest recorded when those bytes were written.</param>
/// <param name="Backend">Which store holds the payload, which is this row's own answer rather than the deployment's.</param>
/// <param name="ObjectLocator">The whole key the object was written under, or <see langword="null" /> when the database holds the payload.</param>
/// <param name="CarriesDatabasePayload">Whether the payload column holds anything at all, whichever store is authoritative.</param>
/// <remarks>
/// <para>
/// The row exists because EF Core projects into provider types: the columns are <c>bytea</c> and arrive as
/// <see cref="byte" /> arrays, which the store then hands over as read-only memory so no caller can write through them.
/// </para>
/// <para>
/// An object-backed row's payload column is deliberately <em>not</em> projected into <paramref name="RawMime" />, and
/// that is what keeps the retained duplicate from costing every read of a moved message a second whole message off the
/// database. What is projected instead is whether there is one, which is a boolean over the same column: a read that
/// needs the retained bytes asks for them afterwards, and only a read whose object could not be vouched for ever does.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailContentRow(
    byte[]? RawMime,
    long MimeByteLength,
    byte[] Sha256Hash,
    ContentStorageBackend Backend,
    string? ObjectLocator,
    bool CarriesDatabasePayload)
{
    /// <summary>Turns the returned columns into the application's own value, over a payload resolved from wherever it lives.</summary>
    /// <param name="payload">The bytes themselves: this row's own column under the database backend, or what the endpoint answered under the object one.</param>
    /// <returns>The stored content, whose digest is a read-only view over the array the provider returned.</returns>
    /// <remarks>
    /// Every table this store reads a message out of — arrived mail, an outgoing send, a draft, a recurring send's
    /// template — projects into this row and ends here, so the conversion from the provider's arrays to read-only
    /// memory is stated once rather than at each read.
    /// <para>
    /// The payload is passed in rather than read off the row, because which store answered is the row's own business
    /// and resolving it needs a network call under one of the two backends. The recorded length and digest come from
    /// the row either way, which is what lets the caller verify an object against what was written for it.
    /// </para>
    /// </remarks>
    internal StoredEmailContent ToStoredContent(ReadOnlyMemory<byte> payload) =>
        new(payload, this.MimeByteLength, this.Sha256Hash.AsMemory());
}
