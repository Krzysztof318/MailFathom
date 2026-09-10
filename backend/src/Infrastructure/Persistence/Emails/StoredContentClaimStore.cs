// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core store of the room every replica has reserved in local content storage.</summary>
/// <remarks>
/// <para>
/// A claim is one statement that measures, decides, and reserves, because separating the three is exactly the gap this
/// exists to close: a replica that read the occupancy and then inserted would be admitting itself against a figure the
/// other replicas had already moved. What the statement measures is what the store occupies plus what every unexpired
/// claim reserves, and the two ceilings are answered from one reading so a payload cannot be admitted by a deployment
/// figure taken at one moment and a user figure taken at another.
/// </para>
/// <para>
/// The serialization is a transaction-level advisory lock rather than a row to contend on, because there is no row a
/// claim naturally belongs to — a claim is an insert, and an insert takes no lock a second insert would wait behind.
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0031-dividing-singleton-work-between-replicas-with-a-leased-scope.md">ADR 0031</see>
/// records why an advisory lock is not how a scope is held and where it is nonetheless the right answer: a short
/// critical section that touches nothing but the database, ending with the transaction whatever happened to the caller.
/// It is taken in a statement of its own because a lock acquired inside the reading statement would come too late —
/// that statement's snapshot is taken before the lock could serialize anything.
/// </para>
/// <para>
/// Every instant is the database's rather than a replica's. An expiry decides when one replica's reservation stops
/// binding the others, so a clock that drifted would have replicas disagreeing about what is reserved; taking all three
/// readings from <c>now()</c> inside one transaction leaves one clock and one moment.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredContentClaimStore(MailFathomDbContext dbContext) : IStoredContentClaimStore
{
    /// <summary>The advisory lock every claim of this deployment serializes on.</summary>
    /// <remarks>
    /// One key for the whole table rather than one per user, because the deployment ceiling is answered from every
    /// claim there is: two users claiming at once are exactly the pair a per-user key would let past each other. The
    /// value is arbitrary and only has to stay unique among whatever else this product ever locks advisorily, which is
    /// currently nothing.
    /// </remarks>
    private const long ClaimSerializationKey = 5_263_456_017_113_920_001L;

    /// <inheritdoc />
    public async Task<StoredContentClaimRecord> ClaimAsync(
        MailUserId user,
        long bytes,
        StoredContentCeilings ceilings,
        TimeSpan claimLifetime,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(claimLifetime, TimeSpan.Zero);

        if (!user.IsSpecified)
        {
            throw new ArgumentException(
                "A stored-content claim is reserved against a named user's ceiling, so a user naming nobody cannot claim.",
                nameof(user));
        }

        if (!ceilings.BoundsAnything)
        {
            return StoredContentClaimRecord.Unbounded;
        }

        var names = StoredContentClaimNames.Of(dbContext.Model);
        var claimId = Guid.CreateVersion7();

        await using var ownTransaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await dbContext.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock({0})",
            [ClaimSerializationKey],
            cancellationToken);

        var reachedBound = await dbContext.Database
            .SqlQueryRaw<int>(
                ClaimStatement(names),
                user.Value,
                bytes,
                claimId,
                claimLifetime,
                ceilings.DeploymentBytes ?? long.MaxValue,
                ceilings.UserBytes ?? long.MaxValue,
                names.ContentsTable)
            .SingleAsync(cancellationToken);

        if (ownTransaction is not null)
        {
            await ownTransaction.CommitAsync(cancellationToken);
        }

        var reached = (StoredContentBound)reachedBound;

        return reached is StoredContentBound.None
            ? new StoredContentClaimRecord(claimId, StoredContentBound.None)
            : new StoredContentClaimRecord(ClaimId: null, reached);
    }

    /// <inheritdoc />
    public Task ReleaseAsync(Guid claimId, CancellationToken cancellationToken)
    {
        return dbContext.Database.ExecuteSqlRawAsync(
            ReleaseStatement(StoredContentClaimNames.Of(dbContext.Model)),
            [claimId],
            cancellationToken);
    }

    /// <summary>The statement that removes one claim, whether or not the payload it covered was stored.</summary>
    private static string ReleaseStatement(StoredContentClaimNames names) =>
        $"DELETE FROM {names.Claims} WHERE {names.ClaimIdColumn} = {{0}}";

    /// <summary>The statement that sweeps, measures, decides, and reserves, and reports which ceiling refused.</summary>
    /// <remarks>
    /// <para>
    /// The sweep removes what has expired and is housekeeping alone: every figure below filters on the expiry anyway,
    /// so a row this statement's own snapshot still sees binds nothing. What it stops is the table growing by one row
    /// for every claim a replica died holding.
    /// </para>
    /// <para>
    /// The deployment's occupancy is PostgreSQL's own accounting of what the content table costs the disk, which is the
    /// quantity the ceiling is set against and is read from the catalogue in constant time. The user's is the maintained
    /// figure of what their payloads hold, which is the only quantity attributable to one person. Neither answers for
    /// the other, and each has that population's unexpired claims added to it.
    /// </para>
    /// <para>
    /// The verdict is reported as the bound that refused rather than as a flag, and the deployment's is reported in
    /// preference to the user's when both are reached, because raising a user's share changes nothing while the
    /// instance itself is full. The numbers it compares are written as a ceiling less the claim rather than as an
    /// occupancy plus it, so a deployment that bounds only one of the two populations passes the other's ceiling in as
    /// the widest value there is without that addition overflowing it.
    /// </para>
    /// </remarks>
    private static string ClaimStatement(StoredContentClaimNames names) => $$"""
        WITH swept AS (
            DELETE FROM {{names.Claims}}
            WHERE {{names.ClaimExpiresAtColumn}} <= now()
            RETURNING 1
        ),
        figures AS (
            SELECT
                COALESCE(pg_total_relation_size(to_regclass({6})), 0)
                    + COALESCE((
                        SELECT SUM(live.{{names.ClaimBytesColumn}})
                        FROM {{names.Claims}} AS live
                        WHERE live.{{names.ClaimExpiresAtColumn}} > now()), 0) AS deployment_held,
                COALESCE((
                    SELECT total.{{names.TotalsCountColumn}}
                    FROM {{names.Totals}} AS total
                    WHERE total.{{names.TotalsUserColumn}} = {0}), 0)
                    + COALESCE((
                        SELECT SUM(live.{{names.ClaimBytesColumn}})
                        FROM {{names.Claims}} AS live
                        WHERE live.{{names.ClaimExpiresAtColumn}} > now()
                          AND live.{{names.ClaimUserColumn}} = {0}), 0) AS user_held
        ),
        taken AS (
            INSERT INTO {{names.Claims}} (
                {{names.ClaimIdColumn}},
                {{names.ClaimUserColumn}},
                {{names.ClaimBytesColumn}},
                {{names.ClaimExpiresAtColumn}})
            SELECT {2}, {0}, {1}, now() + {3}
            FROM figures
            WHERE figures.deployment_held <= {4} - {1}
              AND figures.user_held <= {5} - {1}
            RETURNING 1
        )
        SELECT (CASE
            WHEN EXISTS (SELECT 1 FROM taken) THEN 0
            WHEN figures.deployment_held > {4} - {1} THEN 2
            ELSE 1
        END) AS "Value"
        FROM figures
        """;

    /// <summary>The identifiers the claim statement is composed from, taken from the model once.</summary>
    /// <remarks>
    /// The content table is carried unquoted beside the rest, because it reaches the statement as a parameter to
    /// <c>to_regclass</c> rather than as an identifier in it — which is the one place a table name is a value.
    /// </remarks>
    private sealed record StoredContentClaimNames(
        string Claims,
        string ClaimIdColumn,
        string ClaimUserColumn,
        string ClaimBytesColumn,
        string ClaimExpiresAtColumn,
        string Totals,
        string TotalsUserColumn,
        string TotalsCountColumn,
        string ContentsTable)
    {
        public static StoredContentClaimNames Of(IModel model)
        {
            var claims = PersistedSchemaNames.EntityTypeOf<StoredContentClaimEntity>(model);
            var totals = PersistedSchemaNames.EntityTypeOf<UserStoredContentEntity>(model);
            var contents = PersistedSchemaNames.EntityTypeOf<EmailMessageContentEntity>(model);

            return new StoredContentClaimNames(
                PersistedSchemaNames.QuotedTable(claims),
                PersistedSchemaNames.QuotedColumn(claims, nameof(StoredContentClaimEntity.Id)),
                PersistedSchemaNames.QuotedColumn(claims, nameof(StoredContentClaimEntity.UserId)),
                PersistedSchemaNames.QuotedColumn(claims, nameof(StoredContentClaimEntity.ClaimedByteCount)),
                PersistedSchemaNames.QuotedColumn(claims, nameof(StoredContentClaimEntity.ExpiresAt)),
                PersistedSchemaNames.QuotedTable(totals),
                PersistedSchemaNames.QuotedColumn(totals, nameof(UserStoredContentEntity.UserId)),
                PersistedSchemaNames.QuotedColumn(totals, nameof(UserStoredContentEntity.StoredContentByteCount)),
                contents.GetSchemaQualifiedTableName()
                    ?? throw new InvalidOperationException(
                        "Stored mail content is mapped to no table, so no claim can measure what it occupies."));
        }
    }
}
