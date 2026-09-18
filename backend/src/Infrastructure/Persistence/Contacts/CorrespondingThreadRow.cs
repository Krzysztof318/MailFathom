// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>One scanned message naming a contact, as PostgreSQL returns it.</summary>
/// <param name="EmailThreadId">The conversation the message belongs to.</param>
/// <param name="StoredEmailId">The message's own stable local identity.</param>
/// <param name="Subject">What the message was about, or <see langword="null" /> where it carried none.</param>
/// <param name="ReceivedAt">When it arrived, which the window's own comparison already established is known.</param>
/// <remarks>
/// A row of primitives rather than the application's own shape, because a domain value object's factory inside a
/// projection either fails to translate or forces the query into this process.
/// </remarks>
internal sealed record CorrespondingThreadRow(
    Guid EmailThreadId,
    Guid StoredEmailId,
    string? Subject,
    DateTimeOffset ReceivedAt);
