// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;

namespace MailMcp.IntegrationTests.Mailbox;

/// <summary>One message as the server reports it, independently of anything MailMcp stored about it.</summary>
/// <param name="Uid">The remote identifier within the folder's current UIDVALIDITY.</param>
/// <param name="Subject">The subject the envelope carries, which is how a test recognizes the message it seeded.</param>
/// <param name="IsSeen">Whether the server currently holds the <c>\Seen</c> flag for the message.</param>
internal sealed record ObservedEmail(ImapUid Uid, string? Subject, bool IsSeen);
