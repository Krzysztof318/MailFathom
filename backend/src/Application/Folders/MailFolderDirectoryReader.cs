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

/// <summary>Reads the user's mailboxes and their folders as the one tree a mail screen is drawn from.</summary>
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
/// The folders are the ones configuration maps, which is a wider answer than the composed reading gives: that reading
/// is of local state, and a folder an operator asked not to mirror is never scheduled, so no run ever discovers it. It
/// is still a folder the account has and still a folder MailFathom files into — resolution is indifferent to
/// mirroring — so it is published beside the mirrored ones as never synchronized, at the place its declaration names
/// and with no mail here. A client that could not see it would be told an account has nowhere to put a deleted message
/// while its mailbox has a trash folder.
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

    /// <summary>Reads the user's mailboxes and every folder a screen may draw beneath them.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>One entry per account the caller's user is assigned, each with its folders, and whether the deployment refreshes them.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the use case was reached by anything but a caller granted <see cref="MailFathomPermission.MailRead" /> that is acting for a user.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    /// <remarks>
    /// The grant and the user bound are the composed reading's, taken before anything here runs, so a user assigned
    /// no account reads an empty tree and a caller without the permission is refused — the same two answers the mailbox
    /// list gives, because naming a user's folders is the same disclosure as naming their mailboxes.
    /// </remarks>
    public async Task<MailFolderDirectory> ReadAsync(CancellationToken cancellationToken)
    {
        var accounts = await this.accountReader.ReadAsync(cancellationToken);

        if (accounts.Accounts.Count is 0)
        {
            return new MailFolderDirectory(accounts.SynchronizationEnabled, []);
        }

        // The scope the composed reading resolved, asked for again rather than passed down: it is read again from the
        // account records, so it matches the folders that reading reported freshness for unless an account write
        // commits between the two reads. Junk is included for the reason the mailbox list includes it — the
        // withholding is about not returning its mail unasked, and no mail is returned here.
        var scope = await this.scopeResolver.ReadableScopeAsync([], [], JunkMailInclusion.Included, cancellationToken);

        var stored = await this.storedFolders.ReadAsync(scope, cancellationToken);
        var storedByFolder = stored.ToDictionary(static folder => folder.Folder);

        return new MailFolderDirectory(
            accounts.SynchronizationEnabled,
            [.. accounts.Accounts.Select(account => this.Describe(account, storedByFolder))]);
    }

    /// <summary>Describes one account's folders, in the order the composed reading answered them, and the mapped folders it never reached.</summary>
    /// <remarks>
    /// A folder an operator mapped and asked not to mirror is never scheduled, so no run discovers it and the composed
    /// reading — which is of local state — does not name it. It is still a folder the account has and still a folder
    /// MailFathom files into, resolution being indifferent to mirroring, so leaving it out would answer a client asking
    /// <em>where does a deleted message go</em> with <em>nowhere</em> for a mailbox that has a trash folder. It is
    /// therefore published beside the mirrored ones, as what it is: never synchronized, at the place its declaration
    /// names, and no mail here.
    /// <para>
    /// A mirrored mapping no run has reached yet is added for the same reason and is the commoner case: a folder
    /// somebody has just declared takes part in everything by default, so no run has discovered it and local state
    /// names it nowhere — and a client that could not see it would report the folder as created and then draw a tree
    /// without it until the account's next pass, which is minutes.
    /// </para>
    /// <para>
    /// What is still left out is the one mapping this could otherwise undo a withholding for: a folder withheld from
    /// tools is mirrored and holds mail, and the resolved scope left it out of the composed reading deliberately, so
    /// publishing it here would name a folder every other read refuses. A mapping absent from that reading is
    /// therefore added unless it is both mirrored and withheld, which is exactly that case and no other.
    /// </para>
    /// </remarks>
    private MailAccountFolders Describe(
        MailAccountFreshness account,
        IReadOnlyDictionary<MailFolderIdentity, StoredMailFolder> storedByFolder)
    {
        var delimiter = HierarchyDelimiterOf(account.Account.Id, storedByFolder);

        return new(
            account,
            [
                .. account.Folders.Select(folder =>
                    this.Describe(account.Account.Id, folder, storedByFolder, delimiter)),
                .. this.UnreachedFolders(account, delimiter),
            ]);
    }

    /// <summary>Describes the folders configuration maps for an account that no run has ever reached.</summary>
    private IEnumerable<DescribedMailFolder> UnreachedFolders(MailAccountFreshness account, char? delimiter)
    {
        var reached = account.Folders.Select(static folder => folder.Alias).ToHashSet();

        return this.folderMappings
            .FoldersOf(account.Account.Id)
            .Where(mapping => !reached.Contains(mapping.Alias) && !IsWithheldFromTools(mapping))
            .OrderBy(static mapping => mapping.Alias.Value, StringComparer.Ordinal)
            .Select(mapping => new DescribedMailFolder(
                new MailFolderFreshness(mapping.Alias, MailSynchronizationState.NeverSynchronized, null, false),
                mapping.SpecialUse,
                HierarchyOf(mapping, stored: null, delimiter),
                0,
                0));
    }

    /// <summary>Reports whether the mapping names a folder the resolved scope left out on purpose rather than one no run has reached.</summary>
    /// <remarks>Only a mirrored folder can hold mail to withhold, which is why both halves are asked: an unmirrored folder is invisible to tools by construction and has nothing to refuse.</remarks>
    private static bool IsWithheldFromTools(MailFolderMapping mapping) =>
        mapping.Participation.IsSynchronized && !mapping.Participation.IsVisibleToTools;

    /// <summary>Describes one folder, with what local state holds about it where local state holds anything.</summary>
    /// <remarks>
    /// A folder whose alias has a binding but no mail reads as zero of both counts, and one whose alias has no binding
    /// at all reads as zero. The two are separable through the folder's own freshness rather than through a count that
    /// is absent instead of nought, because "how much is here" and "has anything ever arrived" are the questions the
    /// state and the instant already answer.
    /// </remarks>
    private DescribedMailFolder Describe(
        MailAccountId accountId,
        MailFolderFreshness folder,
        IReadOnlyDictionary<MailFolderIdentity, StoredMailFolder> storedByFolder,
        char? delimiter)
    {
        var stored = storedByFolder.GetValueOrDefault(new MailFolderIdentity(accountId, folder.Alias));
        var mapping = this.folderMappings.FindFolderNamed(accountId, folder.Alias);

        return new DescribedMailFolder(
            folder,
            mapping?.SpecialUse,
            HierarchyOf(mapping, stored, delimiter),
            stored?.StoredEmailCount ?? 0,
            stored?.UnreadEmailCount ?? 0);
    }

    /// <summary>Reads where a folder sits, from the path its declaration names wherever it names one.</summary>
    /// <remarks>
    /// <para>
    /// The declaration rather than the binding, because a folder act writes the declaration and nothing else: a folder
    /// just made has no binding at all, and one just renamed or moved keeps the binding of the path it left until the
    /// account's next pass reaches it — minutes, during which the act's own <c>FoldersChanged</c> re-read would draw the
    /// folder where it no longer is. The binding is read only for a folder found by the role it plays, whose declaration
    /// names no path.
    /// </para>
    /// <para>
    /// A declared path is the text somebody wrote, which carries no hierarchy delimiter, so it is split by the delimiter
    /// the folder's own binding recorded and otherwise by the one the account's other bindings recorded — one server's
    /// namespace nests every folder with the same character. An account no pass has reached yet has recorded none, and
    /// its declared paths read as one level each until one does.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> HierarchyOf(
        MailFolderMapping? mapping,
        StoredMailFolder? stored,
        char? delimiter)
    {
        if (mapping?.RemotePath is not { } declared)
        {
            return stored?.RemotePath.ToHierarchyLevels() ?? [];
        }

        var nesting = stored?.RemotePath.HierarchyDelimiter ?? delimiter;

        return RemoteFolderPath.TryCreate(declared.Value, nesting, out var placed)
            ? placed.ToHierarchyLevels()
            : [declared.Value];
    }

    /// <summary>Reads the hierarchy delimiter the account's bindings recorded, or nothing where none recorded one.</summary>
    private static char? HierarchyDelimiterOf(
        MailAccountId accountId,
        IReadOnlyDictionary<MailFolderIdentity, StoredMailFolder> storedByFolder) =>
        storedByFolder.Values
            .Where(folder => folder.Folder.AccountId == accountId)
            .Select(static folder => folder.RemotePath.HierarchyDelimiter)
            .FirstOrDefault(static delimiter => delimiter is not null);
}
