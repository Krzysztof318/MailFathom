// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.IntegrationTests.Mailbox;

/// <summary>One message as the server reports it, independently of anything MailFathom stored about it.</summary>
/// <param name="Uid">The remote identifier within the folder's current UIDVALIDITY.</param>
/// <param name="Subject">The subject the envelope carries, which is how a test recognizes the message it seeded.</param>
/// <param name="IsSeen">Whether the server currently holds the <c>\Seen</c> flag for the message.</param>
/// <param name="IsFlagged">Whether the server currently holds the <c>\Flagged</c> flag for the message.</param>
/// <param name="Keywords">The keywords the server currently holds for the message, in the order it listed them.</param>
internal sealed record ObservedEmail(
    ImapUid Uid,
    string? Subject,
    bool IsSeen,
    bool IsFlagged,
    IReadOnlyList<string> Keywords);
