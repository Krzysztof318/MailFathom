// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Messages;

namespace MailMcp.Application.MessageContent;

/// <summary>Contains raw MIME content fetched through a seen-preserving mailbox operation.</summary>
/// <param name="OccurrenceId">The stable remote message occurrence identity.</param>
/// <param name="RawMime">The raw RFC 822 bytes.</param>
public sealed record RemoteMessageContent(MessageOccurrenceId OccurrenceId, ReadOnlyMemory<byte> RawMime);
