// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Synchronization;

/// <summary>States how far back into a folder's history synchronization may reach.</summary>
/// <remarks>
/// <para>
/// The bound compares against the date the mail server received an email, which IMAP keeps as its
/// <c>INTERNALDATE</c>, and never against the <c>Date</c> header that MailFathom stores as the sent timestamp. The two
/// disagree for imported and forwarded mail: an archive copied onto a new server carries the arrival date of the copy,
/// and a message forwarded today carries the old header date of what it quotes. Arrival is what decides whether an
/// email is part of the backlog this bound exists to keep out of a first run, it grows with the UID sequence a run
/// walks, and every server keeps it for every email, while a <c>Date</c> header can be absent or unparseable.
/// </para>
/// <para>
/// The bound is a date and not an instant because IMAP compares dates alone, disregarding time and time zone. An email
/// received at any time of the day named by <see cref="EarliestEmailReceivedDate" /> is inside the window.
/// </para>
/// </remarks>
public readonly record struct MailSynchronizationWindow
{
    private MailSynchronizationWindow(DateOnly? earliestEmailReceivedDate) =>
        this.EarliestEmailReceivedDate = earliestEmailReceivedDate;

    /// <summary>Gets the window that reaches every email a folder still holds.</summary>
    public static MailSynchronizationWindow Unbounded => default;

    /// <summary>Gets the earliest date a server may have received an email on for it to be synchronized, or <see langword="null" /> when the window is unbounded.</summary>
    public DateOnly? EarliestEmailReceivedDate { get; }

    /// <summary>Creates a window that reaches no further back than one date.</summary>
    /// <param name="earliestEmailReceivedDate">The earliest date a server may have received an email on, which is itself inside the window.</param>
    /// <returns>A window bounded at that date.</returns>
    public static MailSynchronizationWindow EmailsReceivedSince(DateOnly earliestEmailReceivedDate) =>
        new(earliestEmailReceivedDate);
}
