// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Failures;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Synchronization;

/// <summary>Indicates that a mailbox session was re-established onto a folder the server has recreated since it opened.</summary>
/// <remarks>
/// A recovered connection re-selects the folder, and the server is free to answer with a new UIDVALIDITY, which means
/// every UID the session has handed out so far names a different email than the same number does now. Continuing would
/// attach the recovered folder's emails to the previous folder's checkpoint, so the run stops instead. Nothing is lost:
/// the next run reads the new UIDVALIDITY from an empty checkpoint and re-synchronizes the folder from its start.
/// </remarks>
public sealed class MailboxFolderRecreatedException : MailMcpException
{
    /// <summary>Initializes a new recreated-folder failure naming both observed UIDVALIDITY values.</summary>
    /// <param name="accountId">The account whose folder was re-selected.</param>
    /// <param name="folderAlias">The folder that was re-selected.</param>
    /// <param name="sessionUidValidity">The UIDVALIDITY the session opened with.</param>
    /// <param name="reselectedUidValidity">The UIDVALIDITY the server answered with after the connection recovered.</param>
    public MailboxFolderRecreatedException(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        ImapUidValidity sessionUidValidity,
        ImapUidValidity reselectedUidValidity)
        : base(
            $"Folder {accountId.Value}/{folderAlias.Value} was reselected with UIDVALIDITY {reselectedUidValidity.Value} after the session opened with {sessionUidValidity.Value}, so the identities this session handed out no longer name the same emails.")
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
        this.SessionUidValidity = sessionUidValidity;
        this.ReselectedUidValidity = reselectedUidValidity;
    }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.MailboxFolderRecreated;

    /// <summary>Gets the account whose folder was recreated.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the folder that was recreated.</summary>
    public MailFolderAlias FolderAlias { get; }

    /// <summary>Gets the UIDVALIDITY the session opened with.</summary>
    public ImapUidValidity SessionUidValidity { get; }

    /// <summary>Gets the UIDVALIDITY observed after the connection recovered.</summary>
    public ImapUidValidity ReselectedUidValidity { get; }
}
