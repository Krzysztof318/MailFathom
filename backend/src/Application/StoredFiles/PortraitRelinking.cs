// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.StoredFiles;

/// <summary>What linking a record to a portrait did.</summary>
/// <param name="UserHeld">Whether this deployment holds the user at all; nothing was written when it does not.</param>
/// <param name="Replaced">The file the record linked as its portrait before the write, which the caller removes.</param>
public sealed record PortraitRelinking(bool UserHeld, StoredFileId? Replaced)
{
    /// <summary>Gets the answer for a user this deployment does not hold.</summary>
    public static PortraitRelinking NoSuchUser { get; } = new(UserHeld: false, Replaced: null);
}
