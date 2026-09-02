// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Reads the owner's mailboxes and their folders as the one tree a mail screen is drawn from.</summary>
/// <remarks>
/// <para>
/// It answers in one read what a screen would otherwise assemble from three: which mailboxes there are, which folders
/// each of them has and where those sit in the server's hierarchy, and how current every one of them is. Splitting any
/// of that off would make drawing one tree several requests whose answers disagree with each other by the time the last
/// one arrives — which is the shape this surface exists not to have.
/// </para>
/// <para>
/// The accounts and every freshness reading are <see cref="MailAccountFreshnessReader" />'s, composed rather than
/// re-derived, so the folder tree and the mailbox list beside it cannot come to disagree about the same account. What
/// is added here is what that reading has no reason to carry: the role each folder plays, its place in the mail
/// server's hierarchy, and how much mail is stored in it.
/// </para>
/// <para>
/// The folders are the ones local state knows of, which is what the composed reading already names. A folder an
/// operator mapped that nothing has ever bound to a remote folder is absent rather than empty: there is no folder on
/// the server to draw, and a client showing one would be showing a mailbox that is not there. Where an operator finds
/// out about such a mapping is the administrative status surface, which composes its folder list from configuration for
/// exactly that reason.
/// </para>
/// <para>
/// It reaches no mail server and returns no mail. Folder names, roles, counts, and instants are the whole of it, and
/// asking cannot set the remote <c>\Seen</c> flag.
/// </para>
/// </remarks>
public sealed class MailFolderDirectoryReader
{
    private readonly MailAccountFreshnessReader accountReader;
    private readonly MailboxScopeResolver scopeResolver;
    private readonly IStoredMailFolderReader storedFolders;
    private readonly IMailFolderMappingReader folderMappings;

    /// <summary>Initializes the use case.</summary>
    /// <param name="accountReader">Reads the caller's accounts and how current each one and each of its folders is.</param>
    /// <param name="scopeResolver">Answers which of those accounts' folders may be reported on.</param>
    /// <param name="storedFolders">Reads where each folder sits on its mail server and how much of it is stored.</param>
    /// <param name="folderMappings">Answers which role configuration labelled each folder with.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailFolderDirectoryReader(
        MailAccountFreshnessReader accountReader,
        MailboxScopeResolver scopeResolver,
        IStoredMailFolderReader storedFolders,
        IMailFolderMappingReader folderMappings)
    {
        ArgumentNullException.ThrowIfNull(accountReader);
        ArgumentNullException.ThrowIfNull(scopeResolver);
        ArgumentNullException.ThrowIfNull(storedFolders);
        ArgumentNullException.ThrowIfNull(folderMappings);

        this.accountReader = accountReader;
        this.scopeResolver = scopeResolver;
        this.storedFolders = storedFolders;
        this.folderMappings = folderMappings;
    }

    /// <summary>Reads the owner's mailboxes and every folder a screen may draw beneath them.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per account the caller's owner owns, each with its folders, and whether the deployment refreshes them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" /> that is acting for an owner.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The grant and the owner bound are the composed reading's, taken before anything here runs, so an owner who owns
    /// no account reads an empty tree and a caller without the permission is refused — the same two answers the mailbox
    /// list gives, because naming an owner's folders is the same disclosure as naming their mailboxes.
    /// </remarks>
    public async Task<MailFolderDirectory> ReadAsync(CancellationToken cancellationToken)
    {
        var accounts = await this.accountReader.ReadAsync(cancellationToken);

        if (accounts.Accounts.Count is 0)
        {
            return new MailFolderDirectory(accounts.SynchronizationEnabled, []);
        }

        // The same scope the composed reading resolved, asked for again rather than passed down: it is configuration
        // read in process, and resolving it here is what keeps the folders this counts mail in identical to the folders
        // that reading reported freshness for. Junk is included for the reason the mailbox list includes it — the
        // withholding is about not returning its mail unasked, and no mail is returned here.
        var scope = this.scopeResolver.ReadableScope([], [], JunkMailInclusion.Included);

        var stored = await this.storedFolders.ReadAsync(scope, cancellationToken);
        var storedByFolder = stored.ToDictionary(static folder => folder.Folder);

        return new MailFolderDirectory(
            accounts.SynchronizationEnabled,
            [.. accounts.Accounts.Select(account => this.Describe(account, storedByFolder))]);
    }

    /// <summary>Describes one account's folders, in the order the composed reading answered them.</summary>
    private MailAccountFolders Describe(
        MailAccountFreshness account,
        IReadOnlyDictionary<MailFolderIdentity, StoredMailFolder> storedByFolder) =>
        new(
            account,
            [.. account.Folders.Select(folder => this.Describe(account.Account.Id, folder, storedByFolder))]);

    /// <summary>Describes one folder, with what local state holds about it where local state holds anything.</summary>
    /// <remarks>
    /// A folder whose alias has a binding but no mail reads as zero of both counts, and one whose alias has no binding
    /// at all reads as zero and no hierarchy. The two are separable through the folder's own freshness rather than
    /// through a count that is absent instead of nought, because "how much is here" and "has anything ever arrived" are
    /// the questions the state and the instant already answer.
    /// </remarks>
    private DescribedMailFolder Describe(
        MailAccountId accountId,
        MailFolderFreshness folder,
        IReadOnlyDictionary<MailFolderIdentity, StoredMailFolder> storedByFolder)
    {
        var stored = storedByFolder.GetValueOrDefault(new MailFolderIdentity(accountId, folder.Alias));

        return new DescribedMailFolder(
            folder,
            this.folderMappings.FindFolderNamed(accountId, folder.Alias)?.SpecialUse,
            stored?.RemotePath.ToHierarchyLevels() ?? [],
            stored?.StoredEmailCount ?? 0,
            stored?.UnreadEmailCount ?? 0);
    }
}
