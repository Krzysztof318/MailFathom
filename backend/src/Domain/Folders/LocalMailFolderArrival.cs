// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Where a message arriving from a source folder is placed in a held account's hierarchy.</summary>
/// <param name="Folder">The local folder the message goes into.</param>
/// <param name="Saved">The folder placing it created, which is empty where the folder already existed.</param>
public sealed record LocalMailFolderArrival(LocalMailFolderId Folder, IReadOnlyList<LocalMailFolder> Saved);
