// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Messages;

namespace MailMcp.Application.Synchronization;

/// <summary>Indicates that a remote message is larger than the configured local raw MIME storage limit.</summary>
public sealed class MessageContentTooLargeException : Exception
{
    /// <summary>Initializes a new oversized-message exception.</summary>
    public MessageContentTooLargeException()
    {
    }

    /// <summary>Initializes a new oversized-message exception with a safe message.</summary>
    public MessageContentTooLargeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new oversized-message exception with a safe message and inner exception.</summary>
    public MessageContentTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new oversized-message exception.</summary>
    public MessageContentTooLargeException(MessageOccurrenceId occurrenceId, long sizeOctets, long maxAllowedOctets)
        : base($"Message {occurrenceId.AccountId.Value}/{occurrenceId.FolderName.Value}/{occurrenceId.UidValidity.Value}/{occurrenceId.Uid.Value} is {sizeOctets} octets, which exceeds the configured {maxAllowedOctets} octet raw MIME limit.")
    {
        this.OccurrenceId = occurrenceId;
        this.SizeOctets = sizeOctets;
        this.MaxAllowedOctets = maxAllowedOctets;
    }

    /// <summary>Gets the oversized occurrence identity, when available.</summary>
    public MessageOccurrenceId? OccurrenceId { get; }

    /// <summary>Gets the advertised or streamed size, when available.</summary>
    public long? SizeOctets { get; }

    /// <summary>Gets the configured maximum size, when available.</summary>
    public long? MaxAllowedOctets { get; }
}
