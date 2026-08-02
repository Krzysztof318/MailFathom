// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails;

/// <summary>Reports the IMAP flags a mail server last showed for one email, and when they were read.</summary>
/// <param name="ObservedAt">When the flags below were last read from the server, or <see langword="null" /> while they have never been read.</param>
/// <param name="IsSeen">Whether the server reported the <c>\Seen</c> flag.</param>
/// <param name="IsAnswered">Whether the server reported the <c>\Answered</c> flag.</param>
/// <param name="IsFlagged">Whether the server reported the <c>\Flagged</c> flag.</param>
/// <param name="IsDraft">Whether the server reported the <c>\Draft</c> flag.</param>
/// <param name="IsDeleted">Whether the server reported the <c>\Deleted</c> flag.</param>
/// <remarks>
/// <para>
/// The snapshot is an observation of server state and travels in one direction only: MailFathom reads mail read-only, so
/// no application path turns any of these into an IMAP <c>STORE</c>. Above all, reading an email through MailFathom never
/// sets <c>\Seen</c> remotely, and this type reports what the server said rather than what a local reader did.
/// </para>
/// <para>
/// <paramref name="ObservedAt" /> is what separates "the server reported none of these flags" from "nobody has looked
/// yet", which no combination of the booleans can express on its own. A caller that filters or displays a flag reads it
/// together with the timestamp, because <see cref="NeverObserved" /> carries every flag as <see langword="false" />
/// without having asked any server.
/// </para>
/// </remarks>
public sealed record RemoteEmailFlagSnapshot(
    DateTimeOffset? ObservedAt,
    bool IsSeen,
    bool IsAnswered,
    bool IsFlagged,
    bool IsDraft,
    bool IsDeleted)
{
    /// <summary>Gets the snapshot of an email whose remote flags no run has read yet.</summary>
    public static RemoteEmailFlagSnapshot NeverObserved { get; } = new(
        ObservedAt: null,
        IsSeen: false,
        IsAnswered: false,
        IsFlagged: false,
        IsDraft: false,
        IsDeleted: false);

    /// <summary>Gets whether these flags were read from a server rather than never observed.</summary>
    public bool WasObserved => this.ObservedAt is not null;
}
