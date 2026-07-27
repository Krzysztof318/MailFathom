// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;
using MailMcp.Domain.Emails;

namespace MailMcp.Infrastructure.Persistence;

[RequiresIntegrationCoverage]
internal sealed class StoredEmailEntity
{
    public Guid Id { get; set; }

    public long MailFolderId { get; set; }

    public required MailFolderEntity MailFolder { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    public string? InternetMessageId { get; set; }

    public string? Subject { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public long SizeOctets { get; set; }

    public StoredEmailContentAvailability ContentAvailability { get; set; }

    public uint ConcurrencyVersion { get; set; }

    public EmailMessageContentEntity? Content { get; set; }
}
