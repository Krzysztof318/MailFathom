// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Users.EndpointSwitches;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>The users' rows a deployment holds, with their endpoint switches and the stamp on them, kept in memory.</summary>
/// <remarks>
/// A state fake rather than a substitute, because what the reconciliation is about is which rows it reaches and what
/// each holds afterwards. It decides both the way the store's statements do: a row trails where its version or its
/// organization moved past its stamp, a derivation reads the row and the policies as they stand when it runs rather
/// than as they stood when the row was found, and a row whose stamp matches by then is left alone.
/// </remarks>
internal sealed class InMemoryUserEndpointSwitchRows : IUserEndpointSwitchStore
{
    private readonly SortedDictionary<Guid, Row> rows = [];
    private readonly Dictionary<Guid, string> organizationPolicies = [];

    /// <summary>Gets or sets the deployment's policy as a derivation reads it inside its own write.</summary>
    internal string DeploymentPolicy { get; set; } = "{}";

    /// <summary>Gets or sets what another writer commits between a reading of the trailing rows and the first derivation after it.</summary>
    internal Action? AfterReading { get; set; }

    /// <summary>Gets the user each reading was asked to read past and how many rows it was asked for, in the order the readings were made.</summary>
    internal List<(Guid? After, int Limit)> Readings { get; } = [];

    /// <summary>Gets the users a derivation was asked for, once per asking and in the order they were asked.</summary>
    internal List<UserId> Derivations { get; } = [];

    /// <summary>States the policy of one organization as a derivation reads it inside its own write.</summary>
    /// <param name="organizationId">The organization.</param>
    /// <param name="policy">Its policy.</param>
    internal void GovernedBy(Guid organizationId, string policy) => this.organizationPolicies[organizationId] = policy;

    /// <summary>States a row this build wrote: its stamp names the version and the organization it holds.</summary>
    /// <param name="user">The user.</param>
    /// <param name="json">The user's own record.</param>
    /// <param name="switches">The switches the row carries.</param>
    /// <param name="organizationId">The organization the user belongs to, or <see langword="null" /> for none.</param>
    internal void Hold(UserId user, string? json, UserEndpointAccess switches, Guid? organizationId = null) =>
        this.rows[user.Value] = new Row(json, Version: 1, organizationId, StampedVersion: 1, organizationId) { Switches = switches };

    /// <summary>Writes a user's record as a build that does not know settings policies does: the record's own switches onto the row, and the stamp left where it was.</summary>
    /// <param name="user">The user.</param>
    /// <param name="json">The record it wrote.</param>
    /// <param name="switches">The switches that record states on its own.</param>
    internal void WriteAsAnOlderBuild(UserId user, string? json, UserEndpointAccess switches)
    {
        var held = this.rows[user.Value];

        this.rows[user.Value] = held with { Json = json, Version = held.Version + 1, Switches = switches };
    }

    /// <summary>Moves a user as a build that does not know settings policies does: the organization alone, with the switches and the stamp left where they were.</summary>
    /// <param name="user">The user.</param>
    /// <param name="organizationId">The organization they were moved into, or <see langword="null" /> for none.</param>
    internal void MoveAsAnOlderBuild(UserId user, Guid? organizationId) =>
        this.rows[user.Value] = this.rows[user.Value] with { OrganizationId = organizationId };

    /// <summary>Reads the switches one user's row carries.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The switches.</returns>
    internal UserEndpointAccess SwitchesOf(UserId user) => this.rows[user.Value].Switches;

    /// <summary>Reports whether one user's stamp names the version and the organization their row holds.</summary>
    /// <param name="user">The user.</param>
    /// <returns>Whether the stamp matches.</returns>
    internal bool IsStampedAsCurrent(UserId user) => !this.rows[user.Value].Trails;

    /// <inheritdoc />
    public Task<IReadOnlyList<UserId>> ReadTrailingAsync(Guid? after, int limit, CancellationToken cancellationToken)
    {
        this.Readings.Add((after, limit));

        IReadOnlyList<UserId> trailing =
        [
            .. this.rows
                .Where(row => row.Value.Trails && (after is not { } last || row.Key.CompareTo(last) > 0))
                .Take(limit)
                .Select(row => UserId.Create(row.Key)),
        ];

        this.AfterReading?.Invoke();
        this.AfterReading = null;

        return Task.FromResult(trailing);
    }

    /// <inheritdoc />
    public Task<bool> DeriveAgainAsync(
        UserId user,
        Func<GovernedUserRecord, UserEndpointAccess?> derive,
        CancellationToken cancellationToken)
    {
        this.Derivations.Add(user);

        if (!this.rows.TryGetValue(user.Value, out var held) || !held.Trails)
        {
            return Task.FromResult(false);
        }

        var policies = new GoverningSettingsPolicies(
            new SettingsPolicyDocument(OrganizationId: null, this.DeploymentPolicy, Version: 1),
            held.OrganizationId is { } organization
                ? new SettingsPolicyDocument(organization, this.organizationPolicies.GetValueOrDefault(organization, "{}"), Version: 1)
                : null);

        if (derive(new GovernedUserRecord(user, $"User {user.Value:D}", held.Json, held.Switches, policies, policies)) is not { } derived)
        {
            return Task.FromResult(false);
        }

        this.rows[user.Value] = held with
        {
            Switches = derived,
            StampedVersion = held.Version,
            StampedOrganizationId = held.OrganizationId,
        };

        return Task.FromResult(true);
    }

    private sealed record Row(string? Json, long Version, Guid? OrganizationId, long StampedVersion, Guid? StampedOrganizationId)
    {
        public UserEndpointAccess Switches { get; init; }

        public bool Trails => this.StampedVersion != this.Version || this.StampedOrganizationId != this.OrganizationId;
    }
}
