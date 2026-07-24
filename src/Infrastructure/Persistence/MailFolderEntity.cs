// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Persistence;

internal sealed class MailFolderEntity
{
    public long Id { get; set; }

    public required string AccountId { get; set; }

    public required string FolderName { get; set; }

    public uint UidValidity { get; set; }

    public uint? LastSeenUid { get; set; }

    public DateTimeOffset? SynchronizedAt { get; set; }
}
