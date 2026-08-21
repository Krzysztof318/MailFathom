// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes one durable record a change produced.</summary>
/// <remarks>
/// The change and the state are published under the names MailFathom's own log lines and counters use, so a caller
/// quoting one to an operator is quoting the word they will find. Neither is a caller-facing enumeration this surface
/// invented, which is why both are text.
/// </remarks>
[Description("One durable record: which change it carries, what identifies it, and where it stands.")]
internal sealed record RecordedMailboxChange
{
    /// <summary>Gets the change the record carries.</summary>
    [Description("The change written down: set-seen, set-flagged, add-keywords, remove-keywords, or set-keywords.")]
    public required string Change { get; init; }

    /// <summary>Gets what everything afterwards refers to that change by.</summary>
    [Description("The identifier of the durable record. Quote it when asking an operator why a change has not arrived.")]
    public required string ChangeRecordId { get; init; }

    /// <summary>Gets where the record stands.</summary>
    [Description("Where the record stands: pending means nothing has been issued to the mail server yet, which is what a fresh call returns; converging, completed, and dead-lettered are the later states, and a call repeated under the same requestId answers with whichever the earlier one has reached. dead-lettered means the change will not be attempted again and needs an operator.")]
    public required string State { get; init; }
}
