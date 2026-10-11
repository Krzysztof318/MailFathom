// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Settings;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Reads and commits settings policies in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// A write is one transaction under the lock that excludes every write of a governed record. Inside it the policy is
/// checked against the version the candidate was composed over, the user records it governs are judged and their
/// endpoint switches derived where the candidate changes what a user is served on, and the policy is then stored: a
/// first policy as an insert the scope's unique index admits once, a later one as an update conditional on the
/// version it replaces. Two replicas reaching one scope wait on the lock in turn, and the second finds the version
/// moved.
/// </para>
/// <para>
/// A read is bounded where it is asked: the statement measures the document and sends it only when it is within what
/// this build reads a policy from, so a row written beside MailFathom is refused rather than transferred.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedSettingsPolicies(
    MailFathomDbContext dbContext,
    IUserRecordPolicyJudge judge,
    TimeProvider timeProvider)
    : ISettingsPolicyStore
{
    /// <inheritdoc />
    public async Task<SettingsPolicyDocument?> ReadAsync(Guid? organizationId, CancellationToken cancellationToken)
    {
        // Asked first rather than inferred from the absence of a row, because an organization that states nothing
        // holds no row either, and the two are opposite answers to whoever asked.
        if (organizationId is { } organization
            && !await dbContext.Organizations.AnyAsync(held => held.Id == organization, cancellationToken))
        {
            return null;
        }

        var stored = await StoredPolicies(dbContext)
            .Where(policy => policy.OrganizationId == organizationId)
            .FirstOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return SettingsPolicyDocument.Unwritten(organizationId);
        }

        return stored.Document is { } document
            ? new SettingsPolicyDocument(organizationId, document, stored.Version)
            : throw Unreadable(organizationId, stored.Length);
    }

    /// <inheritdoc />
    public async Task<SettingsPolicyCommit> CommitAsync(
        Guid? organizationId,
        string json,
        long expectedVersion,
        int? maximumUserRecordsJudged,
        CancellationToken cancellationToken)
    {
        RefuseWhatCannotBeCommitted(json, expectedVersion);

        if (maximumUserRecordsJudged is { } stated)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stated, nameof(maximumUserRecordsJudged));
        }

        var now = timeProvider.GetUtcNow();

        try
        {
            await using var write = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            await dbContext.Database.ExecuteSqlRawAsync(
                GoverningSettingsPolicyStatements.HoldAgainstRecordWrites,
                cancellationToken);

            if (await this.ReadAsync(organizationId, cancellationToken) is not { } standing
                || standing.Version != expectedVersion)
            {
                return new SettingsPolicyCommit(SettingsPolicyCommitResult.Superseded);
            }

            var candidate = new SettingsPolicyDocument(organizationId, json, expectedVersion + 1);

            if (maximumUserRecordsJudged is { } maximum
                && await this.JudgeAndProjectAsync(standing, candidate, maximum, cancellationToken) is { } refused)
            {
                return refused;
            }

            var written = expectedVersion == SettingsPolicyDocument.UnwrittenVersion
                ? await this.InsertFirstAsync(organizationId, json, now, cancellationToken)
                : await dbContext.SettingsPolicies
                    .Where(policy => policy.OrganizationId == organizationId && policy.Version == expectedVersion)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(policy => policy.Document, json)
                            .SetProperty(policy => policy.Version, policy => policy.Version + 1)
                            .SetProperty(policy => policy.UpdatedAt, now),
                        cancellationToken);

            if (written != 1)
            {
                return new SettingsPolicyCommit(SettingsPolicyCommitResult.Superseded);
            }

            await write.CommitAsync(cancellationToken);

            return new SettingsPolicyCommit(SettingsPolicyCommitResult.Committed, candidate.Version);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // The organization was removed between the caller's reading of its policy and this statement. Reported as
            // a write that did not land rather than raised, because the caller re-reads either way and that read is
            // what says there is no longer an organization to write for.
            return new SettingsPolicyCommit(SettingsPolicyCommitResult.Superseded);
        }
    }

    /// <summary>Composes the bounded read of every stored policy, which a caller narrows to the scopes it asks about.</summary>
    /// <remarks>
    /// The cast is what makes both the measurement and the value the text a reader parses, and the length decides in
    /// the one statement whether the document is sent at all. A scope is matched by the query composed over this,
    /// which is what reads a deployment's row — the one naming no organization — as a row.
    /// </remarks>
    private static IQueryable<StoredSettingsPolicyRow> StoredPolicies(MailFathomDbContext dbContext) =>
        dbContext.Database.SqlQuery<StoredSettingsPolicyRow>(
            $"""
            SELECT
                "OrganizationId",
                octet_length("Document"::text) AS "Length",
                CASE WHEN octet_length("Document"::text) <= {SettingsPolicyDocument.MaximumOctets} THEN "Document"::text END AS "Document",
                "Version"
            FROM settings_policies
            """);

    /// <summary>Reads the policies that govern a record of one organization, or of none, on the context's own connection.</summary>
    /// <param name="dbContext">The context, inside the transaction of the write that asks.</param>
    /// <param name="organizationId">The organization the record belongs to, or <see langword="null" /> for none.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The deployment's policy and, for a record in an organization, that organization's; each stating nothing where its scope stores none, and each empty text where its row is past what this build reads a policy from.</returns>
    internal static async Task<GoverningSettingsPolicies> ReadGoverningAsync(
        MailFathomDbContext dbContext,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var stored = await StoredPolicies(dbContext)
            .Where(policy => policy.OrganizationId == null || policy.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        SettingsPolicyDocument Held(Guid? scope) =>
            stored.FirstOrDefault(policy => policy.OrganizationId == scope) is { } row
                ? new SettingsPolicyDocument(scope, row.Document ?? string.Empty, row.Version)
                : SettingsPolicyDocument.Unwritten(scope);

        return new GoverningSettingsPolicies(Held(null), organizationId is null ? null : Held(organizationId));
    }

    private static SettingsPolicyUnreadableException Unreadable(Guid? organizationId, int length) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"The settings policy of {(organizationId is { } named ? $"organization {named:D}" : "the deployment")} is {length} octets, past the {SettingsPolicyDocument.MaximumOctets} MailFathom reads a settings policy from, so it was not read. A policy states defaults and forced values rather than carrying a payload: check what wrote the settings_policies row."));

    /// <summary>Judges every user record the candidate governs, and writes the endpoint switches of each one it moves.</summary>
    /// <returns>The refusal that stops the write, or <see langword="null" /> where the candidate may be stored.</returns>
    /// <remarks>
    /// Counted before anything is read, so a policy governing more records than one write judges costs a count and
    /// no document. The switches are written per value rather than per user: there are four pairs a row can hold, so
    /// a policy that moves ten thousand users issues at most four statements.
    /// </remarks>
    private async Task<SettingsPolicyCommit?> JudgeAndProjectAsync(
        SettingsPolicyDocument standing,
        SettingsPolicyDocument candidate,
        int maximum,
        CancellationToken cancellationToken)
    {
        var scope = candidate.OrganizationId;
        var governed = await dbContext.UserAccounts
            .CountAsync(user => scope == null || user.OrganizationId == scope, cancellationToken);

        if (governed > maximum)
        {
            return new SettingsPolicyCommit(SettingsPolicyCommitResult.TooManyRecords, GovernedRecords: governed);
        }

        var users = await GovernedUserRows.Of(dbContext)
            .Where(user => scope == null || user.OrganizationId == scope)
            .OrderBy(user => user.Id)
            .ToListAsync(cancellationToken);

        var others = await this.ReadTheOtherScopesAsync(scope, users, cancellationToken);

        var judgements = judge.Judge(
        [
            .. users.Select(user => new GovernedUserRecord(
                UserId.Create(user.Id),
                user.DisplayName,
                user.Document,
                new UserEndpointAccess(user.McpEndpointEnabled, user.ClientEndpointEnabled),
                GoverningOf(user, standing, others),
                GoverningOf(user, candidate, others))),
        ]);

        if (GovernedUserJudgements.FindRecordsInTheWay(users, judgements) is { } refused)
        {
            return refused;
        }

        // The rows alone, with no version moved: a record's version says the record changed, and what moved here is
        // the policy, whose own version is what has every replica compose these users again.
        foreach (var (served, rows) in GovernedUserJudgements.FindMovedSwitches(users, judgements))
        {
            await dbContext.UserAccounts
                .Where(user => rows.Contains(user.Id))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(user => user.McpEndpointEnabled, served.McpEndpoint)
                        .SetProperty(user => user.ClientEndpointEnabled, served.ClientEndpoint),
                    cancellationToken);
        }

        return null;
    }

    /// <summary>Reads the policies of the scopes a write does not replace, which the records it judges are governed by as well.</summary>
    /// <returns>For an organization's write, the deployment's policy alone; for the deployment's, the policy of every organization a judged user belongs to. The deployment's is keyed by the empty identifier.</returns>
    /// <remarks>
    /// <para>
    /// Only the organizations of the users being judged are read, so what the deployment's write reads is bounded by
    /// the records it was already allowed to judge rather than by how many organizations store a policy.
    /// </para>
    /// <para>
    /// A policy past what this build reads one from is answered as empty text, as it is to every other reader, which
    /// no build judges as a policy: its users are then judged as unreadable under the policy that stands and under
    /// the candidate alike, so they refuse nothing and their rows are left as they are, instead of one organization's
    /// row stopping a write for the whole deployment.
    /// </para>
    /// </remarks>
    private async Task<Dictionary<Guid, SettingsPolicyDocument>> ReadTheOtherScopesAsync(
        Guid? written,
        List<StoredGovernedUserRow> judged,
        CancellationToken cancellationToken)
    {
        Guid[] organizations = [.. judged.Select(user => user.OrganizationId).OfType<Guid>().Distinct()];
        var policies = StoredPolicies(dbContext);

        var stored = await (written is null
                ? policies.Where(policy => policy.OrganizationId != null && organizations.Contains(policy.OrganizationId.Value))
                : policies.Where(policy => policy.OrganizationId == null))
            .ToListAsync(cancellationToken);

        return stored.ToDictionary(
            policy => policy.OrganizationId ?? Guid.Empty,
            policy => new SettingsPolicyDocument(policy.OrganizationId, policy.Document ?? string.Empty, policy.Version));
    }

    /// <summary>Composes the policies governing one user from the one a write replaces and the ones it leaves.</summary>
    private static GoverningSettingsPolicies GoverningOf(
        StoredGovernedUserRow user,
        SettingsPolicyDocument written,
        Dictionary<Guid, SettingsPolicyDocument> others)
    {
        if (written.OrganizationId is not null)
        {
            return new GoverningSettingsPolicies(
                others.GetValueOrDefault(Guid.Empty, SettingsPolicyDocument.Unwritten(organizationId: null)),
                written);
        }

        return new GoverningSettingsPolicies(
            written,
            user.OrganizationId is { } organization
                ? others.GetValueOrDefault(organization, SettingsPolicyDocument.Unwritten(organization))
                : null);
    }

    /// <summary>Stores a scope's first policy, unless another write stored one first.</summary>
    /// <remarks>
    /// An insert that does nothing on conflict rather than a read followed by an insert: each side of that read would
    /// see no row, and the second to commit would violate the scope's index as a provider failure instead of being
    /// told its version was superseded.
    /// </remarks>
    private Task<int> InsertFirstAsync(
        Guid? organizationId,
        string json,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7(now);

        return dbContext.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO settings_policies ("Id", "OrganizationId", "Document", "Version", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {organizationId}, {json}::jsonb, 1, {now}, {now})
            ON CONFLICT DO NOTHING
            """,
            cancellationToken);
    }

    /// <summary>Refuses a candidate the column would store and the next read would not hand back.</summary>
    private static void RefuseWhatCannotBeCommitted(string json, long expectedVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedVersion);

        int persistedOctets;

        try
        {
            if (!RootSettingsCommitRules.RootIsAnObject(json))
            {
                throw new ArgumentException(
                    "The candidate settings policy is JSON whose root is not an object, so it states nothing a policy can state.",
                    nameof(json));
            }

            persistedOctets = RootSettingsCommitRules.PersistedOctetsOf(json);
        }
        catch (JsonException exception)
        {
            throw new ArgumentException(
                "The candidate settings policy is not JSON, so no statement is issued for it.",
                nameof(json),
                exception);
        }

        if (persistedOctets > SettingsPolicyDocument.MaximumOctets)
        {
            throw new ArgumentException(
                $"The candidate settings policy occupies {persistedOctets} octets as the database stores it, past the {SettingsPolicyDocument.MaximumOctets} this build reads a policy from, so persisting it would leave a row nobody could open again.",
                nameof(json));
        }
    }
}
