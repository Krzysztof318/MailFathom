// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Restore;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Observability;

/// <summary>Publishes how far a restoring account's mailbox has got back onto its source.</summary>
/// <remarks>
/// <para>
/// A sibling of <see cref="IMailboxDrainTelemetry" /> and reported for the same reason: what a client sees about a
/// held message is nothing at all, so the counts are the operator's whole view of a switch off. Two of the figures
/// here have no counterpart on the drain, and both are why this exists rather than being folded into it — an append
/// nobody answered holds the account in its phase until somebody settles it, and a pause names a mapping to correct.
/// </para>
/// <para>
/// The dimensions are the account alias and MailFathom's own name for a failure or a pause, and none of them is
/// derived from a message: no subject, no address, no folder path, no UID.
/// </para>
/// </remarks>
public interface IMailboxRestoreTelemetry
{
    /// <summary>Records what one pass at putting an account's mailbox back did.</summary>
    /// <param name="account">The account whose mailbox is being restored.</param>
    /// <param name="report">The counts the pass produced.</param>
    void Report(MailAccountId account, MailboxRestoreReport report);
}
