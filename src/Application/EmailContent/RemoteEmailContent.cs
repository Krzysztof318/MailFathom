// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent;

/// <summary>Contains raw MIME content fetched through a seen-preserving mailbox operation.</summary>
/// <param name="OccurrenceId">The stable remote email occurrence identity.</param>
/// <param name="RawMime">The raw RFC 822 bytes.</param>
public sealed record RemoteEmailContent(EmailOccurrenceId OccurrenceId, ReadOnlyMemory<byte> RawMime);
