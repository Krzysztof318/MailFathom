// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Host.Configuration.Policies;

/// <summary>What the policies governing one record say about its kind: the deployment's, and its organization's where it belongs to one.</summary>
/// <param name="Deployment">What the deployment's policy says, which governs every record.</param>
/// <param name="Organization">What the policy of the record's organization says, or <see langword="null" /> for a record in no organization.</param>
/// <remarks>
/// A record in no organization is governed by the deployment alone, which is why the organization's half is absent
/// rather than empty: an organization that states nothing still names who would hold a setting if it did, and a
/// record outside every organization has nobody in that place.
/// </remarks>
internal sealed record SettingsPolicyLayers(SettingsPolicyStatements Deployment, SettingsPolicyStatements? Organization)
{
    /// <summary>Gets the layers of a record no policy says anything about.</summary>
    internal static SettingsPolicyLayers Nothing { get; } = new(SettingsPolicyStatements.Nothing, Organization: null);

    /// <summary>Gets what these policies force a person's telemetry switch to, or <see langword="null" /> where neither forces it.</summary>
    /// <remarks>The deployment's force is read first and wins over an organization's, here as for every property of the record.</remarks>
    internal ForcedClientTelemetry? ForcedClientTelemetry => (this.Deployment.ForcedClientTelemetry, this.Organization?.ForcedClientTelemetry) switch
    {
        ({ } deployment, _) => new ForcedClientTelemetry(deployment, SettingsPolicyScope.Deployment),
        (null, { } organization) => new ForcedClientTelemetry(organization, SettingsPolicyScope.Organization),
        _ => null,
    };

    /// <summary>Says that a record is not resolved because one of the policies governing it is not one this build reads.</summary>
    /// <param name="unreadable">The scope whose policy it is.</param>
    /// <returns>One sentence naming the scope and what clears it.</returns>
    internal static string DescribeUnreadable(SettingsPolicyScope unreadable) =>
        $"The settings policy of {(unreadable == SettingsPolicyScope.Deployment ? "the deployment" : "this record's organization")} is not one this build reads as a settings policy, so the record is not resolved through it and is held back rather than served on part of what governs it. Correct the policy with 'mfctl policy edit', or bring this replica to the build that wrote it.";

    /// <summary>Reads what the policies governing a record say about its kind.</summary>
    /// <param name="policies">The policies as their rows hold them.</param>
    /// <param name="section">The kind of record.</param>
    /// <param name="unreadable">Which scope's policy this build does not judge as one, where the answer is <see langword="null" />.</param>
    /// <returns>The layers, or <see langword="null" /> when this build does not judge one of the policies as a settings policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policies" /> or <paramref name="section" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// One policy this build cannot bind leaves the record with no layers at all rather than with the other one's:
    /// a force that lifts whenever a replica cannot read it is not a force, so the caller holds the record back
    /// instead of serving it on part of what governs it.
    /// </remarks>
    // ponytail: each reading judges the policy documents afresh, so a start composing every user under one policy
    // judges it once per user. Memoise by scope and version if a start over many users measures slow.
    internal static SettingsPolicyLayers? Read(
        GoverningSettingsPolicies policies,
        SettingsPolicySection section,
        out SettingsPolicyScope unreadable) =>
        Read(policies, json => SettingsPolicyStatements.Read(json, section), out unreadable);

    /// <summary>Reads what the policies governing a record say, through a reading of one policy the caller supplies.</summary>
    /// <param name="policies">The policies as their rows hold them.</param>
    /// <param name="read">Reads one policy's statements about the kind of record asked for, or nothing where this build does not judge it as a policy.</param>
    /// <param name="unreadable">Which scope's policy this build does not judge as one, where the answer is <see langword="null" />.</param>
    /// <returns>The layers, or <see langword="null" /> when one of the policies is not judged as a settings policy.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="policies" /> or <paramref name="read" /> is <see langword="null" />.</exception>
    /// <remarks>What lets a write that reaches many records under one policy judge that policy once rather than once per record.</remarks>
    internal static SettingsPolicyLayers? Read(
        GoverningSettingsPolicies policies,
        Func<string, SettingsPolicyStatements?> read,
        out SettingsPolicyScope unreadable)
    {
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(read);

        unreadable = SettingsPolicyScope.Deployment;

        if (read(policies.Deployment.Json) is not { } deployment)
        {
            return null;
        }

        if (policies.Organization is not { } held)
        {
            return new SettingsPolicyLayers(deployment, Organization: null);
        }

        unreadable = SettingsPolicyScope.Organization;

        return read(held.Json) is { } organization
            ? new SettingsPolicyLayers(deployment, organization)
            : null;
    }
}
