// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>One folder of an account as the surface that manages folders reads it, whichever copy of the mailbox is the truth.</summary>
/// <param name="Id">What every act names the folder by, opaque to whoever holds it.</param>
/// <param name="ParentId">The folder it sits beneath, or <see langword="null" /> at the top of the hierarchy.</param>
/// <param name="Name">The folder's own level of the hierarchy, which is what a person sees.</param>
/// <param name="Role">The role the folder plays for its account, or <see langword="null" /> where it plays none.</param>
/// <param name="AllowedActs">Which of the four acts this folder currently permits, in the enumeration's own order.</param>
/// <remarks>
/// <para>
/// The identity is text rather than a number or a hierarchy of names, and its shape is deliberately not part of the
/// contract: on an account whose mailbox MailFathom holds it is the local folder's own identity, and on a mirrored one
/// it is the alias the folder is declared under. A caller reads it, keeps it, and hands it back — which is the whole
/// of what keeps the storage mode out of what the client knows.
/// </para>
/// <para>
/// The parent is an identity rather than a path for the same reason. A mirrored folder's place in its server's
/// hierarchy is a path, and a held account's is a parent identity, so publishing the path would have been publishing
/// which of the two an account is. A mirrored folder whose parent the account does not declare is reported at the top
/// of the hierarchy, which is where a tree can actually draw it.
/// </para>
/// <para>
/// The allowed acts are the answer a client must not work out for itself. They differ per folder rather than per
/// account — a folder playing a protected role allows none of the three that name a folder, and on a mirrored account
/// a folder the deployment's own configuration fixed allows none either — so a client that branched on anything but
/// this would offer acts the service is going to refuse.
/// </para>
/// </remarks>
public sealed record ManagedMailFolder(
    string Id,
    string? ParentId,
    string Name,
    MailFolderSpecialUse? Role,
    IReadOnlyList<MailFolderAct> AllowedActs);
