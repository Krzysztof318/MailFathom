// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Commits one user's record over the version it was authored against.</summary>
/// <remarks>
/// <para>
/// The writing half of <see cref="IUserSettingsDocumentReader" />, and like it a key lookup rather than a query: one
/// write is one user's row, so there is no shape here that touches two people's records. It holds nothing about what
/// a record may contain — whether the document binds, whether an account collides with another of the same user's,
/// and whether a value is secret material are decisions taken before a candidate reaches here. What this decides is
/// the one thing only the database can decide: whether the record it is replacing is still the record the caller read.
/// </para>
/// <para>
/// A commit sets the runtime-written marker, and that is the contract rather than a side effect. What the marker
/// records is that the record is the user's own rather than the empty column a start provisioned, which is exactly
/// what a committed document makes true — so a marker left behind by a write would leave the next start reading that
/// user's mail accounts out of a configuration section they have stopped being supplied by. Whether the *first* such
/// write is one an operator meant is not this port's question: a user a configuration source still supplies is
/// refused before the candidate is composed, and that declaration is where their mailboxes are changed.
/// </para>
/// </remarks>
public interface IUserSettingsDocumentWriter
{
    /// <summary>Replaces one user's record, if it still stands at the expected version.</summary>
    /// <param name="user">The user whose record is written.</param>
    /// <param name="json">The candidate record, as the JSON object the row will hold.</param>
    /// <param name="expectedVersion">The version the candidate was composed over.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>The version the commit produced, or <see langword="null" /> when the deployment holds no such user or their record had already moved past <paramref name="expectedVersion" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, or when <paramref name="json" /> is <see langword="null" />, empty, white space, not a JSON object, or past <see cref="UserSettingsDocument.MaximumOctets" /> as the database would store it.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedVersion" /> is negative.</exception>
    /// <exception cref="UserSettingsUnwritableException">Thrown when the statement did not commit. Every failure but two leaves the user's record exactly as it was; the exceptions are a command timeout and a connection lost while the statement was in flight, because the statement had been sent by then, so whether it applied is settled by reading the version now in force rather than assumed here.</exception>
    /// <remarks>
    /// A user the deployment does not hold and a record somebody else moved are one answer, because the statement
    /// distinguishes neither and a caller acts on both the same way: it re-reads the user's record, which either
    /// reports the version now in force or reports that there is nobody to write for.
    /// </remarks>
    Task<long?> CommitAsync(
        MailUserId user,
        string json,
        long expectedVersion,
        CancellationToken cancellationToken);
}
