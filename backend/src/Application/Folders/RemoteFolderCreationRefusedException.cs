// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Indicates that a mail server was asked to create the folder a mapping named and answered by refusing it.</summary>
/// <remarks>
/// <para>
/// It is raised rather than returned, and it carries a code of its own, because the operator's remedy depends on being
/// able to tell it apart from the alias that resolves to nothing. A quota reached, a namespace that forbids creating
/// there, and a name the server will not accept are each something to act on; a folder nobody has is a path to correct.
/// Reporting the first as the second would leave an operator reading the message a typo produces about a path they
/// wrote correctly.
/// </para>
/// <para>
/// The message names the alias alone. The remote path is the mailbox owner's own naming of their mail, which no message
/// an operator reads may carry — it belongs to the mapping-change audit record and to the debug detail.
/// </para>
/// </remarks>
public sealed class RemoteFolderCreationRefusedException : MailFathomException
{
    /// <summary>Initializes a new refusal naming the alias whose folder the mail server would not create.</summary>
    /// <param name="accountId">The account whose mailbox the folder was to be created in.</param>
    /// <param name="folderAlias">The alias the configured path was written under.</param>
    /// <param name="innerException">The mail-library failure the server's refusal arrived as.</param>
    public RemoteFolderCreationRefusedException(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        Exception innerException)
        : base(DescribeRefusedCreation(accountId, folderAlias), innerException)
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
    }

    /// <summary>Initializes a new refusal for a path the server holds under a name it will not let a folder take.</summary>
    /// <param name="accountId">The account whose mailbox the folder was to be created in.</param>
    /// <param name="folderAlias">The alias the configured path was written under.</param>
    /// <remarks>
    /// This is the answer to a name the server already lists as a hierarchy container or as a node holding no mail. The
    /// server refused nothing, because nothing was asked of it: the name is taken, so what the operator needs is a
    /// different path rather than an act MailFathom can take for them.
    /// </remarks>
    public RemoteFolderCreationRefusedException(MailAccountId accountId, MailFolderAlias folderAlias)
        : base(DescribeRefusedCreation(accountId, folderAlias))
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.RemoteFolderCreationRefused;

    /// <summary>Gets the account whose mailbox the folder was to be created in.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the alias the configured path was written under.</summary>
    public MailFolderAlias FolderAlias { get; }

    private static string DescribeRefusedCreation(MailAccountId accountId, MailFolderAlias folderAlias) =>
        $"The mail server for {accountId.Value}/{folderAlias.Value} will not hold a folder at the path that alias "
        + "is configured with, so the folder was not created.";
}
