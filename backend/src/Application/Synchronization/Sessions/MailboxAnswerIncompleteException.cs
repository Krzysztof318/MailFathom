// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Sessions;

/// <summary>Indicates that a mail server answered for an email without the data items the command asked for.</summary>
/// <remarks>
/// <para>
/// IMAP requires a server to return every data item a <c>FETCH</c> named, so an answer that omits one is a protocol
/// violation rather than a state a caller can interpret. It is raised instead of interpreted because the interpretation
/// would be destructive: reconciliation reads the absence of an answer as an email the folder no longer holds, so an
/// answer that is present but incomplete must never be allowed to degrade into that silence.
/// </para>
/// <para>
/// It is distinct from <see cref="MailboxUnavailableException" />, which says the server did not answer at all within
/// its budget. Here the server answered and the answer cannot be trusted, so the run ends and no local state is derived
/// from it; the folder's next run asks again.
/// </para>
/// </remarks>
public sealed class MailboxAnswerIncompleteException : MailFathomException
{
    /// <summary>Initializes a new incomplete-answer failure naming the account, the folder alias, and the missing item.</summary>
    /// <param name="accountId">The account whose mail server answered incompletely.</param>
    /// <param name="folderAlias">The folder the operation was working on.</param>
    /// <param name="missingDataItem">The requested data item the answer omitted, named as the protocol names it.</param>
    /// <remarks>
    /// The alias is named rather than the remote path, and no UID or message data appears, because this message is
    /// logged. The item name is the protocol's own and carries nothing derived from a message.
    /// </remarks>
    public MailboxAnswerIncompleteException(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        string missingDataItem)
        : base(
            $"The mail server for {accountId.Value}/{folderAlias.Value} answered for an email without the requested {missingDataItem} data item, so the answer was discarded rather than acted on.")
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
        this.MissingDataItem = missingDataItem;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxAnswerIncomplete;

    /// <summary>Gets the account whose mail server answered incompletely.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the folder the stopped operation was working on.</summary>
    public MailFolderAlias FolderAlias { get; }

    /// <summary>Gets the requested data item the answer omitted.</summary>
    public string MissingDataItem { get; }
}
