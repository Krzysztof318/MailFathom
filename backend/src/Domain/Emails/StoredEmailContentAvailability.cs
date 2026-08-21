// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

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

    /// <summary>
    /// Raw MIME content was deliberately not stored because local content storage had reached its configured ceiling,
    /// and is fetched once that ceiling has headroom again.
    /// </summary>
    /// <remarks>
    /// It stays distinct from <see cref="ExceededSizeLimit" /> because the two have opposite futures. An email above the
    /// size limit will exceed it on every later run, so nothing is waiting for it; an email recorded here is one whose
    /// payload the mailbox would have served, and a later run fetches it as soon as the ceiling permits. Collapsing them
    /// would leave a queue that nothing could tell apart from a permanent gap.
    /// </remarks>
    AwaitingStorageHeadroom = 2,
}
