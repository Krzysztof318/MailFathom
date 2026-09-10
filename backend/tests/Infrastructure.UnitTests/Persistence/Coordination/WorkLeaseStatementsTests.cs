// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Infrastructure.Persistence.Coordination;
using MailFathom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Coordination;

/// <summary>
/// Each of the three statements is the mechanism rather than a query, and each fails silently when part of it is lost:
/// without the conflict clause a claim raises a duplicate key instead of taking an expired scope, without the expiry
/// comparison it takes a scope somebody is holding, and without either holder predicate a write from a hold that was
/// already reclaimed lands on the lease that replaced it. None of that needs a database to establish, so it is
/// established here.
/// </summary>
/// <remarks>
/// The column names are asserted through <see cref="WorkLeaseEntity" /> rather than as text, so renaming a property
/// fails this test at compile time instead of leaving a statement PostgreSQL refuses at run time. The scope and the
/// hold are asserted as arguments rather than as text, because both are parameters.
/// </remarks>
public sealed class WorkLeaseStatementsTests
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    private static readonly WorkScope Scope = WorkScope.Create("mail-account:personal");

    private static readonly WorkLeaseHolder Holder = WorkLeaseHolder.Create("hold-a");

    /// <summary>
    /// One statement covers both ways a scope can be free. Without the conflict clause the second replica to reach a
    /// scope would raise a duplicate key rather than being answered that the scope is held.
    /// </summary>
    [Fact]
    public void ComposeClaim_AClaim_ResolvesTheKeyConflictInTheSameStatementThatInserts()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeClaim(Scope, Holder, LeaseDuration);

        // Assert
        Assert.Contains(
            $"ON CONFLICT (\"{nameof(WorkLeaseEntity.Scope)}\") DO UPDATE",
            statement.Format,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The conflict path takes the scope only when the recorded lease has already run out, judged against the
    /// existing row rather than against the one being proposed. Losing that predicate would hand a held scope to a
    /// second replica.
    /// </summary>
    [Fact]
    public void ComposeClaim_AClaim_TakesTheScopeOnlyWhenTheRecordedLeaseHasRunOut()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeClaim(Scope, Holder, LeaseDuration);

        // Assert
        Assert.Contains(
            $"WHERE work_leases.\"{nameof(WorkLeaseEntity.ExpiresAt)}\" <= now()",
            statement.Format,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Every instant is PostgreSQL's, so the deployment holds one clock rather than one per replica: a replica running
    /// minutes fast would otherwise find a live lease expired and take a scope another replica is working under. The
    /// duration crosses as an interval, which is the only part of the arithmetic a caller supplies.
    /// </summary>
    [Fact]
    public void ComposeClaim_AClaim_StampsAndJudgesEveryInstantWithTheDatabasesOwnClock()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeClaim(Scope, Holder, LeaseDuration);

        // Assert
        Assert.Contains("now(), now() +", statement.Format, StringComparison.Ordinal);
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, LeaseDuration));
        Assert.All(statement.GetArguments(), argument => Assert.IsNotType<DateTimeOffset>(argument));
    }

    /// <summary>
    /// The expiry the caller acts on is read back out of the row rather than computed beside it, because a caller
    /// computing it from its own clock would be back to the drift the statement's <c>now()</c> removed.
    /// </summary>
    [Fact]
    public void ComposeClaim_AClaim_ReportsTheExpiryTheRowEndedUpWith()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeClaim(Scope, Holder, LeaseDuration);

        // Assert
        Assert.Contains(
            $"RETURNING work_leases.\"{nameof(WorkLeaseEntity.ExpiresAt)}\" AS \"Value\"",
            statement.Format,
            StringComparison.Ordinal);
    }

    /// <summary>Nothing about the scope or the hold is written into the statement text; both are parameters.</summary>
    [Fact]
    public void ComposeClaim_AClaim_PassesTheScopeAndTheHoldAsParameters()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeClaim(Scope, Holder, LeaseDuration);

        // Assert
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, Scope.Value));
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, Holder.Value));
        Assert.DoesNotContain(Scope.Value, statement.Format, StringComparison.Ordinal);
    }

    /// <summary>A renewal is conditional on the holder alone, so a hold nothing has taken renews rather than stopping.</summary>
    [Fact]
    public void ComposeRenewal_ARenewal_MovesTheExpiryOnlyWhileTheRowStillNamesTheHold()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeRenewal(Scope, Holder, LeaseDuration);

        // Assert
        Assert.Contains(
            $"WHERE \"{nameof(WorkLeaseEntity.Scope)}\" =",
            statement.Format,
            StringComparison.Ordinal);
        Assert.Contains(
            $"AND \"{nameof(WorkLeaseEntity.Holder)}\" =",
            statement.Format,
            StringComparison.Ordinal);
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, Holder.Value));
    }

    /// <summary>
    /// Adding the expiry to the renewal's predicate would abandon a scope nobody else had taken, so the statement
    /// deliberately does not carry one.
    /// </summary>
    [Fact]
    public void ComposeRenewal_ARenewal_DoesNotRefuseAHoldWhoseLeaseHasAlreadyRunOut()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeRenewal(Scope, Holder, LeaseDuration);

        // Assert
        Assert.DoesNotContain(
            $"\"{nameof(WorkLeaseEntity.ExpiresAt)}\" <=",
            statement.Format,
            StringComparison.Ordinal);
    }

    /// <summary>A renewal is measured from the database's clock and reports what the row ended up with, as a claim is.</summary>
    [Fact]
    public void ComposeRenewal_ARenewal_MeasuresFromTheDatabasesClockAndReportsTheStoredExpiry()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeRenewal(Scope, Holder, LeaseDuration);

        // Assert
        Assert.Contains("= now() +", statement.Format, StringComparison.Ordinal);
        Assert.Contains(
            $"RETURNING \"{nameof(WorkLeaseEntity.ExpiresAt)}\" AS \"Value\"",
            statement.Format,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A release removes the row, so the table stays the set of scopes something is holding and the next claim's
    /// insert takes the scope without waiting out an expiry.
    /// </summary>
    [Fact]
    public void ComposeRelease_ARelease_RemovesTheRowRatherThanClearingItsHolder()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeRelease(Scope, Holder);

        // Assert
        Assert.Contains("DELETE FROM work_leases", statement.Format, StringComparison.Ordinal);
    }

    /// <summary>
    /// The holder predicate is what makes a late release harmless: a hold whose lease was already taken over finds the
    /// row naming somebody else and removes nothing.
    /// </summary>
    [Fact]
    public void ComposeRelease_ARelease_FreesTheScopeOnlyWhileTheRowStillNamesTheHold()
    {
        // Act
        var statement = WorkLeaseStatements.ComposeRelease(Scope, Holder);

        // Assert
        Assert.Contains(
            $"AND \"{nameof(WorkLeaseEntity.Holder)}\" =",
            statement.Format,
            StringComparison.Ordinal);
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, Holder.Value));
    }
}
