// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The columns one stored-content read projects, before they become the application's own value.</summary>
/// <param name="RawMime">The stored RFC 822 bytes.</param>
/// <param name="MimeByteLength">The length recorded when those bytes were written.</param>
/// <param name="Sha256Hash">The digest recorded when those bytes were written.</param>
/// <remarks>
/// The row exists because EF Core projects into provider types: the columns are <c>bytea</c> and arrive as
/// <see cref="byte" /> arrays, which the store then hands over as read-only memory so no caller can write through them.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailContentRow(byte[] RawMime, long MimeByteLength, byte[] Sha256Hash)
{
    /// <summary>Turns the returned columns into the application's own value.</summary>
    /// <returns>The stored content, whose payload and digest are read-only views over the arrays the provider returned.</returns>
    /// <remarks>
    /// Every table this store reads a message out of — arrived mail, an outgoing send, a draft, a recurring send's
    /// template — projects into this row and ends here, so the conversion from the provider's arrays to read-only
    /// memory is stated once rather than at each read.
    /// </remarks>
    internal StoredEmailContent ToStoredContent() =>
        new(this.RawMime.AsMemory(), this.MimeByteLength, this.Sha256Hash.AsMemory());
}
