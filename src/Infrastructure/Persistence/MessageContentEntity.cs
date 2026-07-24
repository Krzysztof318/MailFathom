// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Persistence;

internal sealed class MessageContentEntity
{
    public long Id { get; set; }

    public required string AccountId { get; set; }

    public required string FolderName { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    public required byte[] RawMime { get; set; }
}
