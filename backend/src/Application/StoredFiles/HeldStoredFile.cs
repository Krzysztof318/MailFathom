// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.StoredFiles;

/// <summary>One stored file and the user it belongs to, which is what the sweep decides about.</summary>
/// <param name="File">The file.</param>
/// <param name="Owner">The user whose record would link to it.</param>
public sealed record HeldStoredFile(StoredFileId File, MailUserId Owner);
