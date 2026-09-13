// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>What one act on a local folder hierarchy changes, or why it changes nothing.</summary>
public sealed record LocalMailFolderEdit
{
    private LocalMailFolderEdit()
    {
    }

    /// <summary>Gets the folder the act was about, as it stands afterwards, or <see langword="null" /> where the act was refused.</summary>
    public LocalMailFolder? Folder { get; private init; }

    /// <summary>Gets the folders to write, each either new or replacing the row of the same identity.</summary>
    public IReadOnlyList<LocalMailFolder> Saved { get; private init; } = [];

    /// <summary>Gets the folders the act erases, which is the subject and everything beneath it.</summary>
    public IReadOnlyList<LocalMailFolderId> Erased { get; private init; } = [];

    /// <summary>Gets why the act was refused, or <see langword="null" /> where it was not.</summary>
    public LocalMailFolderRefusal? Refusal { get; private init; }

    /// <summary>States a refusal.</summary>
    /// <param name="refusal">Why the act was refused.</param>
    /// <returns>An edit that changes nothing.</returns>
    public static LocalMailFolderEdit Refused(LocalMailFolderRefusal refusal) => new() { Refusal = refusal };

    internal static LocalMailFolderEdit Saving(LocalMailFolder folder) => new() { Folder = folder, Saved = [folder] };

    internal static LocalMailFolderEdit Erasing(LocalMailFolder folder, IReadOnlyList<LocalMailFolderId> subtree) =>
        new() { Folder = folder, Erased = subtree };
}
