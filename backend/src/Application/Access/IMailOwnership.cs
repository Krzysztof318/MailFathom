// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Access;

/// <summary>Answers whose mail a unit of background work is acting on, from what that work already holds.</summary>
/// <remarks>
/// <para>
/// A caller-facing request carries its user on the principal, and <see cref="AccessAuthorization.RequireUser" /> is
/// what reads it. A worker carries none: it runs under this process's own identity, which acts for nobody, so a
/// synchronization run, an embedding pass, and a backfill each know a message and never an account. This port is how
/// those reach one, and it exists so that a bound stated per user is applied to the users the work is genuinely for
/// rather than to whoever a request happened to admit.
/// </para>
/// <para>
/// It answers with the account rather than with a user, because no stored message names one: mail belongs to the
/// mailbox, and who that mailbox serves is the assignment relation read separately and at the moment the work runs.
/// A ceiling stated per user is therefore applied across the account's assigned users rather than to the one a row
/// happened to carry, which is what ADR 0014 requires of a mailbox two people share.
/// </para>
/// </remarks>
public interface IMailOwnership
{
    /// <summary>Reads the account one locally stored email belongs to.</summary>
    /// <param name="storedEmailId">The stored email whose account is asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account the message belongs to.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no message is stored under that identifier.</exception>
    /// <remarks>
    /// Asked once per message rather than per provider call, because the answer cannot change while a message is being
    /// worked on and the call it precedes costs orders of magnitude more than the read.
    /// </remarks>
    Task<MailAccountId> ReadStoredEmailAccountAsync(StoredEmailId storedEmailId, CancellationToken cancellationToken);
}
