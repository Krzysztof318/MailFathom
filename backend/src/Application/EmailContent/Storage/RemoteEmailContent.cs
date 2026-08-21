// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Storage;

/// <summary>Contains raw MIME content fetched through a seen-preserving mailbox operation.</summary>
/// <param name="OccurrenceId">The stable remote email occurrence identity.</param>
/// <param name="RawMime">The raw RFC 822 bytes.</param>
public sealed record RemoteEmailContent(EmailOccurrenceId OccurrenceId, ReadOnlyMemory<byte> RawMime);
