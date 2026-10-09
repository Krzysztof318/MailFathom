// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Grants;

/// <summary>The grants this replica computed, kept until something they were computed from may have changed.</summary>
/// <remarks>
/// <para>
/// One replica's memory and nothing more. It is a bound on how often a grant is read rather than a guarantee about
/// what a grant is, so it is forgotten whole on every announced change and on every interval of the convergence worker,
/// and a replica that forgot early only reads PostgreSQL once more.
/// </para>
/// <para>
/// Forgetting is a generation rather than only a clear, because a read can be in flight across it: a grant read before
/// a change committed and remembered after the cache was cleared would otherwise outlive the change it predates. A
/// grant is remembered under the generation that was current when its read began and recalled only while that
/// generation still is.
/// </para>
/// </remarks>
public sealed class UserGrantCache
{
    private readonly ConcurrentDictionary<UserId, RememberedGrant> remembered = new();
    private long generation;

    /// <summary>Gets the generation a read beginning now remembers its grant under.</summary>
    public long Generation => Volatile.Read(ref this.generation);

    /// <summary>Recalls the grant this replica computed for one user, where nothing has been forgotten since.</summary>
    /// <param name="user">The user.</param>
    /// <param name="grant">The grant, where one is remembered.</param>
    /// <returns><see langword="true" /> when a grant from the current generation is remembered.</returns>
    public bool TryRecall(UserId user, [NotNullWhen(true)] out ScopedGrant? grant)
    {
        if (this.remembered.TryGetValue(user, out var held) && held.Generation == this.Generation)
        {
            grant = held.Grant;

            return true;
        }

        grant = null;

        return false;
    }

    /// <summary>Remembers the grant one read computed.</summary>
    /// <param name="user">The user.</param>
    /// <param name="grant">What the read found.</param>
    /// <param name="readInGeneration">The <see cref="Generation" /> taken before the read began.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="grant" /> is <see langword="null" />.</exception>
    public void Remember(UserId user, ScopedGrant grant, long readInGeneration)
    {
        ArgumentNullException.ThrowIfNull(grant);

        this.remembered[user] = new RememberedGrant(grant, readInGeneration);
    }

    /// <summary>Forgets every grant, including one whose read is still in flight.</summary>
    public void Forget()
    {
        Interlocked.Increment(ref this.generation);
        this.remembered.Clear();
    }

    private sealed record RememberedGrant(ScopedGrant Grant, long Generation);
}
