// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Jobs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Jobs;

/// <summary>
/// The claim is the mechanism rather than a query, and every part of it fails silently when it is lost: without the
/// locking clause two workers run one job, without the type filter a replica takes work it cannot run, without the
/// bound one claim drains the queue, and without the terminal-state predicate the partial index stops applying. None of
/// that needs a database to establish, so it is established here.
/// </summary>
public sealed class JobClaimStatementTests
{
    private static readonly DateTimeOffset ClaimedAt = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private static JobClaimRequest Request => JobClaimRequest.Create(
        [JobType.ClassifyEmailSpam],
        3,
        TimeSpan.FromMinutes(2),
        JobLeaseOwner.Create("attempt-a"));

    [Fact]
    public void Compose_AClaim_TakesTheRowsUnderSkipLockedSoConcurrentWorkersDoNotWaitOnEachOther()
    {
        // Act
        var statement = JobClaimStatement.Compose(Request, ClaimedAt);

        // Assert
        Assert.Contains("FOR UPDATE SKIP LOCKED", statement.Format, StringComparison.Ordinal);
    }

    /// <summary>
    /// The limit counts the rows that survived locking, which is only true while the locking clause follows it. Written
    /// the other way round, a batch of one against a row another worker holds would come back empty instead of taking
    /// the next free row.
    /// </summary>
    [Fact]
    public void Compose_AClaim_PlacesTheLockingClauseAfterTheBound()
    {
        // Act
        var statement = JobClaimStatement.Compose(Request, ClaimedAt);

        // Assert
        Assert.True(
            statement.Format.IndexOf("LIMIT", StringComparison.Ordinal)
                < statement.Format.IndexOf("FOR UPDATE SKIP LOCKED", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two predicates make a job due, and the second is the crash recovery: a claimed job whose lease ran out is taken
    /// again without anything being told its holder is gone.
    /// </summary>
    [Fact]
    public void Compose_AClaim_TreatsAnExpiredLeaseAsDueBesideAPendingJob()
    {
        // Act
        var statement = JobClaimStatement.Compose(Request, ClaimedAt);

        // Assert
        Assert.Contains("candidate.\"AvailableAt\" <= ", statement.Format, StringComparison.Ordinal);
        Assert.Contains("candidate.\"LeaseExpiresAt\" <= ", statement.Format, StringComparison.Ordinal);
    }

    /// <summary>
    /// The terminal state is excluded explicitly even though the two due predicates already imply it, so PostgreSQL can
    /// prove the partial claim index applies rather than having to derive that through a disjunction.
    /// </summary>
    [Fact]
    public void Compose_AClaim_ExcludesTheTerminalStateInItsOwnPredicate()
    {
        // Act
        var statement = JobClaimStatement.Compose(Request, ClaimedAt);

        // Assert
        Assert.Contains("candidate.\"State\" <> ", statement.Format, StringComparison.Ordinal);
        Assert.Contains(nameof(JobState.Succeeded), statement.GetArguments());
    }

    /// <summary>The claim counts the attempt, because a process that dies mid-execution never reaches a line that would.</summary>
    [Fact]
    public void Compose_AClaim_CountsTheAttemptAndStampsTheLease()
    {
        // Act
        var statement = JobClaimStatement.Compose(Request, ClaimedAt);

        // Assert
        Assert.Contains("\"AttemptCount\" = job.\"AttemptCount\" + 1", statement.Format, StringComparison.Ordinal);
        Assert.Contains("RETURNING job.\"Id\" AS \"Value\"", statement.Format, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every value is a parameter, so nothing a caller composes — a lease owner above all — reaches the statement as
    /// text.
    /// </summary>
    [Fact]
    public void Compose_AClaim_PassesEveryValueAsAParameter()
    {
        // Arrange
        var request = Request;

        // Act
        var statement = JobClaimStatement.Compose(request, ClaimedAt);

        // Assert
        var arguments = statement.GetArguments();
        Assert.DoesNotContain("attempt-a", statement.Format, StringComparison.Ordinal);
        Assert.DoesNotContain("classify-email-spam", statement.Format, StringComparison.Ordinal);
        Assert.Contains(arguments, argument => Equals(argument, request.Owner.Value));
        Assert.Contains(arguments, argument => Equals(argument, request.BatchSize));
        Assert.Contains(arguments, argument => Equals(argument, ClaimedAt + request.LeaseDuration));
        Assert.Contains(
            arguments,
            argument => argument is string[] handledTypes && handledTypes is ["classify-email-spam"]);
    }

    /// <summary>
    /// The statement is data-modifying with a common table expression, which PostgreSQL allows only at the top level of
    /// a query. EF Core wraps raw SQL in a subquery as soon as anything composes over it, so this asserts the one thing
    /// the claim depends on and no signature states: enumerated directly, the statement reaches the database verbatim.
    /// </summary>
    [Fact]
    public void Compose_EnumeratedWithoutComposition_ReachesTheDatabaseUnwrapped()
    {
        // Arrange
        var options = MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null);
        using var context = new MailFathomDbContext(options, PostgresTextSearchConfiguration.Default);

        // Act
        var sql = context.Database.SqlQuery<Guid>(JobClaimStatement.Compose(Request, ClaimedAt)).ToQueryString();

        // Assert
        Assert.Contains("WITH due AS", sql, StringComparison.Ordinal);
        Assert.EndsWith("RETURNING job.\"Id\" AS \"Value\"", sql.TrimEnd(), StringComparison.Ordinal);
    }
}
