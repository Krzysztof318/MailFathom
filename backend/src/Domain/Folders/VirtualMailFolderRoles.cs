// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names the roles whose folder is a view a server presents rather than a place it keeps messages.</summary>
/// <remarks>
/// RFC 6154 defines <c>\All</c> and <c>\Flagged</c> and RFC 8457 defines <c>\Important</c>, and each of the three
/// presents messages that are occurrences of other folders. That makes them the roles an account holding its own
/// mailbox refuses to synchronize: storing such a folder would keep every message it presents a second time, and
/// draining it would take mail out of folders nobody asked about. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </remarks>
public static class VirtualMailFolderRoles
{
    /// <summary>Gets the roles whose folder presents other folders' messages.</summary>
    public static IReadOnlyList<MailFolderSpecialUse> All { get; } =
    [
        MailFolderSpecialUse.All,
        MailFolderSpecialUse.Flagged,
        MailFolderSpecialUse.Important,
    ];

    /// <summary>Reports whether a role names such a view.</summary>
    /// <param name="role">The role to judge, or <see langword="null" /> for a folder configuration gives none.</param>
    /// <returns><see langword="true" /> when the role presents messages held elsewhere.</returns>
    public static bool Includes(MailFolderSpecialUse? role) => role is { } named && All.Contains(named);
}
