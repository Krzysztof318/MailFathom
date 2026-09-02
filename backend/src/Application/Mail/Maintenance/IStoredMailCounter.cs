// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Counts the mail a deployment holds for one maintenance scope.</summary>
/// <remarks>
/// It exists so that an operation whose cost is proportional to the mailbox can state that cost before it is agreed to,
/// which is the same contract <c>mfctl embedding activate</c> already holds an operator to. The figure is a count of
/// messages and nothing else: what a maintenance command is about to spend is proportional to how many messages it will
/// re-read, and a byte total would describe local storage rather than the work.
/// </remarks>
public interface IStoredMailCounter
{
    /// <summary>Counts the stored emails one scope holds.</summary>
    /// <param name="scope">The account, and the one folder of it, whose mail is counted.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>How many stored emails the scope holds.</returns>
    /// <remarks>
    /// A row a tombstone hides is left out, which is the same rule every reader of stored mail applies: neither
    /// maintenance command spends work on mail nothing may retrieve, so counting one would overstate what the operator
    /// is agreeing to.
    /// </remarks>
    Task<int> CountStoredEmailsAsync(StoredMailScope scope, CancellationToken cancellationToken);
}
