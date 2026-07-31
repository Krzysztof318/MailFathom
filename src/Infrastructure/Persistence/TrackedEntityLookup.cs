// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Linq.Expressions;
using MailMcp.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Finds a single entity among both the session's pending inserts and the rows already in the database.</summary>
/// <remarks>
/// A LINQ query always reaches the database, and EF Core does not flush pending changes before running one, so an
/// entity added earlier in the same uncommitted session is invisible to a query. Only the change tracker can see it.
/// This helper keeps that two-pass lookup, and the single predicate that drives both passes, in one place.
/// <para>
/// Prefer <c>FindAsync</c> when the lookup is by primary key: it performs the same change-tracker-first resolution
/// natively. This helper exists for lookups by an alternate key, where no such framework shortcut applies.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal static class TrackedEntityLookup
{
    /// <summary>Gets the single entity matching <paramref name="match" />, preferring one pending in the session.</summary>
    /// <typeparam name="TEntity">The entity type to search.</typeparam>
    /// <param name="entities">The set whose change-tracker entries are searched first.</param>
    /// <param name="persistedQuery">
    /// The database query used when nothing is pending. Pass <paramref name="entities" /> itself, or a query with the
    /// navigations the caller needs, because the change-tracker pass cannot apply <c>Include</c>.
    /// </param>
    /// <param name="match">The predicate applied to both passes.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The matching entity, or <see langword="null" /> when neither pass finds one.</returns>
    /// <exception cref="InvalidOperationException">Thrown when either pass matches more than one entity.</exception>
    public static async Task<TEntity?> SinglePendingOrPersistedAsync<TEntity>(
        DbSet<TEntity> entities,
        IQueryable<TEntity> persistedQuery,
        Expression<Func<TEntity, bool>> match,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(persistedQuery);
        ArgumentNullException.ThrowIfNull(match);

        // AsQueryable lets the same expression drive the in-memory pass, so the predicate is never written twice.
        var pending = entities.Local.AsQueryable().SingleOrDefault(match);
        if (pending is not null)
        {
            return pending;
        }

        return await persistedQuery.SingleOrDefaultAsync(match, cancellationToken);
    }
}
