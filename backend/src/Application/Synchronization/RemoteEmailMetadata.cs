// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Synchronization;

/// <summary>Contains privacy-minimized metadata required for local mailbox timelines and synchronization progress.</summary>
/// <param name="OccurrenceId">Where the message sits on the server, which is its identity for this account.</param>
/// <param name="InternetMessageId">The message identifier its author's client wrote, when the server reported one.</param>
/// <param name="Subject">The subject line the envelope carried, when the server reported one.</param>
/// <param name="SentAt">When the envelope says the message was sent, normalized to UTC.</param>
/// <param name="SizeOctets">How large the server says the message is, which is what the content budget is spent against.</param>
/// <param name="IsRemotelySeen">
/// Whether the server had marked the message <c>\Seen</c> when it described it. It is read here rather than from the
/// stored row because the row's flag is written by the backward pass, whose window need not cover what the forward pass
/// has just stored — so a message stored in this run has no reconciled flag to consult and would read as unread
/// whatever the server said. What turns on it is whether the run reports the message as mail that arrived.
/// A reconstruction describing an occurrence some earlier run discovered therefore has no such report to carry and
/// says <see langword="true" />, which is the reading no arrival can follow from, rather than a column it must not read.
/// </param>
public sealed record RemoteEmailMetadata(
    EmailOccurrenceId OccurrenceId,
    string? InternetMessageId,
    string? Subject,
    DateTimeOffset? SentAt,
    long SizeOctets,
    bool IsRemotelySeen);
