// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>Publishes what an operator's release of retained database copies actually freed.</summary>
/// <remarks>
/// <para>
/// Counters rather than a span, and no scope: the release happens inside a request the transport has already spanned,
/// and the question an operator asks of it is asked across requests — how much of the duplication is gone now, and how
/// many bytes that came to. A release of a large mailbox is a hundred requests, and a figure that only counted one of
/// them would answer nothing.
/// </para>
/// <para>
/// Nothing here is derived from a message, and there is no dimension for the payload kind, for the reason the move's
/// instruments have none: a kind names which table a row is in and an operator does nothing differently for one.
/// </para>
/// </remarks>
public interface IRetainedContentReleaseTelemetry
{
    /// <summary>Records the retained copies one release freed.</summary>
    /// <param name="payloadCount">How many copies were freed.</param>
    /// <param name="byteCount">How many bytes of raw MIME they were holding.</param>
    /// <remarks>A release that freed nothing records nothing, so the counters carry acts rather than requests.</remarks>
    void Released(long payloadCount, long byteCount);
}
