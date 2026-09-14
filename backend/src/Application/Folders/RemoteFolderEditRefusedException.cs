// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Indicates that a mail server was asked to rename or delete a folder and answered by refusing it.</summary>
/// <remarks>
/// <para>
/// It is raised rather than returned for the reason a refused creation is: the act reaches the server from an adapter
/// that has no way to decide what a refusal means to whoever asked, and the caller translating it into the person's
/// own answer is several layers above. What that caller does with it is settled in one place — the act is reported as
/// refused, and nothing of MailFathom's own state is written, because the server is asked first.
/// </para>
/// <para>
/// The message names the alias alone. A remote folder path is the mailbox user's own naming of their mail, which no
/// message an operator reads may carry.
/// </para>
/// </remarks>
public sealed class RemoteFolderEditRefusedException : MailFathomException
{
    /// <summary>Initializes a new refusal naming the alias whose folder the mail server would not change.</summary>
    /// <param name="accountId">The account whose mailbox holds the folder.</param>
    /// <param name="folderAlias">The alias the folder is declared under.</param>
    /// <param name="act">Which act the server refused.</param>
    /// <param name="innerException">The mail-library failure the server's refusal arrived as.</param>
    public RemoteFolderEditRefusedException(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        MailFolderAct act,
        Exception innerException)
        : base(DescribeRefusedEdit(accountId, folderAlias, act), innerException)
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
        this.Act = act;
    }

    /// <summary>Initializes a new refusal for an act the server was not asked to perform because its subject is unusable.</summary>
    /// <param name="accountId">The account whose mailbox holds the folder.</param>
    /// <param name="folderAlias">The alias the folder is declared under.</param>
    /// <param name="act">Which act was refused.</param>
    /// <remarks>
    /// The server refused nothing, because nothing was asked of it: the destination name is taken, or the folder the
    /// act names is a hierarchy container the server holds no mail in. What the person needs is a different name rather
    /// than an act MailFathom can take for them.
    /// </remarks>
    public RemoteFolderEditRefusedException(MailAccountId accountId, MailFolderAlias folderAlias, MailFolderAct act)
        : base(DescribeRefusedEdit(accountId, folderAlias, act))
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
        this.Act = act;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.RemoteFolderEditRefused;

    /// <summary>Gets the account whose mailbox holds the folder.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the alias the folder is declared under.</summary>
    public MailFolderAlias FolderAlias { get; }

    /// <summary>Gets which act the server would not carry out.</summary>
    public MailFolderAct Act { get; }

    /// <summary>Names the act in the sentence's own words rather than by lower-casing its name, which no culture is asked to do.</summary>
    private static string DescribeRefusedEdit(MailAccountId accountId, MailFolderAlias folderAlias, MailFolderAct act)
    {
        var refused = act switch
        {
            MailFolderAct.Create => "create",
            MailFolderAct.Rename => "rename",
            MailFolderAct.Move => "move",
            MailFolderAct.Delete => "delete",
            _ => "change",
        };

        return $"The mail server for {accountId.Value}/{folderAlias.Value} refused to {refused} that folder, "
            + "so nothing was changed.";
    }
}
