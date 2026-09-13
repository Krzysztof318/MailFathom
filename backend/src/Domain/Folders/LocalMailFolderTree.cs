// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>The live folders of one held account, and the rules every act on them is decided by.</summary>
/// <remarks>
/// <para>
/// Every held account has an inbox, a drafts folder, a sent folder, a junk folder, and a trash folder, and none of the
/// five can be renamed, moved, or deleted, because composing, sending, classifying, and deleting each need somewhere
/// to go that no earlier act can have taken away. Deleting any other folder moves it, with everything beneath it, into
/// the trash; deleting a folder already in the trash erases it. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// <para>
/// The tree decides and never writes. What an act changes comes back as a <see cref="LocalMailFolderEdit" />, so the
/// whole of a decision is testable without a store and a store applies what was decided rather than deciding again.
/// </para>
/// </remarks>
public sealed class LocalMailFolderTree
{
    /// <summary>The deepest level a folder may sit at, the top of the hierarchy being level one.</summary>
    public const int MaximumDepth = 16;

    /// <summary>The most live folders one account's hierarchy holds.</summary>
    public const int MaximumFolders = 1_000;

    private readonly Dictionary<LocalMailFolderId, LocalMailFolder> folders;
    private readonly HashSet<MailFolderAlias> erasedSourceAliases;

    /// <summary>Initializes a new instance of the <see cref="LocalMailFolderTree" /> class.</summary>
    /// <param name="folders">The account's live folders.</param>
    /// <param name="erasedSourceAliases">The source folders whose corresponding local folder has been erased.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public LocalMailFolderTree(IEnumerable<LocalMailFolder> folders, IEnumerable<MailFolderAlias> erasedSourceAliases)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(erasedSourceAliases);

