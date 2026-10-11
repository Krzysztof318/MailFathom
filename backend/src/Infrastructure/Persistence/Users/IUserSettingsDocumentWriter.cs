// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Policies;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Commits one user's record over the version it was authored against.</summary>
/// <remarks>
/// The writing half of <see cref="IUserSettingsDocumentReader" />, and like it a key lookup rather than a query: one
/// write is one user's row, so there is no shape here that touches two people's records. It holds nothing about what
/// a record may contain — whether the document binds, whether an account collides with another of the same user's,
/// and whether a value is secret material are decisions its caller takes. What this decides is what only the database
/// can: whether the record it is replacing is still the record the caller read, and which settings policies govern
/// the record at the moment it is replaced.
/// </remarks>
public interface IUserSettingsDocumentWriter
{
    /// <summary>Replaces one user's record, if it still stands at the expected version and the policies in force admit what the caller composes under them.</summary>
    /// <param name="user">The user whose record is written.</param>
    /// <param name="expectedVersion">The version the change was composed over.</param>
    /// <param name="compose">
    /// Composes the record to commit under the policies that govern the user, or answers <see langword="null" /> to
    /// commit nothing. It is called at most once, inside the write's transaction, and performs no I/O of its own.
    /// </param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>The version the commit produced, or <see langword="null" /> when the deployment holds no such user, their record had already moved past <paramref name="expectedVersion" />, or <paramref name="compose" /> answered nothing.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="user" /> names nobody, or when the composed record is empty, white space, not a JSON object, or past <see cref="UserSettingsDocument.MaximumOctets" /> as the database would store it.</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="compose" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedVersion" /> is negative.</exception>
    /// <exception cref="UserSettingsUnwritableException">Thrown when the write did not commit. Every failure but two leaves the user's record exactly as it was; the exceptions are a command timeout and a connection lost while the commit was in flight, because it had been sent by then, so whether it applied is settled by reading the version now in force rather than assumed here.</exception>
    /// <remarks>
    /// <para>
    /// A user the deployment does not hold and a record somebody else moved are one answer, because the statement
    /// distinguishes neither and a caller acts on both the same way: it re-reads the user's record, which either
    /// reports the version now in force or reports that there is nobody to write for.
    /// </para>
    /// <para>
    /// The policies are handed to the caller rather than read by it beforehand, because what the record is judged by
    /// and what its row is served on have to be the policies in force when the write commits: a replica that has not
    /// yet adopted a policy change would otherwise judge and project a record against a policy that no longer holds.
    /// They are read inside the write's transaction, under the lock a policy write excludes record writes with, so no
    /// policy moves between that read and the commit.
    /// </para>
    /// <para>
    /// The switches the candidate carries are the ones the record is served on once resolved through those policies,
    /// which the same statement writes onto the user's row — where a request reads them without reading any record.
    /// </para>
    /// </remarks>
    Task<long?> CommitAsync(
        UserId user,
        long expectedVersion,
        Func<GoverningSettingsPolicies, UserRecordCandidate?> compose,
        CancellationToken cancellationToken);
}
