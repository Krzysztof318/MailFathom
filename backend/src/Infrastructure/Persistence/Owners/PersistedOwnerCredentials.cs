// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Keeps each owner's credentials in PostgreSQL, one row per credential and one table for every method.</summary>
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
/// Every write names the owner beside the credential, which turns an identifier copied from another owner's listing
/// into <see cref="OwnerCredentialWriteOutcome.UnknownCredential" /> rather than into a rotation performed on somebody
/// else's credential.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedOwnerCredentials(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IOwnerCredentialStore
{
    /// <summary>How many credentials one owner may hold, which is the bound the listing reads under.</summary>
    /// <remarks>Named here as well as on the listing so the statement that enforces it and the query that assumes it cannot come to disagree.</remarks>
    private const int Ceiling = OwnerCredential.MaximumListedPerOwner;

    /// <inheritdoc />
    public async Task<ResolvedOwnerCredential?> FindAsync(
        OwnerCredentialMethod method,
        OwnerCredentialLookup lookup,
        CancellationToken cancellationToken)
    {
        var storedMethod = RequireMethod(method);
        var storedLookup = RequireLookup(lookup);

        var stored = await dbContext.OwnerCredentials
            .AsNoTracking()
            .Where(credential => credential.Method == storedMethod && credential.Lookup == storedLookup)
            .Select(credential => new
            {
                credential.Id,
                credential.OwnerId,
                credential.Enabled,
                credential.Permissions,
                credential.Material,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return stored is null
            ? null
            : new ResolvedOwnerCredential(
                stored.Id,
                MailOwnerId.Create(stored.OwnerId),
                method,
                GrantOf(stored.Permissions),
                stored.Enabled,
                stored.Material);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OwnerCredential>> ReadForOwnerAsync(
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var storedOwnerId = RequireOwner(owner);

        var stored = await dbContext.OwnerCredentials
            .AsNoTracking()
            .Where(credential => credential.OwnerId == storedOwnerId)
            .OrderBy(credential => credential.CreatedAt)
            .ThenBy(credential => credential.Id)
            .Take(OwnerCredential.MaximumListedPerOwner)
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
                .Where(credential => OwnerCredentialMethod.TryParse(credential.Method, out _))
                .Select(credential => new OwnerCredential(
                    credential.Id,
                    owner,
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
    /// The insert states the owner and the ceiling as subqueries and lets the unique index answer for the lookup, so
    /// all three refusals are decided by the database rather than by reads this method took a moment earlier. What the
    /// reads below decide is only which of the three happened, on the path where nothing was written — so a concurrent
    /// owner deletion, a concurrent provisioning, and two administrators racing at the ceiling are each answered rather
    /// than raised.
    /// </para>
    /// <para>
    /// The owner's row is locked in a statement of its own before that insert, which is what makes the ceiling hold
    /// against a second administrator provisioning at the same instant. A lock taken inside the insert would not: under
    /// <c>READ COMMITTED</c> a statement that waits on a row lock re-reads the locked row and leaves every other table
    /// on the snapshot it started with, so the count would still be the one taken before the winner committed and both
    /// callers would write the hundredth credential. Locking first is what gives the insert a snapshot taken after that
    /// commit. It is the one write here that opens a transaction, because a ceiling cannot be made idempotent — a
    /// second attempt from a fresh read is a second credential rather than the same one — so the retry policy has
    /// nothing to converge on and the decision has to hold the row it was taken against.
    /// </para>
    /// </remarks>
    public async Task<OwnerCredentialWriteOutcome> CreateAsync(
        Guid credentialId,
        MailOwnerId owner,
        OwnerCredentialMethod method,
        OwnerCredentialLookup lookup,
        string? material,
        IReadOnlyList<MailFathomPermission> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var storedOwnerId = RequireOwner(owner);
        var storedCredentialId = RequireCredential(credentialId);
        var storedMethod = RequireMethod(method);
        var storedLookup = RequireLookup(lookup);
        var storedMaterial = RequireMaterialAgreesWithMethod(method, material);
        var storedPermissions = StoredGrant(permissions);
        var provisionedAt = timeProvider.GetUtcNow();

        await using var provisioning = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        // Locks nothing when the owner is gone, which the insert below then answers as an unknown owner.
        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM settings_accounts WHERE "Id" = {storedOwnerId} FOR UPDATE""",
            cancellationToken);

        // The identifiers are quoted because EF Core names the columns after the properties, which PostgreSQL would
        // otherwise fold to lower case and fail to find.
        var written = await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO owner_credentials
                 ("Id", "OwnerId", "Method", "Lookup", "Material", "Permissions", "Enabled", "Version", "CreatedAt", "MaterialChangedAt")
             SELECT {storedCredentialId}, {storedOwnerId}, {storedMethod}, {storedLookup}, {storedMaterial}, {storedPermissions}, TRUE, 1, {provisionedAt}, {provisionedAt}
             WHERE EXISTS (SELECT 1 FROM settings_accounts WHERE "Id" = {storedOwnerId})
               AND (SELECT COUNT(*) FROM owner_credentials WHERE "OwnerId" = {storedOwnerId}) < {Ceiling}
             ON CONFLICT ("Method", "Lookup") DO NOTHING
             """,
            cancellationToken);

        await provisioning.CommitAsync(cancellationToken);

        if (written == 1)
        {
            return OwnerCredentialWriteOutcome.Written;
        }

        if (!await dbContext.OwnerAccounts
                .AsNoTracking()
                .AnyAsync(ownerAccount => ownerAccount.Id == storedOwnerId, cancellationToken))
        {
            return OwnerCredentialWriteOutcome.UnknownOwner;
        }

        return await dbContext.OwnerCredentials
            .AsNoTracking()
            .CountAsync(credential => credential.OwnerId == storedOwnerId, cancellationToken) >= Ceiling
            ? OwnerCredentialWriteOutcome.OwnerAtCredentialCeiling
            : OwnerCredentialWriteOutcome.LookupTaken;
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
    /// <see cref="OwnerCredentialWriteOutcome.UnknownCredential" />, rather than renaming somebody's sign-in to
    /// whatever was typed — which the update below would otherwise do, since it sets the lookup unconditionally. It is
    /// <see cref="OwnerCredentialMethod.LookupMovesWithTheMaterial" /> that decides, rather than whether the lookup is
    /// derived from the secret: a client's fingerprint is published and still moves, so matching on it would demand the
    /// row already carry the value the rotation is there to write.
    /// </para>
    /// </remarks>
    public async Task<OwnerCredentialWriteOutcome> ReplaceMaterialAsync(
        MailOwnerId owner,
        Guid credentialId,
        OwnerCredentialMethod method,
        OwnerCredentialLookup lookup,
        string? material,
        CancellationToken cancellationToken)
    {
        var storedOwnerId = RequireOwner(owner);
        var storedCredentialId = RequireCredential(credentialId);
        var storedMethod = RequireMethod(method);
        var storedLookup = RequireLookup(lookup);
        var storedMaterial = RequireMaterialAgreesWithMethod(method, material);
        var statedLookup = method.LookupMovesWithTheMaterial ? null : storedLookup;
        var changedAt = timeProvider.GetUtcNow();

        try
        {
            var written = await dbContext.OwnerCredentials
                .Where(credential => credential.Id == storedCredentialId
                    && credential.OwnerId == storedOwnerId
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
            return OwnerCredentialWriteOutcome.LookupTaken;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The instant the material changed at is deliberately left where it is, which is the whole difference between this
    /// and <see cref="ReplaceMaterialAsync" />. The verified record is named in the predicate as well as the credential,
    /// so a rotation that committed while this request was deriving leaves nothing here to write over: the update
    /// matches no row, answers <see cref="OwnerCredentialWriteOutcome.UnknownCredential" />, and the caller drops the
    /// rehash rather than putting the superseded material back.
    /// </remarks>
    public async Task<OwnerCredentialWriteOutcome> RewriteMaterialAsync(
        MailOwnerId owner,
        Guid credentialId,
        string verifiedMaterial,
        string material,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verifiedMaterial);
        ArgumentNullException.ThrowIfNull(material);

        var storedOwnerId = RequireOwner(owner);
        var storedCredentialId = RequireCredential(credentialId);

        var written = await dbContext.OwnerCredentials
            .Where(credential => credential.Id == storedCredentialId
                && credential.OwnerId == storedOwnerId
                && credential.Material == verifiedMaterial)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.Material, material)
                    .SetProperty(credential => credential.Version, credential => credential.Version + 1),
                cancellationToken);

        return OutcomeOf(written);
    }

    /// <inheritdoc />
    public async Task<OwnerCredentialWriteOutcome> SetEnabledAsync(
        MailOwnerId owner,
        Guid credentialId,
        bool enabled,
        CancellationToken cancellationToken)
    {
        var storedOwnerId = RequireOwner(owner);
        var storedCredentialId = RequireCredential(credentialId);

        var written = await dbContext.OwnerCredentials
            .Where(credential => credential.Id == storedCredentialId && credential.OwnerId == storedOwnerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.Enabled, enabled)
                    .SetProperty(credential => credential.Version, credential => credential.Version + 1),
                cancellationToken);

        return OutcomeOf(written);
    }

    /// <inheritdoc />
    public async Task<OwnerCredentialWriteOutcome> DeleteAsync(
        MailOwnerId owner,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        var storedOwnerId = RequireOwner(owner);
        var storedCredentialId = RequireCredential(credentialId);

        var removed = await dbContext.OwnerCredentials
            .Where(credential => credential.Id == storedCredentialId && credential.OwnerId == storedOwnerId)
            .ExecuteDeleteAsync(cancellationToken);

        return OutcomeOf(removed);
    }

    /// <summary>Reads a statement's row count as what the act did.</summary>
    /// <remarks>
    /// One row is the act; none is an owner holding no credential under that identifier, which covers a mistyped
    /// identifier, one belonging to a different owner, one of another method, and one deleted between a listing and
    /// this call. More than one cannot happen, because every statement here is bounded by the primary key.
    /// </remarks>
    private static OwnerCredentialWriteOutcome OutcomeOf(int rowsWritten) =>
        rowsWritten == 1 ? OwnerCredentialWriteOutcome.Written : OwnerCredentialWriteOutcome.UnknownCredential;

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

    private static OwnerCredentialMethod MethodOf(string storedMethod) =>
        OwnerCredentialMethod.TryParse(storedMethod, out var method)
            ? method
            : throw new InvalidOperationException(
                $"The stored credential method '{storedMethod}' is not one this release publishes.");

    private static OwnerCredentialLookup LookupOf(string storedLookup) =>
        OwnerCredentialLookup.TryCreate(storedLookup, out var lookup)
            ? lookup
            : throw new InvalidOperationException("A stored credential lookup is not one this release can read.");

    /// <summary>Refuses material a method does not keep, and the absence of material a method requires.</summary>
    /// <remarks>
    /// The column is nullable because two of the four methods keep nothing in it, and this is what stops that being a
    /// column anything may leave empty: a password credential written without its record would authenticate nobody and
    /// look exactly like one that had been provisioned, and a key credential written with one would keep material the
    /// method promises never to store.
    /// </remarks>
    private static string? RequireMaterialAgreesWithMethod(OwnerCredentialMethod method, string? material) =>
        method.StoresMaterial == (material is not null)
            ? material
            : throw new ArgumentException(
                method.StoresMaterial
                    ? $"A '{method.Name}' credential is stored with the material it is judged against."
                    : $"A '{method.Name}' credential stores no material, and what it is resolved by is its lookup.",
                nameof(material));

    private static string RequireMethod(OwnerCredentialMethod method) => method.IsSpecified
        ? method.Name
        : throw new ArgumentException("A credential is presented by a named method.", nameof(method));

    private static string RequireLookup(OwnerCredentialLookup lookup) => lookup.IsSpecified
        ? lookup.Value
        : throw new ArgumentException("A credential is resolved by a stated lookup.", nameof(lookup));

    private static Guid RequireOwner(MailOwnerId owner) => owner.IsSpecified
        ? owner.Value
        : throw new ArgumentException("A credential belongs to a named owner.", nameof(owner));

    private static Guid RequireCredential(Guid credentialId) => credentialId != Guid.Empty
        ? credentialId
        : throw new ArgumentException("A credential is named by a generated identifier.", nameof(credentialId));
}
