// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail.Audit;

/// <summary>States what one account has decided about keeping a record of the questions answered from its mailbox.</summary>
/// <param name="IsEnabled">Whether an answering run that could read this account's mail leaves an entry behind.</param>
/// <param name="Retention">How long an entry of this account's is kept before it is erased.</param>
/// <remarks>
/// <para>
/// The same pair the mutation trail's own settings carry, for the same reason and deliberately not a second shape: a
/// record nobody turned on holds nothing to describe, and a record with no bound on it is a growing store of derived
/// personal data that no operator undertook to keep. A deployment that decides to hold one decides how long it holds it
/// in the same breath.
/// </para>
/// <para>
/// It is a separate decision from the mutation trail rather than the same switch, because the two records answer
/// different questions and cost different things. One says where a person's mail has been; this says what it was read
/// for. An operator may well want the first without the second, and the reverse.
/// </para>
/// <para>
/// <see cref="Disabled" /> is what an account that configured nothing gets. Off by default because the entry says which
/// of a person's messages a question reached, and data minimization by default means an installation that never asked
/// for that never accumulates it.
/// </para>
/// </remarks>
public sealed record MailAnsweringAuditSettings(bool IsEnabled, TimeSpan Retention)
{
    /// <summary>Gets the settings of an account that has not turned the record on, which is every account by default.</summary>
    /// <remarks>
    /// The retention is <see cref="TimeSpan.Zero" />, which names no window and erases nothing — the honest answer for
    /// an account this deployment does not configure, since there is no operator decision to apply and reading the
    /// absence as "erase immediately" would destroy a record on the strength of a missing configuration section.
    /// </remarks>
    public static MailAnsweringAuditSettings Disabled { get; } = new(IsEnabled: false, TimeSpan.Zero);
}
