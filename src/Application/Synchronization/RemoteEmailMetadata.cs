// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization;

/// <summary>Contains privacy-minimized metadata required for local mailbox timelines and synchronization progress.</summary>
public sealed record RemoteEmailMetadata(EmailOccurrenceId OccurrenceId, string? InternetMessageId, string? Subject, DateTimeOffset? SentAt, long SizeOctets);
