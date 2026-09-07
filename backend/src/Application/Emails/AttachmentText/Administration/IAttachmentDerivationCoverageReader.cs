// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.AttachmentText.Administration;

/// <summary>Counts how far attachment and image reading has come, for one account or for the whole deployment.</summary>
/// <remarks>
/// <para>
/// A read of committed local state and nothing else. It reaches no provider and opens no message, so the numbers an
/// operator is shown before agreeing to a spend cost nothing to produce.
/// </para>
/// <para>
/// One port answers both scopes because they are one query with one predicate more. A deployment administrator asks
/// what the instance owes; an operator watching a mailbox that is not returning what they expect asks it of the account
/// in front of them, and two implementations would let the two disagree about what "outstanding" means.
/// </para>
/// </remarks>
public interface IAttachmentDerivationCoverageReader
{
    /// <summary>Counts what attachment reading has done and what it still owes.</summary>
    /// <param name="account">The account to count, or <see langword="null" /> to count every account together.</param>
    /// <param name="cancellationToken">Cancels the counting.</param>
    /// <returns>The coverage, which reports nothing outstanding for a scope holding no mail with attachments.</returns>
    /// <remarks>
    /// Unbounded aggregates over a mailbox's attachment readings, so it is asked once per operator command or status
    /// request rather than per unit of work.
    /// </remarks>
    Task<AttachmentDerivationCoverage> ReadCoverageAsync(
        MailAccountIdentity? account,
        CancellationToken cancellationToken);
}
