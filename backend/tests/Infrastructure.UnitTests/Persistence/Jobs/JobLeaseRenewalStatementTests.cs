// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Jobs;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Jobs;

/// <summary>
/// A renewal is the half of the lease a long execution lives on, and both of its failures are silent: a holder predicate
/// lost lets a late attempt extend a lease somebody else took, and an expiry stamped from the process's clock lets a
/// replica running fast find a renewed lease already expired.
/// </summary>
public sealed class JobLeaseRenewalStatementTests
{
    private static readonly JobId Job = JobId.Create(new Guid("0b8f4f5e-6c1d-4a2b-9e3f-7a8b9c0d1e2f"));

    private static readonly JobLeaseOwner User = JobLeaseOwner.Create("attempt-a");

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The expiry is PostgreSQL's <c>now()</c> plus an interval and is read back from the row, so no instant this
    /// process read ever reaches the statement or the caller.
    /// </summary>
    [Fact]
    public void Compose_ARenewal_StampsTheExpiryByTheDatabasesClockAndReportsTheStoredValue()
    {
        // Act
        var statement = JobLeaseRenewalStatement.Compose(Job, User, LeaseDuration);

        // Assert
        Assert.Contains(
            $"SET \"{nameof(JobEntity.LeaseExpiresAt)}\" = now() +",
            statement.Format,
            StringComparison.Ordinal);
        Assert.Contains(
            $"RETURNING \"{nameof(JobEntity.LeaseExpiresAt)}\" AS \"Value\"",
            statement.Format,
            StringComparison.Ordinal);
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, LeaseDuration));
        Assert.All(statement.GetArguments(), argument => Assert.IsNotType<DateTimeOffset>(argument));
    }

    /// <summary>
    /// The condition is the holder and the claimed state, and not the expiry: an attempt nobody reclaimed goes on
    /// working, while one whose job another attempt now holds renews nothing.
    /// </summary>
    [Fact]
    public void Compose_ARenewal_MovesTheExpiryOnlyWhileTheRowStillNamesTheAttempt()
    {
        // Act
        var statement = JobLeaseRenewalStatement.Compose(Job, User, LeaseDuration);

        // Assert
        Assert.Contains($"AND \"{nameof(JobEntity.LeaseOwner)}\" =", statement.Format, StringComparison.Ordinal);
        Assert.Contains($"AND \"{nameof(JobEntity.State)}\" =", statement.Format, StringComparison.Ordinal);
        Assert.DoesNotContain($"\"{nameof(JobEntity.LeaseExpiresAt)}\" <=", statement.Format, StringComparison.Ordinal);
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, User.Value));
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, nameof(JobState.Claimed)));
        Assert.DoesNotContain(User.Value, statement.Format, StringComparison.Ordinal);
    }
}
