// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Removes the local copy of a folder MailFathom has stopped mirroring.</summary>
/// <remarks>
/// This is the one write that exists because somebody asked for it rather than because a mail server said something. It
/// is separate from the reconciliation store for that reason: nothing here is an observation, and the rows it removes
/// are removed whatever the server still holds.
/// </remarks>
public interface IStoredMailFolderMirrorStore
{
    /// <summary>Erases a bounded part of one folder's stored mail, and its checkpoint once nothing is left.</summary>
    /// <param name="session">The write transaction this erasure joins.</param>
    /// <param name="account">The account the folder belongs to, by its generated identifier.</param>
    /// <param name="folderAlias">MailFathom's own name for the folder.</param>
    /// <param name="folderHoldsItsOwnMessages">Whether the source keeps messages in this folder rather than presenting other folders' messages.</param>
    /// <param name="maxEmails">The greatest number of emails this pass may erase.</param>
    /// <param name="cancellationToken">Cancels the read that selects the emails and the writes that stage their removal.</param>
    /// <returns>What this pass erased, and whether the folder still holds stored mail.</returns>
    /// <remarks>
    /// <para>
    /// The checkpoint is cleared in the same pass that empties the folder rather than in the first one, because a
    /// checkpoint removed while mail remains would make a mirrored folder resume in front of rows this pass has not
    /// reached. Clearing it at the end is what makes a folder somebody erased mirror afresh, while a folder merely
    /// switched off keeps its checkpoint and resumes from it.
    /// </para>
    /// <para>
    /// A folder playing a virtual role presents occurrences of folders the source keeps elsewhere, so erasing what
    /// MailFathom stored of one says nothing about what that server should be emptied of. The caller answers that
    /// rather than this port, because what the folder is for is the account's mapping to say and a mapping may be gone
    /// while its rows are not; a caller that cannot establish it answers <see langword="false" /> and the erasure asks
    /// the source for nothing.
    /// </para>
    /// </remarks>
    Task<MailFolderMirrorErasure> EraseFolderMirrorAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderAlias folderAlias,
        bool folderHoldsItsOwnMessages,
        int maxEmails,
        CancellationToken cancellationToken);
}
