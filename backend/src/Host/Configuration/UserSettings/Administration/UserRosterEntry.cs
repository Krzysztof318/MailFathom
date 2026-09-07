// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>One user a deployment holds, as an administrator reads a roster.</summary>
/// <param name="User">The identifier every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this user apart by, which nothing resolves them by.</param>
/// <param name="RecordIsTheirOwn">Whether their mail accounts come from their own record rather than from a configuration source.</param>
/// <param name="Served">Whether this process is serving them, which a held user no source declares is not.</param>
/// <param name="DeclaredInConfiguration">Whether a configuration source names this user, so a start would put back what an act here changed.</param>
/// <remarks>
/// <para>
/// The label is here because a column of generated identifiers is not a roster anybody can read. Nothing resolves an
/// user by it — every later act names the identifier — but choosing which user to act on is what an administrator
/// does first, and the identifier says nothing about who the person is.
/// </para>
/// <para>
/// The last three are separate facts and a reader needs all of them. A user held and not served keeps every message
/// of theirs and synchronizes none of it, which is what a file that stopped declaring them leaves behind; a user
/// served from a configuration source has an empty record, which is what makes a write to it something to refuse rather
/// than apply; and a user a file declares carries that file's label and that file's row, whichever source their mail
/// accounts come from, so a relabel here is undone at the next start and an erasure is written back by it.
/// </para>
/// </remarks>
internal sealed record UserRosterEntry(
    MailUserId User,
    string DisplayName,
    bool RecordIsTheirOwn,
    bool Served,
    bool DeclaredInConfiguration);
