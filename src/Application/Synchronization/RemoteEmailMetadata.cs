// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;

namespace MailMcp.Application.Synchronization;

/// <summary>Contains privacy-minimized metadata required for local mailbox timelines and synchronization progress.</summary>
public sealed record RemoteEmailMetadata(EmailOccurrenceId OccurrenceId, string? InternetMessageId, string? Subject, DateTimeOffset? SentAt, long SizeOctets);
