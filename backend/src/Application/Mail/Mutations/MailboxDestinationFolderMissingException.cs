// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Indicates that the folder a relocation or a copy names as its destination does not exist on the server.</summary>
/// <remarks>
/// <para>
/// It is raised rather than returned, and it is terminal on its first occurrence, because it says the same thing about
/// repeating the work as <see cref="MailboxMutationUnsupportedException" /> does: a folder the server does not have will
/// not be there on the next run either. Somebody deleted or renamed it, or whatever asked for the change named a path
/// that was never right, and both remedies are an operator's rather than a wait's. Letting it spend the mutation's
/// attempt bound instead would cost a login and a round trip per attempt to be told the same thing, and would leave the
/// operator reading a change that merely looks busy.
/// </para>
/// <para>
/// It is separate from <see cref="Synchronization.Sessions.MailboxUnavailableException" /> for that reason alone. A mail
/// server that did not answer is expected to answer later; one that answered and said the folder is not there has
/// answered.
/// </para>
/// <para>
/// The message names the account alias, the folder alias, and the mutation. The destination path is deliberately absent:
/// a remote folder path is the mailbox owner's own naming of their mail, which no message an operator reads may carry.
/// </para>
/// </remarks>
public sealed class MailboxDestinationFolderMissingException : MailboxMutationRefusedException
{
    /// <summary>Initializes a new refusal naming the mutation whose destination folder the server does not have.</summary>
    /// <param name="accountId">The account the mutation was requested for.</param>
    /// <param name="folderAlias">The folder the email is in, which is the one MailFathom has a name of its own for.</param>
    /// <param name="mutation">The mutation that was asked for.</param>
    /// <param name="innerException">The mail-library failure that reported the folder as absent.</param>
    public MailboxDestinationFolderMissingException(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        MailboxMutation mutation,
        Exception innerException)
        : base(DescribeMissingDestination(accountId, folderAlias, mutation), innerException)
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
        this.Mutation = mutation;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxMutationDestinationMissing;

    /// <summary>Gets the account the mutation was requested for.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the folder the email is in.</summary>
    public MailFolderAlias FolderAlias { get; }

    /// <summary>Gets the mutation that was asked for.</summary>
    public MailboxMutation Mutation { get; }

    private static string DescribeMissingDestination(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        MailboxMutation mutation) =>
        $"The mail server for {accountId.Value}/{folderAlias.Value} holds no folder at the destination a "
        + $"{mutation.Name} named, so the change was given up on rather than attempted again.";
}
