// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Writes the envelope of a user a deployment declares, and nothing inside their document.</summary>
/// <remarks>
/// <para>
/// Both statements are single and conditional, which is what makes them safe against the race they actually meet: two
/// replicas of one deployment start at the same moment and reconcile the same roster. An insert guarded by
/// <c>ON CONFLICT</c> — over every unique constraint the row has rather than over the key alone — leaves the loser
/// having written nothing rather than raising, and an update that names the label it is replacing writes no row when
/// the label is already the one declared.
/// </para>
/// <para>
/// The document is provisioned as the empty object and is never written here. A user read from configuration is
/// served from their declaration, so filling the column would stop the file reaching them — which is an administrative
/// write rather than something a start does on their behalf.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedMailUserProvisioning(MailFathomDbContext dbContext, TimeProvider timeProvider)
    : IMailUserProvisioning
{
    /// <summary>The document a user's row is provisioned with, which is the empty record rather than their declaration.</summary>
    private const string EmptyDocument = "{}";

    /// <inheritdoc />
    public async Task<bool> ProvisionAsync(MailUserId user, string displayName, CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var provisionedAt = timeProvider.GetUtcNow();

        // The conflict clause names no target, so it covers the label's unique index as well as the primary key. Two
        // replicas of a deployment holding no user at all each mint an identifier and provision it under the one
        // label such a deployment uses, and a clause guarding the key alone would leave the loser raising the server's
        // own unique-violation sentence out of its start. The read below is what turns the silence into an answer.
        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO settings_accounts ("Id", "DisplayName", "Document", "Version", "CreatedAt", "UpdatedAt", "DocumentWrittenAtRuntime")
             VALUES ({userId}, {displayName}, CAST({EmptyDocument} AS jsonb), 1, {provisionedAt}, {provisionedAt}, FALSE)
             ON CONFLICT DO NOTHING
             """,
            cancellationToken);

        return await dbContext.UserAccounts
            .AsNoTracking()
            .AnyAsync(record => record.Id == userId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RelabelAsync(MailUserId user, string displayName, CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        // Guarded by the label's own index rather than by a read before it, for the reason the insert above is guarded
        // by ON CONFLICT: a roster read a moment earlier says nothing about the row another writer is committing, and
        // the alternative to a conditional statement is the server's unique-violation sentence reaching an operator.
        // The version is deliberately left where it is — it guards the document a writer composes over, and the label
        // is not part of that document, so stepping it here would refuse a write that had read the record correctly.
        await RowsToRelabel(dbContext.UserAccounts, userId, displayName)
            .ExecuteUpdateAsync(
                record => record.SetProperty(user => user.DisplayName, displayName),
                cancellationToken);

        // What the row carries afterwards rather than whether this statement was the one that wrote it, so a caller
        // relabelling to the label the row already had is told it holds it rather than that somebody else does.
        return await dbContext.UserAccounts
            .AsNoTracking()
            .AnyAsync(record => record.Id == userId && record.DisplayName == displayName, cancellationToken);
    }

    /// <summary>Composes the rows a relabel writes to: this user's, and only while no other user carries the label.</summary>
    /// <param name="records">The user records to select from.</param>
    /// <param name="userId">The user being relabelled.</param>
    /// <param name="displayName">The label they would carry.</param>
    /// <returns>The row to write, or nothing where the label is already theirs or is somebody else's.</returns>
    /// <remarks>
    /// Composed apart from the statement so that what it translates to is assertable without a server: the guard is a
    /// correlated existence check, and a predicate the provider could not translate would first be met as an exception
    /// out of a deployment's own rename.
    /// </remarks>
    internal static IQueryable<UserAccountEntity> RowsToRelabel(
        IQueryable<UserAccountEntity> records,
        Guid userId,
        string displayName) =>
        records.Where(record => record.Id == userId
            && record.DisplayName != displayName
            && !records.Any(held => held.Id != userId && held.DisplayName == displayName));

    private static Guid RequireNamed(MailUserId user) =>
        user.IsSpecified
            ? user.Value
            : throw new ArgumentException("A user record is provisioned for a named user.", nameof(user));
}
