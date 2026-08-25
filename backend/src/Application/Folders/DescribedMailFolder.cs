// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Describes one folder as a screen drawing a mailbox tree reads it.</summary>
/// <param name="Freshness">How current the local copy of the folder is, which is also what names the folder.</param>
/// <param name="Role">The role the folder plays for its account, or <see langword="null" /> when configuration labels it with none.</param>
/// <param name="HierarchyLevels">The folder's place on its mail server, outermost level first, and empty when nothing has bound the alias to a remote folder yet.</param>
/// <param name="StoredEmailCount">How many of the folder's emails this deployment holds.</param>
/// <param name="UnreadEmailCount">How many of those the mail server last reported without <c>\Seen</c>.</param>
/// <remarks>
/// <para>
/// The role is the answer a screen cannot work out for itself. Special-use folders are identified by server attributes
/// rather than by their names, which differ per provider and per language, so a client that guessed which folder is the
/// sent one from its name would guess wrong on somebody's Polish provider. It is <see cref="MailFolderMapping" />'s
/// label — what an operator wrote, or what discovery matched — and never a name this reduced from.
/// </para>
/// <para>
/// The levels are the server's own hierarchy rather than the alias, because an alias is one configured word — upper
/// cased, unique within its account, and flat by construction — and a mailbox tree is not drawn from those. They are
/// split here rather than published as a path and a delimiter, so no reader has to know that a delimiter exists.
/// </para>
/// <para>
/// The counts are of the local copy. A folder still being backfilled holds fewer than the mail server does, which is
/// why the freshness travels in the same value rather than being available separately: a count without it is a figure a
/// reader would take for the mailbox's own.
/// </para>
/// </remarks>
public sealed record DescribedMailFolder(
    MailFolderFreshness Freshness,
    MailFolderSpecialUse? Role,
    IReadOnlyList<string> HierarchyLevels,
    int StoredEmailCount,
    int UnreadEmailCount)
{
    /// <summary>Gets MailFathom's own name for the folder, which is what everything else names it by.</summary>
    public MailFolderAlias Alias => this.Freshness.Alias;
}
