// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;

namespace MailMcp.Application.EmailContent;

/// <summary>Contains raw MIME content fetched through a seen-preserving mailbox operation.</summary>
/// <param name="OccurrenceId">The stable remote email occurrence identity.</param>
/// <param name="RawMime">The raw RFC 822 bytes.</param>
public sealed record RemoteEmailContent(EmailOccurrenceId OccurrenceId, ReadOnlyMemory<byte> RawMime);
