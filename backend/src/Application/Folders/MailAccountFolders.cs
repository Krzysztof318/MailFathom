// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;

namespace MailFathom.Application.Folders;

/// <summary>One of the owner's accounts together with the folders a screen draws beneath it.</summary>
/// <param name="Account">How current the account's local copy is, and under which names it is published.</param>
/// <param name="Folders">One entry per folder local state knows of, ordered by alias, empty when synchronization has reached none.</param>
/// <remarks>
/// The account travels with its folders rather than being left to a second request, because a tree is one thing on
/// screen: a client that read the folders here and the mailbox names elsewhere would be composing one picture out of
/// two answers, the second of them already stale relative to the first.
/// </remarks>
public sealed record MailAccountFolders(
    MailAccountFreshness Account,
    IReadOnlyList<DescribedMailFolder> Folders);
