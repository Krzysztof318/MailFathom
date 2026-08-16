// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

[RequiresIntegrationCoverage]
internal sealed class OutgoingMessageContentEntity
{
    public Guid OutgoingMessageId { get; set; }

    public required byte[] RawMime { get; set; }

    public long MimeByteLength { get; set; }

    public required byte[] Sha256Hash { get; set; }

    public DateTimeOffset StoredAt { get; set; }

    public required OutgoingMessageEntity OutgoingMessage { get; set; }
}
