// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The settings policies governing a record, as a test states them.</summary>
internal static class StatedPolicies
{
    /// <summary>The organization a test's organization policy belongs to.</summary>
    internal static readonly Guid Organization = Guid.Parse("7c1f6f0e-8f0b-4a53-9d0f-3c0c7a5a9e11");

    /// <summary>States the policies governing one record.</summary>
    /// <param name="deployment">The deployment's policy.</param>
    /// <param name="organization">The policy of the record's organization, or <see langword="null" /> for a record in none.</param>
    /// <param name="deploymentVersion">The version the deployment's policy stands at.</param>
    /// <param name="organizationVersion">The version the organization's policy stands at.</param>
    /// <returns>The policies.</returns>
    internal static GoverningSettingsPolicies Governing(
        string deployment = "{}",
        string? organization = null,
        long deploymentVersion = 1,
        long organizationVersion = 1) => new(
            new SettingsPolicyDocument(OrganizationId: null, deployment, deploymentVersion),
            organization is null ? null : new SettingsPolicyDocument(Organization, organization, organizationVersion));
}
