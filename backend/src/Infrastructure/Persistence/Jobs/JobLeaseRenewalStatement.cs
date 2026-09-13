// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;

namespace MailFathom.Infrastructure.Persistence.Jobs;

/// <summary>Composes the statement that pushes a held job's lease further out.</summary>
/// <remarks>
/// <para>
/// The expiry is stamped from PostgreSQL's <c>now()</c> with the duration passed as an interval, and read back out of
/// the row rather than computed beside it. A claim on another replica judges that expiry against the same <c>now()</c>,
/// so an expiry stamped from this process's clock would be shortened or lengthened by the drift between the two.
/// </para>
/// <para>
/// The condition is the holder and the state rather than the expiry, for the reason
/// <see cref="JobStore.RenewLeaseAsync" /> states. It is a type of its own so the statement can be read and asserted
/// without a database.
/// </para>
/// </remarks>
internal static class JobLeaseRenewalStatement
{
    /// <summary>Composes the renewal of one job's lease.</summary>
    /// <param name="jobId">The job whose lease is renewed.</param>
    /// <param name="user">The attempt claiming to hold it.</param>
    /// <param name="leaseDuration">How much longer the job is held from the instant PostgreSQL renews it.</param>
    /// <returns>The statement, whose one row is the renewed expiry, and which returns none when the attempt no longer holds the job.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="user" /> is <see langword="null" />.</exception>
    internal static FormattableString Compose(JobId jobId, JobLeaseOwner user, TimeSpan leaseDuration)
    {
        ArgumentNullException.ThrowIfNull(user);

        var jobIdValue = jobId.Value;
        var userValue = user.Value;
        var claimed = nameof(JobState.Claimed);

        return $"""
                UPDATE jobs
                SET "LeaseExpiresAt" = now() + {leaseDuration}
                WHERE "Id" = {jobIdValue}
                  AND "State" = {claimed}
                  AND "LeaseOwner" = {userValue}
                RETURNING "LeaseExpiresAt" AS "Value"
                """;
    }
}
