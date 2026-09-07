// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.TestSupport;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the roster a startup gate would have published, so a test downstream of it need not run one.</summary>
/// <remarks>
/// The real roster is reconciled against the user records the database holds, and the refusals that reconciliation
/// raises are asserted where the gate lives. Everything downstream of it only needs the roster to exist, so a test of
/// what a configuration serves states it the way the composition root ends up with it.
/// </remarks>
internal static class ResolvedServedMailUsers
{
    /// <summary>Builds the roster of a deployment that declares no user, whose accounts are the deployment's own section.</summary>
    /// <returns>The roster, holding the one user such a deployment serves.</returns>
    internal static ServedMailUsers TheSoleUser() =>
        Serving(new ServedMailUser(
            SyntheticMailUser.Deployment,
            "user",
            MailUserAccountSource.DeploymentSection,
            MailAccounts: []));

    /// <summary>Builds the roster of a deployment whose file declares each user and the mailboxes they own.</summary>
    /// <param name="user">The user the accounts belong to.</param>
    /// <param name="displayName">The label the user is declared under.</param>
    /// <param name="mailAccounts">The mail accounts that user declares.</param>
    /// <returns>The roster, holding that one declared user.</returns>
    internal static ServedMailUsers Declaring(
        MailUserId user,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        Serving(new ServedMailUser(user, displayName, MailUserAccountSource.UserDeclaration, mailAccounts));

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
