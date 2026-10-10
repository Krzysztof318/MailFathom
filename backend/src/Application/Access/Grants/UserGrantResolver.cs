// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>Computes what a user holds, from the roles assigned to them directly and through their groups.</summary>
/// <remarks>
/// <para>
/// The one place a user's grant comes from. It is computed from the records rather than stored on the user, so a
/// change to any of them reaches the next request rather than the next sign-in; what this replica computed is kept in
/// <see cref="UserGrantCache" /> until an announced change or the convergence interval forgets it.
/// </para>
/// <para>
/// What a caller may do is this grant narrowed by whatever admitted it — the surface's half, the credential's list,
/// and a token's scopes — through <see cref="ScopedGrant.NarrowedTo" />. None of those is asked here, because each is
/// a fact about the request rather than about the user.
/// </para>
/// </remarks>
public sealed class UserGrantResolver
{
    private readonly IGrantStore store;
    private readonly UserGrantCache cache;

    /// <summary>Initializes the resolver over where grants are kept and what this replica remembers of them.</summary>
    /// <param name="store">Where roles, groups, and assignments are kept.</param>
    /// <param name="cache">What this replica already computed.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public UserGrantResolver(IGrantStore store, UserGrantCache cache)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(cache);

        this.store = store;
        this.cache = cache;
    }

    /// <summary>Computes what one user holds.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every permission the user holds, each with the scopes it is held at; empty for a user assigned nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    public async Task<ScopedGrant> ResolveAsync(UserId user, CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A grant is computed for a named user.", nameof(user));
        }

        if (this.cache.TryRecall(user, out var remembered))
        {
            return remembered;
        }

        var generation = this.cache.Generation;
        var grant = await this.store.ReadGrantOfAsync(user, cancellationToken);

        this.cache.Remember(user, grant, generation);

        return grant;
    }

    /// <summary>Reports whether what one user holds widens on upgrade, because a role they were given lists a pattern.</summary>
    /// <param name="user">The user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> when a role given to them, directly or through a group, lists a pattern.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody.</exception>
    /// <remarks>Read from the records each time rather than remembered: only a write bounded by it asks, and a write decides on what is stored now.</remarks>
    public Task<bool> WidensOnUpgradeAsync(UserId user, CancellationToken cancellationToken) => user.IsSpecified
        ? this.store.HoldsWideningRoleAsync(AssignmentPrincipal.User(user), cancellationToken)
        : throw new ArgumentException("A grant is computed for a named user.", nameof(user));
}
