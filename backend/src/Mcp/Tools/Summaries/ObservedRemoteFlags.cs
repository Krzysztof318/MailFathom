// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Domain.Emails;

namespace MailFathom.Mcp.Tools.Summaries;

/// <summary>Publishes the IMAP flags a synchronization run last observed for one email.</summary>
/// <remarks>
/// Every description on this type reaches the client inside the tool's output schema, so each one states what the value
/// means on the wire rather than how it is stored. The wording is deliberate about the flags being an observation: a
/// client that reads them as MailFathom's own state would expect writing them back to be possible, and it is not.
/// </remarks>
[Description("The IMAP flags a mail server reported for the email when it was last synchronized. These are observations of the server's state, not local state, and reading mail through MailFathom never changes them.")]
internal sealed record ObservedRemoteFlags
{
    /// <summary>Gets whether the server reported the email as read.</summary>
    [Description("Whether the mail server reported the email as read at the time of the observation.")]
    public required bool Seen { get; init; }

    /// <summary>Gets whether the server reported the email as answered.</summary>
    [Description("Whether the mail server reported the email as answered.")]
    public required bool Answered { get; init; }

    /// <summary>Gets whether the server reported the email as flagged for attention.</summary>
    [Description("Whether the mail server reported the email as flagged for attention.")]
    public required bool Flagged { get; init; }

    /// <summary>Gets whether the server reported the email as a draft.</summary>
    [Description("Whether the mail server reported the email as a draft.")]
    public required bool Draft { get; init; }

    /// <summary>Gets whether the server reported the email as deleted but not yet expunged.</summary>
    [Description("Whether the mail server reported the email as deleted but not yet expunged from the folder.")]
    public required bool Deleted { get; init; }

    /// <summary>Gets the keywords the server reported, which are the flags nobody standardized.</summary>
    [Description("The keywords the mail server reported for the email, such as $JUNK or a label a mail client set, in upper case and without duplicates. Flag names are compared without regard to case, so the case a keyword is written in never decides a match; an empty list means the server reported none, or that nothing has observed this email yet.")]
    public required IReadOnlyList<string> Keywords { get; init; }

    /// <summary>Gets when the flags were last read from the server, or <see langword="null" /> when no run has observed them.</summary>
    [Description("When the flags were last read from the mail server, as an ISO 8601 timestamp, or null when no synchronization run has observed this email yet. The flags are only as current as this timestamp.")]
    public DateTimeOffset? ObservedAt { get; init; }

    /// <summary>Gets whether the flags were read from a server at all.</summary>
    [Description("Whether these flags were read from a mail server rather than never observed. When false, every flag above is false because nobody has looked yet, not because the server reported none of them.")]
    public required bool WasObserved { get; init; }

    /// <summary>Publishes one observed flag set.</summary>
    /// <param name="flags">The flags the summary carried.</param>
    /// <returns>The wire representation of <paramref name="flags" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="flags" /> is <see langword="null" />.</exception>
    public static ObservedRemoteFlags From(RemoteEmailFlagSnapshot flags)
    {
        ArgumentNullException.ThrowIfNull(flags);

        return new ObservedRemoteFlags
        {
            Seen = flags.IsSeen,
            Answered = flags.IsAnswered,
            Flagged = flags.IsFlagged,
            Draft = flags.IsDraft,
            Deleted = flags.IsDeleted,
            Keywords = flags.Keywords.Values,
            ObservedAt = flags.ObservedAt,
            WasObserved = flags.WasObserved,
        };
    }
}
