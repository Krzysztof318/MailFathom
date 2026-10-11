// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>The version one user's record stands at, read without the record itself.</summary>
/// <param name="User">The user the record belongs to.</param>
/// <param name="Version">The version the record's last commit produced.</param>
/// <remarks>
/// The versions of the policies governing the user travel with it, because what a replica serves somebody is their
/// record resolved through those policies: a composition is out of date when any of the three has moved, and a policy
/// write moves no record's version.
/// </remarks>
public sealed record UserSettingsDocumentVersion(UserId User, long Version)
{
    /// <summary>Gets the version the deployment's settings policy stands at, which is zero where it stores none.</summary>
    public long DeploymentPolicyVersion { get; init; }

    /// <summary>Gets the version the settings policy of the user's organization stands at, which is zero where that organization stores none or the user belongs to none.</summary>
    public long OrganizationPolicyVersion { get; init; }
}
