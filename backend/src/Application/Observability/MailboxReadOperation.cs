// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Observability;

/// <summary>Names the local read a report is being opened for.</summary>
/// <remarks>
/// Each member names the operation the use case performs rather than the tool that reached it, because a second
/// entrypoint over the same use case is work of the same kind and the protocol span above already carries the tool's
/// own name. What each member is published as is the adapter's to decide, which is what keeps a span name out of
/// <c>Application</c> along with every other tracing detail.
/// </remarks>
public enum MailboxReadOperation
{
    /// <summary>Reading which accounts this deployment serves and how current each one's local copy is.</summary>
    ReadAccountDirectory = 0,

    /// <summary>Reading one bounded page of the stored email timeline.</summary>
    ListMailboxTimeline = 1,

    /// <summary>Ranking the stored emails against a query and reading one window of the ranking.</summary>
    SearchMailbox = 2,

    /// <summary>Reading the stored content of the emails one call names.</summary>
    ReadEmailContent = 3,
}
