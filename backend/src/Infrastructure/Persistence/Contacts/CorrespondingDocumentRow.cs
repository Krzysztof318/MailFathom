// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>One attachment of a message a contact sent, as PostgreSQL returns it.</summary>
/// <param name="StoredEmailId">The message the file arrived on.</param>
/// <param name="AttachmentPosition">Where the file sits in that message's walk.</param>
/// <param name="FileName">The name the sender wrote, or <see langword="null" /> where the part carried none.</param>
/// <param name="DeclaredMediaType">The type the sender declared.</param>
/// <param name="ReceivedAt">When the message carrying it arrived.</param>
/// <remarks>A row of primitives, for the reason <see cref="CorrespondingThreadRow" /> is one.</remarks>
internal sealed record CorrespondingDocumentRow(
    Guid StoredEmailId,
    int AttachmentPosition,
    string? FileName,
    string DeclaredMediaType,
    DateTimeOffset ReceivedAt);
