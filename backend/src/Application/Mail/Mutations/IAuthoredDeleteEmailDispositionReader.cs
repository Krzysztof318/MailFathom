// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Answers what one account keeps locally of an email MailFathom is about to delete on its mail server.</summary>
/// <remarks>
/// <para>
/// It is read where the delete is authored rather than where it completes, because those are different runs: the
/// deletion is written down and issued now, and the local copy is disposed of by the synchronization run that later
/// sees the message gone. Reading the configuration there would apply whatever an operator had changed it to in the
/// meantime, so the answer this gives travels on the mutation record instead.
/// </para>
/// <para>
/// It is a port of its own rather than a second method on
/// <see cref="Synchronization.Reconciliation.IRemotelyDeletedEmailDispositionReader" /> because the two answer for
/// different acts and are read by different callers: that one belongs to reconciliation observing a removal somebody
/// else made, and this one to the act of deleting. One port would let a caller reach for the wrong answer with no
/// signature to stop it.
/// </para>
/// </remarks>
public interface IAuthoredDeleteEmailDispositionReader
{
    /// <summary>Gets the disposition configured for one account's own deletions.</summary>
    /// <param name="accountId">The account whose mail is being deleted.</param>
    /// <returns>What becomes of the local copy once the server no longer holds the message.</returns>
    AuthoredDeleteEmailDisposition GetAuthoredDeleteDisposition(MailAccountId accountId);
}
