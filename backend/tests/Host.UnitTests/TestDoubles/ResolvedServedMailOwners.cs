// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.OwnerSettings;
using MailFathom.TestSupport;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the roster a startup gate would have published, so a test downstream of it need not run one.</summary>
/// <remarks>
/// The real roster is reconciled against the owner records the database holds, and the refusals that reconciliation
/// raises are asserted where the gate lives. Everything downstream of it only needs the roster to exist, so a test of
/// what a configuration serves states it the way the composition root ends up with it.
/// </remarks>
internal static class ResolvedServedMailOwners
{
    /// <summary>Builds the roster of a deployment that declares no owner, whose accounts are the deployment's own section.</summary>
    /// <returns>The roster, holding the one owner such a deployment serves.</returns>
    internal static ServedMailOwners TheSoleOwner() =>
        Serving(new ServedMailOwner(
            SyntheticMailOwner.Deployment,
            "owner",
            MailOwnerAccountSource.DeploymentSection,
            MailAccounts: []));

    /// <summary>Builds the roster of a deployment whose file declares each owner and the mailboxes they own.</summary>
    /// <param name="owner">The owner the accounts belong to.</param>
    /// <param name="displayName">The label the owner is declared under.</param>
    /// <param name="mailAccounts">The mail accounts that owner declares.</param>
    /// <returns>The roster, holding that one declared owner.</returns>
    internal static ServedMailOwners Declaring(
        MailOwnerId owner,
        string displayName,
        params MailSynchronizationAccountOptions[] mailAccounts) =>
        Serving(new ServedMailOwner(owner, displayName, MailOwnerAccountSource.OwnerDeclaration, mailAccounts));

    /// <summary>Builds a roster from the owners it is given, in the order they are given.</summary>
    /// <param name="owners">The owners the deployment serves.</param>
    /// <returns>The roster.</returns>
    internal static ServedMailOwners Serving(params ServedMailOwner[] owners)
    {
        var roster = new ServedMailOwners();

        roster.Resolved(owners);

        return roster;
    }
}
