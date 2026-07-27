// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;

namespace MailMcp.Infrastructure.Persistence;

[RequiresIntegrationCoverage]
internal sealed class EmailMessageContentEntity
{
    public Guid StoredEmailId { get; set; }

    public required byte[] RawMime { get; set; }

    public long MimeByteLength { get; set; }

    public required byte[] Sha256Hash { get; set; }

    public DateTimeOffset StoredAt { get; set; }

    public required StoredEmailEntity StoredEmail { get; set; }
}
