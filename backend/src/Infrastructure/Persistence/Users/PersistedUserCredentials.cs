// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Keeps each user's credentials in PostgreSQL, one row per credential and one table for every method.</summary>
/// <remarks>
/// <para>
/// Every write here is a single statement executed against the database rather than a change-tracked edit followed by
/// <c>SaveChanges</c>, for the reason <see cref="Accounts.MailboxRefreshTokenStore" /> gives: these acts are reached on
/// paths that hold no persistence session, so saving the scoped context would commit whatever else that scope had
/// pending. It buys the property rotation needs as well — the replacement is one statement, so a request arriving while
/// it commits is judged against exactly one of the two records and the previous credential stops working at that
/// instant rather than over a window.
/// </para>
/// <para>
/// Reads are projections rather than entity graphs, and the two projections are deliberately different shapes. The
/// administrative listing selects every column but the material, so no answer composed from it can carry stored
/// material however it is later mapped; the resolution a request performs selects the material and nothing an operator
/// reads.
/// </para>
/// <para>
/// Every write names the user beside the credential, which turns an identifier copied from another user's listing
/// into <see cref="UserCredentialWriteOutcome.UnknownCredential" /> rather than into a rotation performed on somebody
/// else's credential.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedUserCredentials(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IUserCredentialStore
{
    /// <summary>How many credentials one user may hold, which is the bound the listing reads under.</summary>
    /// <remarks>Named here as well as on the listing so the statement that enforces it and the query that assumes it cannot come to disagree.</remarks>
    private const int Ceiling = UserCredential.MaximumListedPerUser;

    /// <inheritdoc />
    public async Task<ResolvedUserCredential?> FindAsync(
        UserCredentialMethod method,
        UserCredentialLookup lookup,
        CancellationToken cancellationToken)
    {
        var storedMethod = RequireMethod(method);
        var storedLookup = RequireLookup(lookup);

        var stored = await dbContext.UserCredentials
            .AsNoTracking()
            .Where(credential => credential.Method == storedMethod && credential.Lookup == storedLookup)
            .Select(credential => new
            {
                credential.Id,
                credential.UserId,
                credential.Enabled,
                credential.Permissions,
                credential.Material,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return stored is null
            ? null
            : new ResolvedUserCredential(
                stored.Id,
                MailUserId.Create(stored.UserId),
                method,
                GrantOf(stored.Permissions),
                stored.Enabled,
                stored.Material);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserCredential>> ReadForUserAsync(
        MailUserId user,
        CancellationToken cancellationToken)
    {
        var storedUserId = RequireUser(user);

        var stored = await dbContext.UserCredentials
            .AsNoTracking()
            .Where(credential => credential.UserId == storedUserId)
            .OrderBy(credential => credential.CreatedAt)
            .ThenBy(credential => credential.Id)
            .Take(UserCredential.MaximumListedPerUser)
            .Select(credential => new
            {
                credential.Id,
                credential.Method,
                credential.Lookup,
                credential.Permissions,
                credential.Enabled,
                credential.Version,
                credential.CreatedAt,
                credential.MaterialChangedAt,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. stored
                .Where(credential => UserCredentialMethod.TryParse(credential.Method, out _))
                .Select(credential => new UserCredential(
                    credential.Id,
                    user,
                    MethodOf(credential.Method),
                    LookupOf(credential.Lookup),
                    GrantOf(credential.Permissions),
                    credential.Enabled,
                    credential.Version,
                    credential.CreatedAt,
                    credential.MaterialChangedAt)),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The insert states the user and the ceiling as subqueries and lets the unique index answer for the lookup, so
    /// all three refusals are decided by the database rather than by reads this method took a moment earlier. What the
    /// reads below decide is only which of the three happened, on the path where nothing was written — so a concurrent
    /// user deletion, a concurrent provisioning, and two administrators racing at the ceiling are each answered rather
    /// than raised.
    /// </para>
    /// <para>
    /// The user's row is locked in a statement of its own before that insert, which is what makes the ceiling hold
    /// against a second administrator provisioning at the same instant. A lock taken inside the insert would not: under
    /// <c>READ COMMITTED</c> a statement that waits on a row lock re-reads the locked row and leaves every other table
    /// on the snapshot it started with, so the count would still be the one taken before the winner committed and both
    /// callers would write the hundredth credential. Locking first is what gives the insert a snapshot taken after that
    /// commit. It is the one write here that opens a transaction, because a ceiling cannot be made idempotent — a
    /// second attempt from a fresh read is a second credential rather than the same one — so the retry policy has
    /// nothing to converge on and the decision has to hold the row it was taken against.
    /// </para>
    /// </remarks>
    public async Task<UserCredentialWriteOutcome> CreateAsync(
        Guid credentialId,
        MailUserId user,
        UserCredentialMethod method,
        UserCredentialLookup lookup,
        string? material,
        IReadOnlyList<MailFathomPermission> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var storedUserId = RequireUser(user);
        var storedCredentialId = RequireCredential(credentialId);
        var storedMethod = RequireMethod(method);
        var storedLookup = RequireLookup(lookup);
        var storedMaterial = RequireMaterialAgreesWithMethod(method, material);
        var storedPermissions = StoredGrant(permissions);
        var provisionedAt = timeProvider.GetUtcNow();

        await using var provisioning = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Locks nothing when the user is gone, which the insert below then answers as an unknown user.
        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM settings_accounts WHERE "Id" = {storedUserId} FOR UPDATE""",
            cancellationToken);

        // The identifiers are quoted because EF Core names the columns after the properties, which PostgreSQL would
        // otherwise fold to lower case and fail to find.
        var written = await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO user_credentials
                 ("Id", "UserId", "Method", "Lookup", "Material", "Permissions", "Enabled", "Version", "CreatedAt", "MaterialChangedAt")
             SELECT {storedCredentialId}, {storedUserId}, {storedMethod}, {storedLookup}, {storedMaterial}, {storedPermissions}, TRUE, 1, {provisionedAt}, {provisionedAt}
             WHERE EXISTS (SELECT 1 FROM settings_accounts WHERE "Id" = {storedUserId})
               AND (SELECT COUNT(*) FROM user_credentials WHERE "UserId" = {storedUserId}) < {Ceiling}
             ON CONFLICT ("Method", "Lookup") DO NOTHING
             """,
            cancellationToken);

        await provisioning.CommitAsync(cancellationToken);

        if (written == 1)
        {
            return UserCredentialWriteOutcome.Written;
        }

        if (!await dbContext.UserAccounts
                .AsNoTracking()
                .AnyAsync(userAccount => userAccount.Id == storedUserId, cancellationToken))
        {
            return UserCredentialWriteOutcome.UnknownUser;
        }

        return await dbContext.UserCredentials
            .AsNoTracking()
            .CountAsync(credential => credential.UserId == storedUserId, cancellationToken) >= Ceiling
            ? UserCredentialWriteOutcome.UserAtCredentialCeiling
            : UserCredentialWriteOutcome.LookupTaken;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The method is in the predicate as well as the credential, so a rotation aimed at the wrong kind of credential
    /// answers that no such credential exists rather than writing a password's record into a row a key is resolved by.
    /// The lookup moves with the material for the two methods whose material it follows — a key this deployment mints
    /// and a client's public key — which is why the unique index can be violated here and is answered as the taken
    /// lookup it is.
    /// <para>
    /// Where it does <em>not</em> move it is in the predicate too, because there the caller states a value the row is
    /// meant to already carry. A username the credential does not hold then matches no row and answers
    /// <see cref="UserCredentialWriteOutcome.UnknownCredential" />, rather than renaming somebody's sign-in to
    /// whatever was typed — which the update below would otherwise do, since it sets the lookup unconditionally. It is
    /// <see cref="UserCredentialMethod.LookupMovesWithTheMaterial" /> that decides, rather than whether the lookup is
    /// derived from the secret: a client's fingerprint is published and still moves, so matching on it would demand the
    /// row already carry the value the rotation is there to write.
    /// </para>
    /// </remarks>
    public async Task<UserCredentialWriteOutcome> ReplaceMaterialAsync(
        MailUserId user,
        Guid credentialId,
        UserCredentialMethod method,
        UserCredentialLookup lookup,
        string? material,
        CancellationToken cancellationToken)
    {
        var storedUserId = RequireUser(user);
        var storedCredentialId = RequireCredential(credentialId);
        var storedMethod = RequireMethod(method);
        var storedLookup = RequireLookup(lookup);
        var storedMaterial = RequireMaterialAgreesWithMethod(method, material);
        var statedLookup = method.LookupMovesWithTheMaterial ? null : storedLookup;
        var changedAt = timeProvider.GetUtcNow();

        try
        {
            var written = await dbContext.UserCredentials
                .Where(credential => credential.Id == storedCredentialId
                    && credential.UserId == storedUserId
                    && credential.Method == storedMethod
                    && (statedLookup == null || credential.Lookup == statedLookup))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(credential => credential.Lookup, storedLookup)
                        .SetProperty(credential => credential.Material, storedMaterial)
                        .SetProperty(credential => credential.MaterialChangedAt, changedAt)
                        .SetProperty(credential => credential.Version, credential => credential.Version + 1),
                    cancellationToken);

            return OutcomeOf(written);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return UserCredentialWriteOutcome.LookupTaken;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The instant the material changed at is deliberately left where it is, which is the whole difference between this
    /// and <see cref="ReplaceMaterialAsync" />. The verified record is named in the predicate as well as the credential,
    /// so a rotation that committed while this request was deriving leaves nothing here to write over: the update
    /// matches no row, answers <see cref="UserCredentialWriteOutcome.UnknownCredential" />, and the caller drops the
    /// rehash rather than putting the superseded material back.
    /// </remarks>
    public async Task<UserCredentialWriteOutcome> RewriteMaterialAsync(
        MailUserId user,
        Guid credentialId,
        string verifiedMaterial,
        string material,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verifiedMaterial);
        ArgumentNullException.ThrowIfNull(material);

        var storedUserId = RequireUser(user);
        var storedCredentialId = RequireCredential(credentialId);

        var written = await dbContext.UserCredentials
            .Where(credential => credential.Id == storedCredentialId
                && credential.UserId == storedUserId
                && credential.Material == verifiedMaterial)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.Material, material)
                    .SetProperty(credential => credential.Version, credential => credential.Version + 1),
                cancellationToken);

        return OutcomeOf(written);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// <b>Disabling writes the row and ends that credential's client sessions in one commit</b>, which is what makes it
    /// an act rather than a race. A session is verified against the sessions table rather than against this row, so the
    /// two writes have to be indivisible: between an update that committed and a removal that had not, a client would
    /// go on being admitted by a credential an operator was told is off. The exchange and the renewal close the same
    /// window from their own side, taking a share lock on this row and requiring it to still be enabled before they
    /// write, so a session arriving beside this transaction waits for it and is then removed by it.
    /// </para>
    /// <para>
    /// The order is this row and then the sessions beneath it, which is the order every other path takes:
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0033-where-a-signed-in-session-lives-so-every-replica-accepts-it.md">ADR 0033</see>
    /// requires the user row, then the credential row, then the session row over all four of them, and what an
    /// inversion costs is a deadlock PostgreSQL breaks by aborting a renewal, a disable, or an erasure outright.
    /// </para>
    /// <para>
    /// Enabling takes no transaction and ends nothing. There is no session to end — the credential was not
    /// authenticating anything — and a credential turned back on is signed in with rather than refused, this table
    /// having no window to wait out.
    /// </para>
    /// </remarks>
    public async Task<UserCredentialWriteOutcome> SetEnabledAsync(
        MailUserId user,
        Guid credentialId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var storedUserId = RequireUser(user);
        var storedCredentialId = RequireCredential(credentialId);

        if (enabled)
        {
            return OutcomeOf(await SetEnablementAsync(
                dbContext,
                storedUserId,
                storedCredentialId,
                enabled: true,
                cancellationToken));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var written = await SetEnablementAsync(
            dbContext,
            storedUserId,
            storedCredentialId,
            enabled: false,
            cancellationToken);

        if (written == 1)
        {
            await dbContext.ClientSessions
                .Where(session => session.CredentialId == storedCredentialId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return OutcomeOf(written);
    }

    /// <summary>Writes whether one credential authenticates requests, and reports how many rows that named.</summary>
    private static Task<int> SetEnablementAsync(
        MailFathomDbContext dbContext,
        Guid storedUserId,
        Guid storedCredentialId,
        bool enabled,
        CancellationToken cancellationToken) =>
        dbContext.UserCredentials
            .Where(credential => credential.Id == storedCredentialId && credential.UserId == storedUserId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.Enabled, enabled)
                    .SetProperty(credential => credential.Version, credential => credential.Version + 1),
                cancellationToken);

    /// <inheritdoc />
    public async Task<UserCredentialWriteOutcome> DeleteAsync(
        MailUserId user,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        var storedUserId = RequireUser(user);
        var storedCredentialId = RequireCredential(credentialId);

        var removed = await dbContext.UserCredentials
            .Where(credential => credential.Id == storedCredentialId && credential.UserId == storedUserId)
            .ExecuteDeleteAsync(cancellationToken);

        return OutcomeOf(removed);
    }

    /// <summary>Reads a statement's row count as what the act did.</summary>
    /// <remarks>
    /// One row is the act; none is a user holding no credential under that identifier, which covers a mistyped
    /// identifier, one belonging to a different user, one of another method, and one deleted between a listing and
    /// this call. More than one cannot happen, because every statement here is bounded by the primary key.
    /// </remarks>
    private static UserCredentialWriteOutcome OutcomeOf(int rowsWritten) =>
        rowsWritten == 1 ? UserCredentialWriteOutcome.Written : UserCredentialWriteOutcome.UnknownCredential;

    /// <summary>Reads a stored grant back into the permissions it names.</summary>
    /// <remarks>
    /// A name this release does not publish is dropped rather than raising, and the direction is deliberate: a row
    /// written by a release that published a permission this one withdrew must not stop a credential working, and
    /// admitting a name nothing enforces would be a grant that says more than the deployment can do. The published
    /// order is restored here, because a grant is a set and two rows written in two orders are one grant.
    /// </remarks>
    private static IReadOnlyList<MailFathomPermission> GrantOf(string[] storedPermissions)
    {
        var granted = new HashSet<MailFathomPermission>();

        foreach (var storedPermission in storedPermissions)
        {
            if (MailFathomPermission.TryParse(storedPermission, out var permission))
            {
                granted.Add(permission);
            }
        }

        return [.. MailFathomPermission.All.Where(granted.Contains)];
    }

    private static string[] StoredGrant(IReadOnlyList<MailFathomPermission> permissions) =>
    [
        .. permissions.Select(permission => permission.IsSpecified
            ? permission.Name
            : throw new ArgumentException(
                "A credential grants published permissions.",
                nameof(permissions))),
    ];

    private static UserCredentialMethod MethodOf(string storedMethod) =>
        UserCredentialMethod.TryParse(storedMethod, out var method)
            ? method
            : throw new InvalidOperationException(
                $"The stored credential method '{storedMethod}' is not one this release publishes.");

    private static UserCredentialLookup LookupOf(string storedLookup) =>
        UserCredentialLookup.TryCreate(storedLookup, out var lookup)
            ? lookup
            : throw new InvalidOperationException("A stored credential lookup is not one this release can read.");

    /// <summary>Refuses material a method does not keep, and the absence of material a method requires.</summary>
    /// <remarks>
    /// The column is nullable because two of the four methods keep nothing in it, and this is what stops that being a
    /// column anything may leave empty: a password credential written without its record would authenticate nobody and
    /// look exactly like one that had been provisioned, and a key credential written with one would keep material the
    /// method promises never to store.
    /// </remarks>
    private static string? RequireMaterialAgreesWithMethod(UserCredentialMethod method, string? material) =>
        method.StoresMaterial == (material is not null)
            ? material
            : throw new ArgumentException(
                method.StoresMaterial
                    ? $"A '{method.Name}' credential is stored with the material it is judged against."
                    : $"A '{method.Name}' credential stores no material, and what it is resolved by is its lookup.",
                nameof(material));

    private static string RequireMethod(UserCredentialMethod method) => method.IsSpecified
        ? method.Name
        : throw new ArgumentException("A credential is presented by a named method.", nameof(method));

    private static string RequireLookup(UserCredentialLookup lookup) => lookup.IsSpecified
        ? lookup.Value
        : throw new ArgumentException("A credential is resolved by a stated lookup.", nameof(lookup));

    private static Guid RequireUser(MailUserId user) => user.IsSpecified
        ? user.Value
        : throw new ArgumentException("A credential belongs to a named user.", nameof(user));

    private static Guid RequireCredential(Guid credentialId) => credentialId != Guid.Empty
        ? credentialId
        : throw new ArgumentException("A credential is named by a generated identifier.", nameof(credentialId));
}
