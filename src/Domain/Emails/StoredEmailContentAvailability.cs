// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Domain.Emails;

/// <summary>Records whether raw MIME content is locally available for a stored email occurrence.</summary>
/// <remarks>
/// Synchronization must never leave a discovered occurrence without a durable local trace. When content is
/// intentionally not stored, the occurrence is still recorded with the reason so the gap stays auditable
/// instead of existing only as a counter in a log line.
/// </remarks>
public enum StoredEmailContentAvailability
{
    /// <summary>Raw MIME content is stored locally next to the occurrence metadata.</summary>
    Available = 0,

    /// <summary>Raw MIME content was deliberately not stored because the email exceeded the configured size limit.</summary>
    ExceededSizeLimit = 1,
}
