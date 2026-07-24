// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
namespace MailMcp.Infrastructure.Persistence;

[ExcludeFromCodeCoverage(Justification = "Provider-boundary adapter behavior requires future integration coverage.")]
internal sealed class MessageMetadataEntity
{
    public long Id { get; set; }

    public required string AccountId { get; set; }

    public required string FolderName { get; set; }

    public uint UidValidity { get; set; }

    public uint Uid { get; set; }

    public string? InternetMessageId { get; set; }

    public string? Subject { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public long SizeOctets { get; set; }
}
