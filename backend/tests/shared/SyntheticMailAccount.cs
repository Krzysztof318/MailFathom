// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.TestSupport;

/// <summary>The mailboxes a test arranges when which mailbox it is does not matter, only that there are two of them.</summary>
/// <remarks>
/// The counterpart of <see cref="SyntheticUser" />, and stated here for the same reason: two fixed identities so a
/// failure names the same value every run, rather than one generated per suite. The mail graph is keyed by the account
/// alone, so most tests that used to need one user and somebody else now need one mailbox and another one.
/// </remarks>
internal static class SyntheticMailAccount
{
    /// <summary>Gets the mailbox a deployment serves, which most arranged mail belongs to.</summary>
    public static MailAccountId Deployment { get; } =
        MailAccountId.Create("33333333-3333-3333-3333-333333333333");

    /// <summary>Gets a second mailbox, which is what a test bounding two mailboxes against each other reaches for.</summary>
    public static MailAccountId Another { get; } =
        MailAccountId.Create("44444444-4444-4444-4444-444444444444");
}
