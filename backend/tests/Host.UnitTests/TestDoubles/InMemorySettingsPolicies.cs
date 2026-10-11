// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Policies;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Holds settings policies in memory, deciding a commit the way the store's own transaction does.</summary>
/// <param name="judge">What a commit asks about each governed user's record, or <see langword="null" /> where a test holds no user.</param>
/// <remarks>
/// <para>
/// A state fake rather than a substitute, because what a test of the administration is about is the version a write
/// was composed over and the version it finds: a commit here matches the policy at the expected version or matches
/// nothing, so the attempt that commits second is refused whoever made it.
/// </para>
/// <para>
/// It holds users as well, since a policy write is judged against the records it governs and rewrites the endpoint
/// switches on their rows in the same commit. The steps are the store's, in its order: the bound on how many records
/// one write judges, then the judgement of every governed record, then the rows the policy moves, then the policy.
/// </para>
/// </remarks>
internal sealed class InMemorySettingsPolicies(IUserRecordPolicyJudge? judge = null) : ISettingsPolicyStore
{
    private readonly Dictionary<Guid, SettingsPolicyDocument> policies = [];
    private readonly HashSet<Guid> organizations = [];
    private readonly List<HeldUser> users = [];

    /// <summary>Holds policies whose writes are judged by the deployment's own judge, over the binder a record is bound by.</summary>
    /// <returns>The store.</returns>
    internal static InMemorySettingsPolicies JudgingRecords() => new(new UserRecordPolicyJudge(new UserAccountDocumentBinder(
        new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
        new FakeTimeProvider(),
        Options.Create(new SensitiveContentOptions()))));

    /// <summary>Gets or sets what happens between a candidate being judged and its commit, which is where another administrator's write lands.</summary>
    internal Action? BeforeCommit { get; set; }

    /// <summary>Gets how many commits moved a policy.</summary>
    internal int Commits { get; private set; }

    /// <summary>Gets how many user records the last commit attempted was allowed to judge, or <see langword="null" /> where it judged none.</summary>
    internal int? LastMaximumJudged { get; private set; }

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

    /// <summary>Records a user the deployment holds, served everywhere until a policy moves them.</summary>
    /// <param name="user">The user.</param>
    /// <param name="displayName">The label a refusal names them by.</param>
    /// <param name="organizationId">The organization they belong to, or <see langword="null" /> for none.</param>
    /// <param name="json">Their own record.</param>
    internal void HoldingUser(UserId user, string displayName, Guid? organizationId, string json) =>
        this.users.Add(new HeldUser(user, displayName, organizationId, json) { EndpointAccess = UserEndpointAccess.Everywhere });

    /// <summary>Reads the endpoint switches one user's row carries.</summary>
    /// <param name="user">The user.</param>
    /// <returns>The switches.</returns>
    internal UserEndpointAccess EndpointAccessOf(UserId user) => this.users.Single(held => held.User == user).EndpointAccess;

    /// <inheritdoc />
    public Task<SettingsPolicyDocument?> ReadAsync(Guid? organizationId, CancellationToken cancellationToken) =>
        Task.FromResult(this.Holds(organizationId) ? this.PolicyOf(organizationId) : null);

    /// <inheritdoc />
    public Task<SettingsPolicyCommit> CommitAsync(
        Guid? organizationId,
        string json,
        long expectedVersion,
        int? maximumUserRecordsJudged,
        CancellationToken cancellationToken)
    {
        this.BeforeCommit?.Invoke();
        this.LastMaximumJudged = maximumUserRecordsJudged;

        if (!this.Holds(organizationId) || this.PolicyOf(organizationId).Version != expectedVersion)
        {
            return Task.FromResult(new SettingsPolicyCommit(SettingsPolicyCommitResult.Superseded));
        }

        var candidate = new SettingsPolicyDocument(organizationId, json, expectedVersion + 1);

        if (maximumUserRecordsJudged is { } maximum && this.JudgeAndProject(candidate, maximum) is { } refused)
        {
            return Task.FromResult(refused);
        }

        this.Commits++;
        this.policies[KeyOf(organizationId)] = candidate;

        return Task.FromResult(new SettingsPolicyCommit(SettingsPolicyCommitResult.Committed, candidate.Version));
    }

    private static Guid KeyOf(Guid? organizationId) => organizationId ?? Guid.Empty;

    private SettingsPolicyDocument PolicyOf(Guid? organizationId) =>
        this.policies.GetValueOrDefault(KeyOf(organizationId), SettingsPolicyDocument.Unwritten(organizationId));

    private bool Holds(Guid? organizationId) =>
        organizationId is not { } organization || this.organizations.Contains(organization);

    private SettingsPolicyCommit? JudgeAndProject(SettingsPolicyDocument candidate, int maximum)
    {
        HeldUser[] governed =
        [
            .. this.users.Where(user => candidate.OrganizationId is null || user.OrganizationId == candidate.OrganizationId),
        ];

        if (governed.Length > maximum)
        {
            return new SettingsPolicyCommit(SettingsPolicyCommitResult.TooManyRecords, GovernedRecords: governed.Length);
        }

        GoverningSettingsPolicies Governing(HeldUser user, bool written) => new(
            written && candidate.OrganizationId is null ? candidate : this.PolicyOf(organizationId: null),
            user.OrganizationId is not { } organization
                ? null
                : written && candidate.OrganizationId == organization ? candidate : this.PolicyOf(organization));

        var judgements = (judge ?? throw new InvalidOperationException("A policy write that governs users is judged, and this store was given no judge."))
            .Judge([.. governed.Select(user => new GovernedUserRecord(
                user.User,
                user.DisplayName,
                user.Json,
                Governing(user, written: false),
                Governing(user, written: true)))]);

        var judged = governed.Zip(judgements).ToArray();
        var inTheWay = judged.Where(user => user.Second.Refusals.Count > 0).ToArray();

        if (inTheWay.Length > 0)
        {
            return new SettingsPolicyCommit(
                SettingsPolicyCommitResult.RecordsLeftInvalid,
                GovernedRecords: governed.Length,
                RecordsInTheWay:
                [
                    .. inTheWay
                        .Take(SettingsPolicyCommit.MaximumRecordsNamed)
                        .Select(user => new SettingsPolicyRecordInTheWay(user.First.User.Value, user.First.DisplayName, user.Second.Refusals)),
                ],
                RecordsLeftInvalid: inTheWay.Length);
        }

        foreach (var (user, judgement) in judged)
        {
            user.EndpointAccess = judgement.EndpointAccess ?? user.EndpointAccess;
        }

        return null;
    }

    /// <summary>One user's row as a policy write reaches it.</summary>
    private sealed record HeldUser(UserId User, string DisplayName, Guid? OrganizationId, string Json)
    {
        /// <summary>Gets or sets the switches the row carries, which a policy write rewrites.</summary>
        public UserEndpointAccess EndpointAccess { get; set; }
    }
}
