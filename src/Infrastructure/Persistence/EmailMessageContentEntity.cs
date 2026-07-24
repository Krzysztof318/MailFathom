// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
namespace MailMcp.Infrastructure.Persistence;

// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
internal sealed class EmailMessageContentEntity
{
    public Guid StoredEmailId { get; set; }

    public required byte[] RawMime { get; set; }

    public long MimeByteLength { get; set; }

    public required byte[] Sha256Hash { get; set; }

    public DateTimeOffset StoredAt { get; set; }

    public required StoredEmailEntity StoredEmail { get; set; }
}
