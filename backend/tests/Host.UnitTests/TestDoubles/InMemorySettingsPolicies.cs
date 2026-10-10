// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Holds settings policies in memory, deciding a commit the way the store's one statement does.</summary>
/// <remarks>
/// A state fake rather than a substitute, because what a test of the administration is about is the version a write
/// was composed over and the version it finds: a commit here matches the policy at the expected version or matches
/// nothing, so the attempt that commits second is refused whoever made it.
/// </remarks>
internal sealed class InMemorySettingsPolicies : ISettingsPolicyStore
{
    private readonly Dictionary<Guid, SettingsPolicyDocument> policies = [];
    private readonly HashSet<Guid> organizations = [];

    /// <summary>Gets or sets what happens between a candidate being judged and its commit, which is where another administrator's write lands.</summary>
    internal Action? BeforeCommit { get; set; }

    /// <summary>Gets how many commits moved a policy.</summary>
    internal int Commits { get; private set; }

    /// <summary>Records an organization this deployment holds, storing no policy for it.</summary>
    /// <param name="organizationId">The organization.</param>
    internal void HoldingOrganization(Guid organizationId) => this.organizations.Add(organizationId);

    /// <summary>Removes an organization, and its policy with it.</summary>
    /// <param name="organizationId">The organization.</param>
    internal void RemovingOrganization(Guid organizationId)
    {
        this.organizations.Remove(organizationId);
        this.policies.Remove(organizationId);
    }

    /// <summary>Stores a policy for one scope as an earlier write left it.</summary>
    /// <param name="organizationId">The organization, or <see langword="null" /> for the deployment.</param>
    /// <param name="json">The policy.</param>
    /// <param name="version">The version it stands at.</param>
    internal void Holding(Guid? organizationId, string json, long version)
    {
        if (organizationId is { } organization)
        {
            this.organizations.Add(organization);
        }

        this.policies[KeyOf(organizationId)] = new SettingsPolicyDocument(organizationId, json, version);
    }

    /// <inheritdoc />
    public Task<SettingsPolicyDocument?> ReadAsync(Guid? organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(this.Holds(organizationId)
            ? this.policies.GetValueOrDefault(KeyOf(organizationId), SettingsPolicyDocument.Unwritten(organizationId))
            : null);

    /// <inheritdoc />
    public Task<long?> CommitAsync(
        Guid? organizationId,
        string json,
        long expectedVersion,
        CancellationToken cancellationToken)
    {
        this.BeforeCommit?.Invoke();

        if (!this.Holds(organizationId)
            || this.policies.GetValueOrDefault(KeyOf(organizationId), SettingsPolicyDocument.Unwritten(organizationId)).Version != expectedVersion)
        {
            return Task.FromResult<long?>(null);
        }

        this.Commits++;
        this.policies[KeyOf(organizationId)] = new SettingsPolicyDocument(organizationId, json, expectedVersion + 1);

        return Task.FromResult<long?>(expectedVersion + 1);
    }

    private static Guid KeyOf(Guid? organizationId) => organizationId ?? Guid.Empty;

    private bool Holds(Guid? organizationId) =>
        organizationId is not { } organization || this.organizations.Contains(organization);
}
