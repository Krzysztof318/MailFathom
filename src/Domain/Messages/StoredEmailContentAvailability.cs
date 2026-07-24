// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Messages;

/// <summary>Records whether raw MIME content is locally available for a stored message occurrence.</summary>
/// <remarks>
/// Synchronization must never leave a discovered occurrence without a durable local trace. When content is
/// intentionally not stored, the occurrence is still recorded with the reason so the gap stays auditable
/// instead of existing only as a counter in a log line.
/// </remarks>
public enum StoredEmailContentAvailability
{
    /// <summary>Raw MIME content is stored locally next to the occurrence metadata.</summary>
    Available = 0,

    /// <summary>Raw MIME content was deliberately not stored because the message exceeded the configured size limit.</summary>
    ExceededSizeLimit = 1,
}
