// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Keeps each owner's username-and-password credentials in PostgreSQL, one row per credential.</summary>
/// <remarks>
/// <para>
/// Every write here is a single statement executed against the database rather than a change-tracked edit followed by
/// <c>SaveChanges</c>, for the reason <see cref="Accounts.MailboxRefreshTokenStore" /> gives: these acts are reached on
/// paths that hold no persistence session, so saving the scoped context would commit whatever else that scope had
/// pending. It buys the property rotation needs as well — the replacement is one statement, so a request arriving while
/// it commits is judged against exactly one of the two records and the previous password stops working at that instant
/// rather than over a window.
/// </para>
/// <para>
/// Reads are projections rather than entity graphs, and the two projections are deliberately different shapes. The
/// administrative listing selects every column but the hash, so no answer composed from it can carry stored material
/// however it is later mapped; the resolution a request performs selects the hash and nothing an operator reads.
/// </para>
/// <para>
/// Every write names the owner beside the credential, which turns an identifier copied from another owner's listing
/// into <see cref="OwnerCredentialWriteOutcome.UnknownCredential" /> rather than into a rotation performed on somebody
/// else's credential.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedOwnerPasswordCredentials(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IOwnerPasswordCredentialStore
{
    /// <summary>How many credentials one owner may hold, which is the bound the listing reads under.</summary>
    /// <remarks>Named here as well as on the listing so the statement that enforces it and the query that assumes it cannot come to disagree.</remarks>
    private const int Ceiling = OwnerPasswordCredential.MaximumListedPerOwner;

    /// <inheritdoc />
    public async Task<ResolvedOwnerPasswordCredential?> FindByUsernameAsync(
        OwnerCredentialUsername username,
        CancellationToken cancellationToken)
    {
        if (!username.IsSpecified)
        {
            throw new ArgumentException("A credential is resolved by a named username.", nameof(username));
        }

        var canonicalUsername = username.Value;

        var stored = await dbContext.OwnerPasswordCredentials
            .AsNoTracking()
            .Where(credential => credential.Username == canonicalUsername)
            .Select(credential => new
            {
                credential.Id,
                credential.OwnerId,
                credential.Enabled,
                credential.PasswordHash,
            })
            .SingleOrDefaultAsync(cancellationToken);

        return stored is null
            ? null
            : new ResolvedOwnerPasswordCredential(
                stored.Id,
                MailOwnerId.Create(stored.OwnerId),
                stored.Enabled,
                stored.PasswordHash);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OwnerPasswordCredential>> ReadForOwnerAsync(
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var storedOwnerId = RequireOwner(owner);

        var stored = await dbContext.OwnerPasswordCredentials
            .AsNoTracking()
            .Where(credential => credential.OwnerId == storedOwnerId)
            .OrderBy(credential => credential.CreatedAt)
            .ThenBy(credential => credential.Id)
            .Take(OwnerPasswordCredential.MaximumListedPerOwner)
            .Select(credential => new
            {
                credential.Id,
                credential.Username,
                credential.Enabled,
                credential.Version,
                credential.CreatedAt,
                credential.PasswordChangedAt,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. stored.Select(credential => new OwnerPasswordCredential(
                credential.Id,
                owner,
                OwnerCredentialUsername.Create(credential.Username),
                credential.Enabled,
                credential.Version,
                credential.CreatedAt,
                credential.PasswordChangedAt)),
        ];
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The insert states the owner and the ceiling as subqueries and lets the unique index answer for the username, so
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
        OwnerCredentialUsername username,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);

        var storedOwnerId = RequireOwner(owner);
        var storedCredentialId = RequireCredential(credentialId);

        if (!username.IsSpecified)
        {
            throw new ArgumentException("A credential is provisioned under a named username.", nameof(username));
        }

        var canonicalUsername = username.Value;
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
             INSERT INTO owner_password_credentials
                 ("Id", "OwnerId", "Username", "PasswordHash", "Enabled", "Version", "CreatedAt", "PasswordChangedAt")
             SELECT {storedCredentialId}, {storedOwnerId}, {canonicalUsername}, {passwordHash}, TRUE, 1, {provisionedAt}, {provisionedAt}
             WHERE EXISTS (SELECT 1 FROM settings_accounts WHERE "Id" = {storedOwnerId})
               AND (SELECT COUNT(*) FROM owner_password_credentials WHERE "OwnerId" = {storedOwnerId}) < {Ceiling}
             ON CONFLICT ("Username") DO NOTHING
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

        return await dbContext.OwnerPasswordCredentials
            .AsNoTracking()
            .CountAsync(credential => credential.OwnerId == storedOwnerId, cancellationToken) >= Ceiling
            ? OwnerCredentialWriteOutcome.OwnerAtCredentialCeiling
            : OwnerCredentialWriteOutcome.UsernameTaken;
    }

    /// <inheritdoc />
    public async Task<OwnerCredentialWriteOutcome> ReplacePasswordAsync(
        MailOwnerId owner,
        Guid credentialId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);

        var storedOwnerId = RequireOwner(owner);
        var storedCredentialId = RequireCredential(credentialId);
        var changedAt = timeProvider.GetUtcNow();

        var written = await dbContext.OwnerPasswordCredentials
            .Where(credential => credential.Id == storedCredentialId && credential.OwnerId == storedOwnerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.PasswordHash, passwordHash)
                    .SetProperty(credential => credential.PasswordChangedAt, changedAt)
                    .SetProperty(credential => credential.Version, credential => credential.Version + 1),
                cancellationToken);

        return OutcomeOf(written);
    }

    /// <inheritdoc />
    /// <remarks>The instant the password changed at is deliberately left where it is, which is the whole difference between this and <see cref="ReplacePasswordAsync" />.</remarks>
    public async Task<OwnerCredentialWriteOutcome> RewritePasswordHashAsync(
        MailOwnerId owner,
        Guid credentialId,
        string passwordHash,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);

        var storedOwnerId = RequireOwner(owner);
        var storedCredentialId = RequireCredential(credentialId);

        var written = await dbContext.OwnerPasswordCredentials
            .Where(credential => credential.Id == storedCredentialId && credential.OwnerId == storedOwnerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(credential => credential.PasswordHash, passwordHash)
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

        var written = await dbContext.OwnerPasswordCredentials
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

        var removed = await dbContext.OwnerPasswordCredentials
            .Where(credential => credential.Id == storedCredentialId && credential.OwnerId == storedOwnerId)
            .ExecuteDeleteAsync(cancellationToken);

        return OutcomeOf(removed);
    }

    /// <summary>Reads a statement's row count as what the act did.</summary>
    /// <remarks>
    /// One row is the act; none is an owner holding no credential under that identifier, which covers a mistyped
    /// identifier, one belonging to a different owner, and one deleted between a listing and this call. More than one
    /// cannot happen, because every statement here is bounded by the primary key.
    /// </remarks>
    private static OwnerCredentialWriteOutcome OutcomeOf(int rowsWritten) =>
        rowsWritten == 1 ? OwnerCredentialWriteOutcome.Written : OwnerCredentialWriteOutcome.UnknownCredential;

    private static Guid RequireOwner(MailOwnerId owner) => owner.IsSpecified
        ? owner.Value
        : throw new ArgumentException("A credential belongs to a named owner.", nameof(owner));

    private static Guid RequireCredential(Guid credentialId) => credentialId != Guid.Empty
        ? credentialId
        : throw new ArgumentException("A credential is named by a generated identifier.", nameof(credentialId));
}
