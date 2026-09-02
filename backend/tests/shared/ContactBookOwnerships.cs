// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts;
using MailFathom.Domain.Access;

namespace MailFathom.TestSupport;

/// <summary>Builds the resolution of whose contact book a use case is acting on, over a principal a test states.</summary>
/// <remarks>
/// Every use case over the book takes one, and composing it by hand means arranging a deployment-owner source in each
/// suite that arranges a caller. What a test states here is the principal, and — where it matters — which owner the
/// deployment serves, because those are the two facts the resolution reads.
/// </remarks>
internal static class ContactBookOwnerships
{
    /// <summary>Builds the resolution for work acting on the book of the owner the deployment serves.</summary>
    /// <returns>The resolution a use case whose test says nothing about ownership consults.</returns>
    /// <remarks>
    /// For the suites whose subject is something else — addressing a message, drafting one, submitting one — where the
    /// book is one collaborator among several and the caller is the ordinary one. A test about the scoping itself
    /// states its own principal through <see cref="For(AccessAuthorization)" /> instead.
    /// </remarks>
    internal static ContactBookOwnership ForTheServedOwner() => For(AccessAuthorizations.ForCallerGranted());

    /// <summary>Builds the resolution for a deployment serving <see cref="SyntheticMailOwner.Deployment" />.</summary>
    /// <param name="authorization">The authorization the use case beside it was given.</param>
    /// <returns>The resolution that use case consults.</returns>
    internal static ContactBookOwnership For(AccessAuthorization authorization) =>
        For(authorization, SyntheticMailOwner.Deployment);

    /// <summary>Builds the resolution for a deployment serving one named owner.</summary>
    /// <param name="authorization">The authorization the use case beside it was given.</param>
    /// <param name="deploymentOwner">The owner a principal acting for none falls back to.</param>
    /// <returns>The resolution that use case consults.</returns>
    internal static ContactBookOwnership For(AccessAuthorization authorization, MailOwnerId deploymentOwner) =>
        new(authorization, new StatedDeploymentOwner(deploymentOwner));

    /// <summary>Names the owner a test says this deployment serves.</summary>
    private sealed class StatedDeploymentOwner(MailOwnerId owner) : IDeploymentMailOwnerSource
    {
        public MailOwnerId Owner => owner;
    }
}
