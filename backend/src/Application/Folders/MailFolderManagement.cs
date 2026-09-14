// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>What one account's folders are and what may be done to them.</summary>
/// <param name="AllowedActs">Which acts the account as a whole permits, which is <see cref="MailFolderAct.Create" /> or nothing.</param>
/// <param name="CreatableRoles">The roles the account has no folder for and which a creation may therefore name.</param>
/// <param name="Folders">The account's folders, ordered by name, each with the acts it allows.</param>
/// <remarks>
/// <para>
/// An account permits creating a folder or it does not, and the reasons it may not are its own rather than any
/// folder's: its mailbox may be being restored to its source, or the hierarchy may already hold as many folders as it
/// takes. Everything else is per folder, which is why the two are reported at the levels they belong to instead of one
/// flag for the account.
/// </para>
/// <para>
/// The creatable roles are how a missing special folder is made: a person chooses the role and the service supplies
/// the name, so a client never asks anybody to name their own trash folder. A role the account already has a folder for
/// is absent from the list, because one account has at most one folder per role.
/// </para>
/// </remarks>
public sealed record MailFolderManagement(
    IReadOnlyList<MailFolderAct> AllowedActs,
    IReadOnlyList<MailFolderSpecialUse> CreatableRoles,
    IReadOnlyList<ManagedMailFolder> Folders);
