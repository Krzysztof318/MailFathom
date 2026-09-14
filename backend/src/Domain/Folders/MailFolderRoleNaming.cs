// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Says which roles a folder may be created for, and what such a folder is called.</summary>
/// <remarks>
/// <para>
/// A folder made for a role is never named by the person asking for it: they choose the role and this supplies the
/// name, which is what stops a client from asking anybody to name their own trash folder and what makes the name the
/// same on every account of every deployment. The names are the roles' own English words, which is deliberate rather
/// than a gap in translation — a name here is an identifier a mail server and other mail clients read, and the name a
/// person sees is theirs to draw from the role.
/// </para>
/// <para>
/// The set is narrower than <see cref="MailFolderSpecialUse" /> by three kinds. An inbox is never created, because
/// RFC 3501 has every server hold one already. <see cref="MailFolderSpecialUse.All" />,
/// <see cref="MailFolderSpecialUse.Flagged" />, and <see cref="MailFolderSpecialUse.Important" /> are views a server
/// presents rather than folders anybody makes. <see cref="MailFolderSpecialUse.Outbox" /> is MailFathom's own label
/// with no attribute behind it, so a server would have nothing to be told and nothing to advertise afterwards.
/// </para>
/// </remarks>
public static class MailFolderRoleNaming
{
    /// <summary>Gets the roles a folder may be created for, in the order a client may offer them.</summary>
    public static IReadOnlyList<MailFolderSpecialUse> Creatable { get; } =
    [
        MailFolderSpecialUse.Archive,
        MailFolderSpecialUse.Drafts,
        MailFolderSpecialUse.Sent,
        MailFolderSpecialUse.Junk,
        MailFolderSpecialUse.Trash,
    ];

    /// <summary>Gets the name a folder created for a role is given.</summary>
    /// <param name="role">The role the folder is to play.</param>
    /// <returns>The role's standard English name.</returns>
    /// <remarks>The inbox answers <c>INBOX</c>, which is the one folder name RFC 3501 fixes and the one this never invents.</remarks>
    public static string StandardNameOf(MailFolderSpecialUse role) =>
        role is MailFolderSpecialUse.Inbox ? "INBOX" : role.ToString();
}
