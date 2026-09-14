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
/// synchronization run, an embedding pass, and a backfill each know an account or a message and never a person. This
/// port is how those reach one, and it exists so that a bound stated per user is charged to the user the work is
/// genuinely for rather than to whoever a request happened to admit.
/// </para>
/// <para>
/// Ownership hangs on the mail account, and a stored message inherits it from the folder it was synchronized into, so
/// the answer is a pair of columns of the message's own row rather than a resolution through the account. Both halves
/// come back together because the work needs both and they cost one read: a spend ceiling is stated per user, and what
/// a scanner redacts on the way out is the account's own posture.
/// </para>
/// </remarks>
public interface IMailOwnership
{
    /// <summary>Reads the mail account one locally stored email belongs to, named by its user and its identifier.</summary>
    /// <param name="storedEmailId">The stored email whose account is asked for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account the message belongs to.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no message is stored under that identifier.</exception>
    /// <remarks>
    /// Asked once per message rather than per provider call, because the answer cannot change while a message is being
    /// worked on and the call it precedes costs orders of magnitude more than the read.
    /// </remarks>
    Task<MailAccountIdentity> ReadStoredEmailAccountAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken);
}
