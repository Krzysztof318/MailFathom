// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Common.Observability;

/// <summary>Names every activity source and meter MailFathom publishes to under its own name.</summary>
/// <remarks>
/// <para>
/// A name is decided by the subsystem it describes, never by the feature that happens to emit first and never by the
/// assembly the code currently sits in. An operator filters a dashboard by subsystem, and a subsystem survives a type
/// moving between projects, so <c>MailFathom.Mail</c> stays the answer whether the span is opened in the mailbox
/// adapter or in the use case above it. The <see cref="NamePrefix" /> is what makes one filter reach all of them.
/// </para>
/// <para>
/// One name serves both registries. An activity source and a meter are separate subscriptions to OpenTelemetry and
/// cannot collide, so a subsystem that emits spans and instruments registers the same string twice rather than
/// carrying two names that could drift apart. The names are declared here ahead of the code that publishes to them,
/// which is the whole point of the type: a boundary does not choose what it is called, it reads it.
/// </para>
/// <para>
/// A span or an instrument published under one of these names carries counts, sizes, durations, outcomes, error codes,
/// and MailFathom's own configured account and folder aliases. It never carries mail content, an address, a subject, a
/// remote folder path, a message identifier, a UID, a search term, a credential, or model prompt and completion text —
/// which is a cardinality rule as much as a privacy one, because every one of those would open a time series per
/// message or per person.
/// </para>
/// <para>
/// A meter is obtained from <see cref="System.Diagnostics.Metrics.IMeterFactory" /> by the service that owns the
/// instruments, so a test observes them through its own provider instead of through process-wide state. An activity
/// source has no such factory and is created directly from the name below.
/// </para>
/// </remarks>
public static class MailFathomTelemetry
{
    /// <summary>The prefix every name below carries.</summary>
    public const string NamePrefix = "MailFathom";

    /// <summary>Names mailbox work: IMAP sessions, folder reconciliation, synchronization runs, and mutations.</summary>
    public const string Mail = $"{NamePrefix}.Mail";

    /// <summary>Names the MCP surface: tool calls and the protocol boundary that serves them.</summary>
    public const string Mcp = $"{NamePrefix}.Mcp";

    /// <summary>Names local storage: the email content store and the write sessions around it.</summary>
    public const string Persistence = $"{NamePrefix}.Persistence";

    /// <summary>Names mail text extraction and the backfill that reprocesses what earlier runs left.</summary>
    public const string Extraction = $"{NamePrefix}.Extraction";

    /// <summary>Gets every name declared above, which is the set the host subscribes.</summary>
    public static IReadOnlyList<string> All { get; } = [Mail, Mcp, Persistence, Extraction];
}
