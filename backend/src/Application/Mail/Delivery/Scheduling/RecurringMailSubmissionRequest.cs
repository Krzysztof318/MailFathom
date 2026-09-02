// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;

namespace MailFathom.Application.Mail.Delivery.Scheduling;

/// <summary>States one message somebody asked this deployment to send again, in the terms they wrote it in.</summary>
/// <remarks>
/// <para>
/// It is a submission request with a repetition where the due time would be, and everything the two have in common
/// means the same thing: no sending address, an account named the way a caller names one, and a requester the caller
/// supplies because only they know whether they are asking again or asking anew.
/// </para>
/// <para>
/// The repetition is the text a caller wrote, in the syntax this system's recurring dispatch already owns. It is read
/// where the declaration is made, so a form nobody can resolve is refused while somebody is present to be told rather
/// than every week from a worker.
/// </para>
/// </remarks>
public sealed record RecurringMailSubmissionRequest
{
    /// <summary>Gets the account every occurrence is sent as, named as a caller names one.</summary>
    public required MailAccountSelector Account { get; init; }

    /// <summary>Gets the people every occurrence is addressed to, in the headers the author named them in.</summary>
    public required IReadOnlyList<NamedRecipient> Recipients { get; init; }

    /// <summary>Gets the subject line the author wrote.</summary>
    public required string Subject { get; init; }

    /// <summary>Gets the plain-text body the author wrote, which every occurrence carries.</summary>
    public required string PlainTextBody { get; init; }

    /// <summary>Gets the HTML alternative the author wrote, or <see langword="null" /> when they wrote none.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>Gets the authored act asking, which is what makes the same declaration twice one declaration.</summary>
    public required OutgoingEmailRequester Requester { get; init; }

    /// <summary>Gets the repetition, written in the syntax a schedule is declared in.</summary>
    public required string Schedule { get; init; }
}
