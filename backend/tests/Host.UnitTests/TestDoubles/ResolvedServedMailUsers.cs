// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the roster a startup gate would have published, so a test downstream of it need not run one.</summary>
/// <remarks>
/// The real roster is composed from the user records the database holds, and the refusals that composition raises are
/// asserted where the gate lives. Everything downstream of it only needs the roster to exist, so a test of what a
/// deployment serves states it the way the composition root ends up with it.
/// </remarks>
internal static class ResolvedServedMailUsers
{
    /// <summary>Builds the roster of a deployment whose users are records, each holding the mailboxes they own.</summary>
    /// <param name="user">The user the accounts belong to.</param>
    /// <param name="displayName">The label the user is recorded under.</param>
    /// <param name="mailAccounts">The mail accounts that user's record declares.</param>
    /// <returns>The roster, holding that one recorded user.</returns>
    internal static ServedMailUsers Recording(
        MailUserId user,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        Serving(new ServedMailUser(user, displayName, mailAccounts));

    /// <summary>Builds a roster from the users it is given, in the order they are given.</summary>
    /// <param name="users">The users the deployment serves.</param>
    /// <returns>The roster.</returns>
    internal static ServedMailUsers Serving(params ServedMailUser[] users)
    {
        var roster = new ServedMailUsers();

        roster.Resolved(users);

        return roster;
    }
}
