// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Emails;

namespace MailMcp.Application.Synchronization;

/// <summary>Indicates that a remote email is larger than the configured local raw MIME storage limit.</summary>
public sealed class EmailContentTooLargeException : Exception
{
    /// <summary>Initializes a new oversized-email exception.</summary>
    public EmailContentTooLargeException()
    {
    }

    /// <summary>Initializes a new oversized-email exception with a safe message.</summary>
    public EmailContentTooLargeException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new oversized-email exception with a safe message and inner exception.</summary>
    public EmailContentTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new oversized-email exception.</summary>
    public EmailContentTooLargeException(EmailOccurrenceId occurrenceId, long sizeOctets, long maxAllowedOctets)
        : base($"Email {occurrenceId.AccountId.Value}/{occurrenceId.FolderName.Value}/{occurrenceId.UidValidity.Value}/{occurrenceId.Uid.Value} is {sizeOctets} octets, which exceeds the configured {maxAllowedOctets} octet raw MIME limit.")
    {
        this.OccurrenceId = occurrenceId;
        this.SizeOctets = sizeOctets;
        this.MaxAllowedOctets = maxAllowedOctets;
    }

    /// <summary>Gets the oversized occurrence identity, when available.</summary>
    public EmailOccurrenceId? OccurrenceId { get; }

    /// <summary>Gets the advertised or streamed size, when available.</summary>
    public long? SizeOctets { get; }

    /// <summary>Gets the configured maximum size, when available.</summary>
    public long? MaxAllowedOctets { get; }
}