        this.folders = folders.ToDictionary(static folder => folder.Id);
        this.erasedSourceAliases = [.. erasedSourceAliases];
    }

    /// <summary>Gets the roles every held account has a folder for.</summary>
    public static IReadOnlyList<MailFolderSpecialUse> ProtectedRoles { get; } =
    [
        MailFolderSpecialUse.Inbox,
        MailFolderSpecialUse.Drafts,
        MailFolderSpecialUse.Sent,
        MailFolderSpecialUse.Junk,
        MailFolderSpecialUse.Trash,
    ];

    /// <summary>Gets the live folders.</summary>
    public IReadOnlyCollection<LocalMailFolder> Folders => this.folders.Values;

    /// <summary>Finds a live folder.</summary>
    /// <param name="id">The folder's identity.</param>
    /// <returns>The folder, or <see langword="null" /> where the account has no live folder by that identity.</returns>
    public LocalMailFolder? Find(LocalMailFolderId id) => this.folders.GetValueOrDefault(id);

    /// <summary>States the protected folders the account lacks.</summary>
    /// <param name="mintId">Mints the identity of a folder this creates.</param>
    /// <returns>
    /// One folder per missing role: an ordinary top-level folder already carrying the role's name takes the role, and
    /// otherwise a new top-level folder is created for it.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mintId" /> is <see langword="null" />.</exception>
    public IReadOnlyList<LocalMailFolder> MissingProtectedFolders(Func<LocalMailFolderId> mintId)
    {
        ArgumentNullException.ThrowIfNull(mintId);

        return
        [
            .. ProtectedRoles
                .Where(role => this.folders.Values.All(folder => folder.Role != role))
                .Select(role => this.ProtectedFolderFor(role, mintId)),
        ];
    }

    /// <summary>Returns the tree with the given folders written over it.</summary>
    /// <param name="saved">The folders to write, each new or replacing the folder of the same identity.</param>
    /// <returns>The tree afterwards.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="saved" /> is <see langword="null" />.</exception>
    public LocalMailFolderTree With(IReadOnlyCollection<LocalMailFolder> saved)
    {
        ArgumentNullException.ThrowIfNull(saved);

        var savedIds = saved.Select(static folder => folder.Id).ToHashSet();

        return new LocalMailFolderTree(
            this.folders.Values.Where(folder => !savedIds.Contains(folder.Id)).Concat(saved),
            this.erasedSourceAliases);
    }

    /// <summary>Decides creating a folder.</summary>
    /// <param name="id">The new folder's identity.</param>
    /// <param name="parentId">The folder to create it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <param name="name">The name as supplied.</param>
    /// <returns>The folder to write, or the refusal.</returns>
    public LocalMailFolderEdit Create(LocalMailFolderId id, LocalMailFolderId? parentId, string? name)
    {
        if (this.folders.Count >= MaximumFolders)
        {
            return LocalMailFolderEdit.Refused(LocalMailFolderRefusal.TooManyFolders);
        }

        if (parentId is { } parent && !this.folders.ContainsKey(parent))
        {
            return LocalMailFolderEdit.Refused(LocalMailFolderRefusal.ParentMissing);
        }

        if (!LocalMailFolderName.TryCreate(name, out var parsed))
        {
            return LocalMailFolderEdit.Refused(LocalMailFolderRefusal.NameInvalid);
        }

        if (this.RefusePlacement(parsed, parentId, movingFolder: null) is { } refusal)
        {
            return LocalMailFolderEdit.Refused(refusal);
        }

        return this.LevelBeneath(parentId) > MaximumDepth
            ? LocalMailFolderEdit.Refused(LocalMailFolderRefusal.TooDeep)
            : LocalMailFolderEdit.Saving(new LocalMailFolder(id, parentId, parsed, Role: null, SourceFolderAlias: null));
    }

    /// <summary>Decides renaming a folder.</summary>
    /// <param name="id">The folder.</param>
    /// <param name="name">The new name as supplied.</param>
    /// <returns>The folder to write, or the refusal.</returns>
    public LocalMailFolderEdit Rename(LocalMailFolderId id, string? name)
    {
        if (this.RefuseActingOn(id) is { } refusal)
        {
            return LocalMailFolderEdit.Refused(refusal);
        }

        var folder = this.folders[id];

        if (!LocalMailFolderName.TryCreate(name, out var parsed))
        {
            return LocalMailFolderEdit.Refused(LocalMailFolderRefusal.NameInvalid);
        }

        return this.RefusePlacement(parsed, folder.ParentId, folder.Id) is { } placementRefusal
            ? LocalMailFolderEdit.Refused(placementRefusal)
            : LocalMailFolderEdit.Saving(folder with { Name = parsed });
    }

    /// <summary>Decides moving a folder, with everything beneath it, to another place in the hierarchy.</summary>
    /// <param name="id">The folder.</param>
    /// <param name="parentId">The folder to move it beneath, or <see langword="null" /> for the top of the hierarchy.</param>
    /// <returns>The folder to write, or the refusal.</returns>
    public LocalMailFolderEdit Move(LocalMailFolderId id, LocalMailFolderId? parentId)
    {
        if (this.RefuseActingOn(id) is { } refusal)
        {
            return LocalMailFolderEdit.Refused(refusal);
        }

        var folder = this.folders[id];

        if (parentId is { } parent)
        {
            if (!this.folders.ContainsKey(parent))
            {
                return LocalMailFolderEdit.Refused(LocalMailFolderRefusal.ParentMissing);
            }

            if (this.IsWithin(parent, id))
            {
                return LocalMailFolderEdit.Refused(LocalMailFolderRefusal.NestedInItself);
            }
        }

        if (this.RefusePlacement(folder.Name, parentId, folder.Id) is { } placementRefusal)
        {
            return LocalMailFolderEdit.Refused(placementRefusal);
        }

        return this.LevelBeneath(parentId) + this.HeightOf(id) - 1 > MaximumDepth
            ? LocalMailFolderEdit.Refused(LocalMailFolderRefusal.TooDeep)
            : LocalMailFolderEdit.Saving(folder with { ParentId = parentId });
    }

    /// <summary>Decides deleting a folder: into the trash, or out of existence where it is already there.</summary>
    /// <param name="id">The folder.</param>
    /// <returns>The folder moved into the trash, the subtree to erase, or the refusal.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the tree has no trash folder, which <see cref="MissingProtectedFolders" /> supplies.</exception>
    public LocalMailFolderEdit Delete(LocalMailFolderId id)
    {
        if (this.RefuseActingOn(id) is { } refusal)
        {
            return LocalMailFolderEdit.Refused(refusal);
        }

        var trash = this.RequireRole(MailFolderSpecialUse.Trash);

        return this.IsWithin(id, trash.Id)
            ? LocalMailFolderEdit.Erasing(this.folders[id], [.. this.SubtreeOf(id)])
            : this.Move(id, trash.Id);
    }

    /// <summary>Decides where a message arriving from a source folder goes.</summary>
    /// <param name="sourceAlias">The source folder's alias.</param>
    /// <param name="sourceRole">The role the source folder plays, or <see langword="null" /> where it plays none.</param>
    /// <param name="sourceFolderName">The source folder's own name, which a folder created for it takes where it can.</param>
    /// <param name="mintId">Mints the identity of a folder this creates.</param>
    /// <returns>The folder the message goes into, and the folder created for it if any.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mintId" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the tree lacks a protected folder, which <see cref="MissingProtectedFolders" /> supplies.</exception>
    /// <remarks>
    /// The source inbox, drafts, sent, junk, and trash go to the local folders playing those roles. Any other source
    /// folder corresponds to the local folder created for it the first time a message arrives from it, whatever that
    /// folder has been renamed or moved to since; where that folder has been deleted, arrivals go to the inbox. A folder
    /// created for a source takes the source's name, then its alias, and where neither can be a top-level name the
    /// message goes to the inbox rather than the arrival being refused.
    /// </remarks>
    public LocalMailFolderArrival PlaceArrival(
        MailFolderAlias sourceAlias,
        MailFolderSpecialUse? sourceRole,
        string? sourceFolderName,
        Func<LocalMailFolderId> mintId)
    {
        ArgumentNullException.ThrowIfNull(mintId);

        if (sourceRole is { } role && ProtectedRoles.Contains(role))
        {
            return new LocalMailFolderArrival(this.RequireRole(role).Id, []);
        }

        var inbox = new LocalMailFolderArrival(this.RequireRole(MailFolderSpecialUse.Inbox).Id, []);
        var trash = this.RequireRole(MailFolderSpecialUse.Trash);

        if (this.folders.Values.FirstOrDefault(folder => folder.SourceFolderAlias == sourceAlias) is { } corresponding)
        {
            return this.IsWithin(corresponding.Id, trash.Id)
                ? inbox
                : new LocalMailFolderArrival(corresponding.Id, []);
        }

        if (this.erasedSourceAliases.Contains(sourceAlias) || this.folders.Count >= MaximumFolders)
        {
            return inbox;
        }

        LocalMailFolderName? name = new[] { sourceFolderName, sourceAlias.Value }
            .Select(static candidate => LocalMailFolderName.TryCreate(candidate, out var parsed) ? parsed : (LocalMailFolderName?)null)
            .FirstOrDefault(candidate => candidate is { } parsed && this.RefusePlacement(parsed, parentId: null, movingFolder: null) is null);

        if (name is not { } folderName)
        {
            return inbox;
        }

        var created = new LocalMailFolder(mintId(), ParentId: null, folderName, Role: null, sourceAlias);

        return new LocalMailFolderArrival(created.Id, [created]);
    }

    private LocalMailFolder ProtectedFolderFor(MailFolderSpecialUse role, Func<LocalMailFolderId> mintId)
    {
        var name = role switch
        {
            MailFolderSpecialUse.Inbox => LocalMailFolderName.Inbox,
            _ => LocalMailFolderName.Create(role.ToString()),
        };

        var namesake = this.folders.Values.FirstOrDefault(folder =>
            folder.ParentId is null && folder.Role is null && folder.Name.NamesSameFolderAs(name));

        return namesake is null
            ? new LocalMailFolder(mintId(), ParentId: null, name, role, SourceFolderAlias: null)
            : namesake with { Role = role };
    }

    private LocalMailFolderRefusal? RefuseActingOn(LocalMailFolderId id) =>
        this.folders.GetValueOrDefault(id) switch
        {
            null => LocalMailFolderRefusal.FolderMissing,
            { IsProtected: true } => LocalMailFolderRefusal.ProtectedRole,
            _ => null,
        };

    private LocalMailFolderRefusal? RefusePlacement(
        LocalMailFolderName name,
        LocalMailFolderId? parentId,
        LocalMailFolderId? movingFolder)
    {
        if (parentId is null && name.IsReservedAtTopLevel)
        {
            return LocalMailFolderRefusal.InboxNameAtTopLevel;
        }

        return this.folders.Values.Any(folder =>
                folder.ParentId == parentId && folder.Id != movingFolder && folder.Name.NamesSameFolderAs(name))
            ? LocalMailFolderRefusal.NameTaken
            : null;
    }

    private LocalMailFolder RequireRole(MailFolderSpecialUse role) =>
        this.folders.Values.FirstOrDefault(folder => folder.Role == role)
        ?? throw new InvalidOperationException($"The hierarchy has no {role} folder, which every held account has before any act on it.");

    private int LevelBeneath(LocalMailFolderId? parentId) => this.AncestorsOf(parentId).Count() + 1;

    private bool IsWithin(LocalMailFolderId candidate, LocalMailFolderId ancestor) =>
        this.AncestorsOf(candidate).Contains(ancestor);

    /// <summary>Walks from a folder to the top, the folder itself first.</summary>
    /// <remarks>Bounded by the folder count, so rows that were ever written in a cycle end the walk rather than the process.</remarks>
    private IEnumerable<LocalMailFolderId> AncestorsOf(LocalMailFolderId? start)
    {
        var current = start;

        for (var step = 0; current is { } id && step <= this.folders.Count; step++)
        {
            yield return id;

            current = this.folders.GetValueOrDefault(id)?.ParentId;
        }
    }

    private int HeightOf(LocalMailFolderId id) =>
        1 + this.folders.Values
            .Where(folder => folder.ParentId == id)
            .Select(child => this.HeightOf(child.Id))
            .DefaultIfEmpty(0)
            .Max();

    private IEnumerable<LocalMailFolderId> SubtreeOf(LocalMailFolderId id) =>
        this.folders.Values
            .Where(folder => folder.ParentId == id)
            .SelectMany(child => this.SubtreeOf(child.Id))
            .Prepend(id);
}
