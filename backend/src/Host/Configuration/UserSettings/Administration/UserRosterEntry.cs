// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Host.Configuration.UserSettings.Administration;

/// <summary>One user a deployment holds, as an administrator reads a roster.</summary>
/// <param name="User">The identifier every mail account and every stored message of theirs hangs on.</param>
/// <param name="DisplayName">The label an operator tells this user apart by, which nothing resolves them by.</param>
/// <param name="RecordIsTheirOwn">Whether their mail accounts come from their own record rather than from a configuration source.</param>
/// <param name="Served">Whether this process is serving them, which every user it holds is.</param>
/// <param name="DeclaredInConfiguration">Whether the deployment's own mail section supplies this user's mailboxes, so an erasure of them is refused rather than performed — a start would only record them again.</param>
/// <remarks>
/// <para>
/// The label is here because a column of generated identifiers is not a roster anybody can read. Nothing resolves a
/// user by it — every later act names the identifier — but choosing which user to act on is what an administrator
/// does first, and the identifier says nothing about who the person is.
/// </para>
/// <para>
/// The last three are separate facts and a reader needs all of them. Every user this deployment holds is served, so
/// <c>Served</c> answers whether this process has settled its roster rather than whether anybody was left out of it; a
/// user the deployment's own mail section supplies has an empty record, which is what makes a write to it something to
/// refuse rather than apply; and a start records a user for that section wherever it holds none, so an erasure there
/// would be followed by the person being recreated and their mail downloaded again.
/// </para>
/// </remarks>
internal sealed record UserRosterEntry(
    MailUserId User,
    string DisplayName,
    bool RecordIsTheirOwn,
    bool Served,
    bool DeclaredInConfiguration);
