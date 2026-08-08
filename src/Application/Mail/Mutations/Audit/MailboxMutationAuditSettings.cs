// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Mutations.Audit;

/// <summary>States what one account has decided about keeping a record of the changes MailFathom made to its mailbox.</summary>
/// <param name="IsEnabled">Whether a finished mutation on this account leaves an audit entry behind.</param>
/// <param name="Retention">How long an entry of this account's is kept before it is erased.</param>
/// <remarks>
/// <para>
/// The two travel together because neither answers the accountability question on its own. A trail nobody turned on
/// holds nothing to describe, and a trail with no bound on it is a growing store of derived personal data that no
/// operator undertook to keep — so a deployment that decides to hold the record decides how long it holds it in the same
/// breath.
/// </para>
/// <para>
/// <see cref="Disabled" /> is what an account that configured nothing gets. The default is off rather than on because
/// the entry says where a person's mail has been and at whose instruction, and data minimization by default means an
/// installation that never asked for that never accumulates it.
/// </para>
/// </remarks>
public sealed record MailboxMutationAuditSettings(bool IsEnabled, TimeSpan Retention)
{
    /// <summary>Gets the settings of an account that has not turned the trail on, which is every account by default.</summary>
    /// <remarks>
    /// The retention is <see cref="TimeSpan.Zero" />, which names no window and erases nothing. That is the honest
    /// answer for an account this deployment does not configure: there is no operator decision to apply, and reading the
    /// absence as "erase immediately" would destroy a trail on the strength of a missing configuration section. An
    /// account that turns the trail off keeps the window it configured, so its existing entries age out as they were
    /// always going to.
    /// </remarks>
    public static MailboxMutationAuditSettings Disabled { get; } = new(IsEnabled: false, TimeSpan.Zero);
}
