// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Policies;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Configuration.UserSettings;
using MailFathom.Infrastructure.Persistence.Policies;
using MailFathom.Infrastructure.Persistence.Policies.MailAccounts;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Holds settings policies in memory, deciding a commit the way the store's own transaction does.</summary>
/// <param name="judge">What a commit asks about each governed user's record, or <see langword="null" /> where a test holds no user.</param>
/// <param name="accountJudge">What a commit asks about each governed mail account, or <see langword="null" /> where a test holds none.</param>
/// <remarks>
/// <para>
/// A state fake rather than a substitute, because what a test of the administration is about is the version a write
/// was composed over and the version it finds: a commit here matches the policy at the expected version or matches
/// nothing, so the attempt that commits second is refused whoever made it.
/// </para>
/// <para>
/// It holds users and mail accounts as well, since a policy write is judged against the records it governs and
/// rewrites the endpoint switches on a user's row, and the queryable settings on an account's, in the same commit. The
/// steps are the store's, in its order: the bound on how many records one write judges, then the judgement of every
/// governed record, then the rows the policy moves, then the policy.
/// </para>
/// </remarks>
internal sealed class InMemorySettingsPolicies(
    IUserRecordPolicyJudge? judge = null,
    IMailAccountPolicyJudge? accountJudge = null) : ISettingsPolicyStore
{
    private readonly Dictionary<Guid, SettingsPolicyDocument> policies = [];
    private readonly HashSet<Guid> organizations = [];
    private readonly List<HeldUser> users = [];
    private readonly List<HeldAccount> accounts = [];

    /// <summary>Holds policies whose writes are judged by the deployment's own judges, over the binder a record is bound by.</summary>
    /// <param name="scanning">The deployment's own scanning section, which an account's block may only tighten; the default scans nothing.</param>
    /// <returns>The store.</returns>
    internal static InMemorySettingsPolicies JudgingRecords(SensitiveContentOptions? scanning = null)
    {
        var binder = new UserAccountDocumentBinder(
            new PersistedSecretMaterial(DeclaredSecretScheme.Registered),
            new FakeTimeProvider(),
            Options.Create(scanning ?? new SensitiveContentOptions()));

        return new InMemorySettingsPolicies(new UserRecordPolicyJudge(binder), new MailAccountPolicyJudge(binder));
    }

    /// <summary>Gets or sets what happens between a candidate being judged and its commit, which is where another administrator's write lands.</summary>
    internal Action? BeforeCommit { get; set; }

    /// <summary>Gets how many commits moved a policy.</summary>
    internal int Commits { get; private set; }

    /// <summary>Gets which records the last commit attempted was asked to judge, and under what bounds.</summary>
    internal GovernedRecordsToJudge? LastJudged { get; private set; }

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

    /// <summary>Records a mail account the deployment holds, whose columns no policy write has rewritten yet.</summary>
    /// <param name="account">The account as its row holds it.</param>
    /// <param name="organizationId">The organization it belongs to, or <see langword="null" /> for none.</param>
    /// <param name="assignedTo">The users it is assigned to.</param>
    internal void HoldingAccount(MailAccountRecord account, Guid? organizationId, params UserId[] assignedTo) =>
        this.accounts.Add(new HeldAccount(account, organizationId, assignedTo));

    /// <summary>Reads the queryable settings a policy write wrote on one account's row.</summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The settings, or <see langword="null" /> where no policy write has rewritten them.</returns>
    internal MailAccountQueryableSettings? SettingsOf(Guid accountId) =>
        this.accounts.Single(held => held.Account.Id == accountId).Settings;

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
        GovernedRecordsToJudge judged,
        CancellationToken cancellationToken)
    {
        this.BeforeCommit?.Invoke();
        this.LastJudged = judged;

        if (!this.Holds(organizationId) || this.PolicyOf(organizationId).Version != expectedVersion)
        {
            return Task.FromResult(new SettingsPolicyCommit(SettingsPolicyCommitResult.Superseded));
        }

        var candidate = new SettingsPolicyDocument(organizationId, json, expectedVersion + 1);

        if (this.JudgeAndProject(candidate, judged) is { } refused)
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

    private SettingsPolicyCommit? JudgeAndProject(SettingsPolicyDocument candidate, GovernedRecordsToJudge judged)
    {
        bool IsGoverned(Guid? organizationId) => candidate.OrganizationId is null || organizationId == candidate.OrganizationId;

        HeldUser[] governedUsers = judged.Users ? [.. this.users.Where(user => IsGoverned(user.OrganizationId))] : [];
        HeldAccount[] governedAccounts = judged.MailAccounts ? [.. this.accounts.Where(account => IsGoverned(account.OrganizationId))] : [];
        var governed = governedUsers.Length + governedAccounts.Length;

        if (governed > judged.Maximum)
        {
            return new SettingsPolicyCommit(SettingsPolicyCommitResult.TooManyRecords, GovernedRecords: governed);
        }

        if (governed == 0)
        {
            return null;
        }

        GoverningSettingsPolicies Governing(Guid? recordOrganization, bool written) => new(
            written && candidate.OrganizationId is null ? candidate : this.PolicyOf(organizationId: null),
            recordOrganization is not { } organization
                ? null
                : written && candidate.OrganizationId == organization ? candidate : this.PolicyOf(organization));

        var userJudgements = governedUsers.Length == 0
            ? []
            : (judge ?? throw new InvalidOperationException("A policy write that governs users is judged, and this store was given no judge."))
                .Judge([.. governedUsers.Select(user => new GovernedUserRecord(
                    user.User,
                    user.DisplayName,
                    user.Json,
                    user.EndpointAccess,
                    Governing(user.OrganizationId, written: false),
                    Governing(user.OrganizationId, written: true)))]);

        var accountJudgements = governedAccounts.Length == 0
            ? []
            : (accountJudge ?? throw new InvalidOperationException("A policy write that governs mail accounts is judged, and this store was given no judge."))
                .Judge(
                    [
                        .. governedAccounts.Select(account => new GovernedMailAccount(
                            account.Account,
                            account.AssignedTo,
                            Governing(account.OrganizationId, written: false),
                            Governing(account.OrganizationId, written: true))),
                    ],
                    judged.UnderANarrowerGrant);

        var judgedUsers = governedUsers.Zip(userJudgements).ToArray();
        var judgedAccounts = governedAccounts.Zip(accountJudgements).ToArray();

        if (SettingsPolicyCommit.RefusedOver(
                governed,
                [
                    .. judgedUsers
                        .Where(user => user.Second.Refusals.Count > 0)
                        .Select(user => new SettingsPolicyRecordInTheWay(user.First.User.Value, user.First.DisplayName, user.Second.Refusals)),
                    .. judgedAccounts
                        .Where(account => account.Second.Refusals.Count > 0)
                        .Select(account => new SettingsPolicyRecordInTheWay(
                            account.First.Account.Id,
                            account.First.Account.DisplayName,
                            account.Second.Refusals)
                        {
                            Kind = SettingsPolicyRecordKind.MailAccount,
                        }),
                ]) is { } refused)
        {
            return refused;
        }

        foreach (var (user, judgement) in judgedUsers)
        {
            user.EndpointAccess = judgement.EndpointAccess ?? user.EndpointAccess;
        }

        foreach (var (account, judgement) in judgedAccounts)
        {
            account.Settings = judgement.Settings ?? account.Settings;
        }

        return null;
    }

    /// <summary>One mail account's row as a policy write reaches it.</summary>
    private sealed record HeldAccount(MailAccountRecord Account, Guid? OrganizationId, IReadOnlyList<UserId> AssignedTo)
    {
        /// <summary>Gets or sets the queryable settings a policy write wrote on the row.</summary>
        public MailAccountQueryableSettings? Settings { get; set; }
    }

    /// <summary>One user's row as a policy write reaches it.</summary>
    private sealed record HeldUser(UserId User, string DisplayName, Guid? OrganizationId, string Json)
    {
        /// <summary>Gets or sets the switches the row carries, which a policy write rewrites.</summary>
        public UserEndpointAccess EndpointAccess { get; set; }
    }
}
