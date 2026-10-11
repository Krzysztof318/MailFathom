// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Policies;

/// <summary>Reads and commits the settings policy one scope holds.</summary>
/// <remarks>
/// <para>
/// A key lookup in both directions: one call is one scope's row, so no shape here reads two organizations' policies
/// or writes the deployment's beside an organization's. It holds nothing about what a policy may state — whether a
/// path names a property, whether a value binds, and whether a property's class admits the statement are decided
/// before a candidate reaches here. What this decides is what only the database can: whether the organization
/// exists, and whether the policy being replaced is still the one the caller read.
/// </para>
/// <para>
/// A scope that stores no row and a scope whose row states nothing are one answer on the way out, because they are
/// one fact about what the scope governs; the version tells them apart for the writer that has to.
/// </para>
/// </remarks>
public interface ISettingsPolicyStore
{
    /// <summary>Reads the policy one scope holds.</summary>
    /// <param name="organizationId">The organization, or <see langword="null" /> for the deployment.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The policy, stating nothing at <see cref="SettingsPolicyDocument.UnwrittenVersion" /> where the scope stores none; or <see langword="null" /> when the deployment holds no such organization.</returns>
    /// <exception cref="SettingsPolicyUnreadableException">Thrown when the row holds a document past <see cref="SettingsPolicyDocument.MaximumOctets" />, which is refused rather than read.</exception>
    Task<SettingsPolicyDocument?> ReadAsync(Guid? organizationId, CancellationToken cancellationToken);

    /// <summary>Replaces the policy one scope holds, if it still stands at the expected version.</summary>
    /// <param name="organizationId">The organization, or <see langword="null" /> for the deployment.</param>
    /// <param name="json">The candidate policy, as the JSON object the row will hold.</param>
    /// <param name="expectedVersion">The version the candidate was composed over, which is <see cref="SettingsPolicyDocument.UnwrittenVersion" /> for a scope that stored none.</param>
    /// <param name="maximumUserRecordsJudged">
    /// The most user records the write may judge, or <see langword="null" /> when the candidate changes no default
    /// and no forced value a user's record is served on, in which case none is read.
    /// </param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>What the attempt did, carrying the version a commit produced.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json" /> is <see langword="null" />, empty, white space, not a JSON object, or past <see cref="SettingsPolicyDocument.MaximumOctets" /> as the database would store it.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedVersion" /> is negative, or <paramref name="maximumUserRecordsJudged" /> is not positive.</exception>
    /// <exception cref="SettingsPolicyUnreadableException">Thrown when a policy the write has to read is past <see cref="SettingsPolicyDocument.MaximumOctets" />.</exception>
    /// <remarks>
    /// <para>
    /// A policy somebody else moved and an organization removed underneath the write are one answer, for the reason
    /// a user's record gives the same one: the statement distinguishes neither and a caller settles both by reading
    /// again, which reports the version now in force or that there is no organization to write for. The attempt
    /// that commits second is the one refused, whichever replica each reached.
    /// </para>
    /// <para>
    /// Where the candidate changes what a user's record is served on, the write judges every user record the policy
    /// governs through <see cref="IUserRecordPolicyJudge" /> before it commits — every user of the organization, or
    /// every user of the deployment for the deployment's own policy. A record the candidate would leave unservable
    /// refuses the whole write, and a governed set past <paramref name="maximumUserRecordsJudged" /> refuses it
    /// before any record is read. What passes commits together with the endpoint switches of every user whose
    /// effective access it moved, so a session or a token stops or resumes on its holder's next request on every
    /// replica, including one that has not adopted the policy. No record's version moves.
    /// </para>
    /// <para>
    /// The whole of it runs under the lock that excludes every write of a governed record, so a record written while
    /// the policy is being judged is either judged with it or judged against it afterwards, never missed by both.
    /// </para>
    /// </remarks>
    Task<SettingsPolicyCommit> CommitAsync(
        Guid? organizationId,
        string json,
        long expectedVersion,
        int? maximumUserRecordsJudged,
        CancellationToken cancellationToken);
}
