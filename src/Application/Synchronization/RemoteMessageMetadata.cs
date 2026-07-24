// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Messages;

namespace MailMcp.Application.Synchronization;

/// <summary>Contains privacy-minimized metadata required for local mailbox timelines and synchronization progress.</summary>
public sealed record RemoteMessageMetadata(MessageOccurrenceId OccurrenceId, string? InternetMessageId, string? Subject, DateTimeOffset? SentAt, long SizeOctets);
