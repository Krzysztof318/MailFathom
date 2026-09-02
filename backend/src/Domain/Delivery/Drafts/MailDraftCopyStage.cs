// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Drafts;

/// <summary>States what has become of one copy of a draft that MailFathom appended to the drafts folder.</summary>
/// <remarks>
/// <para>
/// It is a stage of its own rather than the one a filed outgoing copy uses, because a draft copy can end in a way a
/// filed copy cannot. A sent copy is appended once and kept; a draft copy is appended, replaced, and taken back out,
/// and the taking out can fail in a way that leaves the copy standing in somebody's folder with nothing left able to
/// name it. <see cref="Abandoned" /> is that ending, and reporting it as <see cref="Withdrawn" /> would be MailFathom
/// stating that a message it can no longer reach is gone.
/// </para>
/// <para>
/// The stage is stored as its name, so an ad-hoc audit query stays readable and a later reordering of this enum changes
/// nothing about a database already written.
/// </para>
/// </remarks>
public enum MailDraftCopyStage
{
    /// <summary>The append has begun and the server's answer to it has not been read.</summary>
    /// <remarks>
    /// A copy left here by a stopped process may or may not be in the folder, and it is never appended again: an
    /// <c>APPEND</c> issued twice is a second draft in the owner's folder rather than a repeat of the first. It is also
    /// never withdrawn, because nothing names it.
    /// </remarks>
    Issued = 0,

    /// <summary>The server accepted the append, and the folder holds the copy as far as MailFathom knows.</summary>
    Standing = 1,

    /// <summary>MailFathom took the copy back out of the folder.</summary>
    Withdrawn = 2,

    /// <summary>The copy can no longer be shown to be the one MailFathom appended, so it is left as the owner's.</summary>
    /// <remarks>
    /// This is where every divergence lands. What it says is that nothing will touch the copy again — not that the
    /// folder no longer holds it — which is the only honest answer once the occurrence stopped being identifiable.
    /// </remarks>
    Abandoned = 3,
}
