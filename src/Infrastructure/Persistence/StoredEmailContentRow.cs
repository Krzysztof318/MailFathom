// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>The columns one stored-content read projects, before they become the application's own value.</summary>
/// <param name="RawMime">The stored RFC 822 bytes.</param>
/// <param name="MimeByteLength">The length recorded when those bytes were written.</param>
/// <param name="Sha256Hash">The digest recorded when those bytes were written.</param>
/// <remarks>
/// The row exists because EF Core projects into provider types: the columns are <c>bytea</c> and arrive as
/// <see cref="byte" /> arrays, which the store then hands over as read-only memory so no caller can write through them.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed record StoredEmailContentRow(byte[] RawMime, long MimeByteLength, byte[] Sha256Hash);
