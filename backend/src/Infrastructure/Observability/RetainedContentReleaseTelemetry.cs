// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Observability;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Publishes how many retained database copies an operator's releases have freed, and how much they held.</summary>
/// <remarks>
/// <para>
/// The counters are what the release of a large mailbox is watched by, because it is a hundred requests rather than one:
/// what a single batch freed says nothing, and what the deployment has freed since it started is the figure an operator
/// weighs against the backlog they began with. They survive the interruptions and the terminals a long release lives
/// through, which the answers to individual requests do not.
/// </para>
/// <para>
/// The volume is the point rather than a decoration. Releasing is the one step of the move that actually takes weight
/// off a database, so a count of payloads without the bytes behind it would report the work and never the result.
/// </para>
/// <para>
/// Nothing here is derived from a message, and there is no dimension for the payload kind, for the reason the move's
/// instruments have none.
/// </para>
/// </remarks>
public sealed class RetainedContentReleaseTelemetry : IRetainedContentReleaseTelemetry
{
    private readonly Counter<long> releasedPayloads;
    private readonly Counter<long> releasedBytes;

    /// <summary>Initializes the instruments every release reports through.</summary>
    public RetainedContentReleaseTelemetry()
    {
        this.releasedPayloads = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.content.release.released",
            unit: "{payload}",
            description: "Retained database copies freed, leaving the object the only copy of that payload.");
        this.releasedBytes = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.mail.content.release.released.bytes",
            unit: "By",
            description: "Raw MIME the release stopped the database from holding.");
    }

    /// <inheritdoc />
    public void Released(long payloadCount, long byteCount)
    {
        this.releasedPayloads.Add(payloadCount);
        this.releasedBytes.Add(byteCount);
    }
}
