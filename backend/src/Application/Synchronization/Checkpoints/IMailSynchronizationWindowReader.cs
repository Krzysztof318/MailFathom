// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Application.Synchronization.Checkpoints;

/// <summary>Resolves how far back synchronization may reach for one configured mail account.</summary>
/// <remarks>
/// The window is read once per run and handed to the mailbox session as an input, for the same reason the transport
/// security policy is: an adapter may narrow what it is given and must never decide for itself which emails a run is
/// allowed to see. An account that configured no bound reads as
/// <see cref="MailSynchronizationWindow.Unbounded" /> rather than as an absent answer.
/// </remarks>
public interface IMailSynchronizationWindowReader
{
    /// <summary>Gets the configured synchronization window for an account.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The account's window.</returns>
    MailSynchronizationWindow GetWindow(MailAccountId accountId);
}
