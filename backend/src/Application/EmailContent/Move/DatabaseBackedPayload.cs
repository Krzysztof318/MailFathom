// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.EmailContent.Move;

/// <summary>One payload the database still holds, as the move reads it before deciding to carry it.</summary>
/// <remarks>
/// <para>
/// The bytes are deliberately absent. A batch names as many payloads as the pass may carry, and a batch that carried
/// their contents would hold every one of them at once — which is the whole message multiplied by the batch size, over
/// a walk whose reason for existing is that the mail is large. The move reads one payload's bytes at the moment it
/// carries them and lets go of them before it reads the next.
/// </para>
/// <para>
/// The length and the digest come from the row rather than from the bytes, because they are what the copy is verified
/// against. Reading them here, in the same query that named the payload, is what keeps the move from verifying an
/// object against a row a concurrent write had already replaced.
/// </para>
/// </remarks>
/// <param name="Kind">Which of the four payload kinds this is, which decides the table it lives in.</param>
/// <param name="PayloadId">The identity of the row holding it, which is what the walk is ordered by.</param>
/// <param name="ByteLength">How many bytes of raw MIME the row records.</param>
/// <param name="Sha256Hash">The digest the row records over them.</param>
public sealed record DatabaseBackedPayload(
    EmailContentKind Kind,
    Guid PayloadId,
    long ByteLength,
    ReadOnlyMemory<byte> Sha256Hash);
