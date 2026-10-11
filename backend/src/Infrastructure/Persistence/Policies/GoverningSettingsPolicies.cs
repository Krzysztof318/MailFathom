// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>The settings policies that govern one record: the deployment's, and the one of the organization the record belongs to.</summary>
/// <param name="Deployment">The deployment's policy, stating nothing where the deployment stores none.</param>
/// <param name="Organization">The policy of the record's organization, stating nothing where that organization stores none; <see langword="null" /> for a record in no organization.</param>
/// <remarks>
/// A record in no organization is governed by the deployment's policy alone, so the absence of the second half is the
/// fact that it belongs to none rather than a policy that was not found. Each half carries the version it was read
/// at, which is what lets a replica tell that a record it composed was composed under a policy that has since moved.
/// </remarks>
public sealed record GoverningSettingsPolicies(SettingsPolicyDocument Deployment, SettingsPolicyDocument? Organization)
{
    /// <summary>Gets the policies of a deployment that stores none, for a record in no organization.</summary>
    public static GoverningSettingsPolicies Unwritten { get; } =
        new(SettingsPolicyDocument.Unwritten(organizationId: null), Organization: null);
}
