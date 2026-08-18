// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Jobs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Jobs;

/// <summary>
/// Enqueuing is idempotent because the database refuses the second insert, so the conflict target and the standing down
/// are the guarantee rather than support for one. Both are readable without a database and neither is visible from the
/// port, so both are established here.
/// </summary>
public sealed class JobEnqueueStatementTests
{
    private static readonly DateTimeOffset EnqueuedAt = new(2026, 8, 12, 12, 0, 0, TimeSpan.Zero);

    private static JobEnqueueRequest Request => JobEnqueueRequest.Create(
        JobIdempotencyKey.Create("account-a/INBOX#1/12345/4711"),
        EmailOccurrenceJobPayload.For(EmailOccurrenceId.Create(
            MailAccountId.Create("account-a"),
            new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
            ImapUidValidity.Create(12345),
            ImapUid.Create(4711))),
        MailAccountId.Create("account-a"));

    /// <summary>
    /// The conflict target names the columns the unique index is built on. A target naming anything else would either
    /// fail to infer an index or deduplicate on the wrong identity, and both reach an operator as work done twice.
    /// </summary>
    [Fact]
    public void Compose_AnEnqueue_ConflictsOnTheTypeAndTheKeyTogether()
    {
        // Act
        var statement = JobEnqueueStatement.Compose(Guid.CreateVersion7(), Request, "{}", EnqueuedAt, enqueuedTrace: null);

        // Assert
        Assert.Contains(
            """ON CONFLICT ("JobType", "IdempotencyKey") DO NOTHING""",
            statement.Format,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A losing insert stands down rather than updating, so the existing job keeps its state, its attempts, and its
    /// lease. Updating instead would let a repeated enqueue disturb work already in flight.
    /// </summary>
    [Fact]
    public void Compose_AnEnqueue_LeavesAnExistingJobUntouched()
    {
        // Act
        var statement = JobEnqueueStatement.Compose(Guid.CreateVersion7(), Request, "{}", EnqueuedAt, enqueuedTrace: null);

        // Assert
        Assert.DoesNotContain("DO UPDATE", statement.Format, StringComparison.Ordinal);
        Assert.Contains("RETURNING \"Id\" AS \"Value\"", statement.Format, StringComparison.Ordinal);
    }

    /// <summary>A job with no available instant of its own is claimable as soon as it is written.</summary>
    [Fact]
    public void Compose_AnEnqueueThatNamesNoAvailableInstant_UsesTheInstantItIsWrittenAt()
    {
        // Act
        var statement = JobEnqueueStatement.Compose(Guid.CreateVersion7(), Request, "{}", EnqueuedAt, enqueuedTrace: null);

        // Assert
        Assert.Equal(3, statement.GetArguments().Count(argument => Equals(argument, EnqueuedAt)));
    }

    [Fact]
    public void Compose_AnEnqueueThatNamesAnAvailableInstant_KeepsItApartFromTheInstantItIsWrittenAt()
    {
        // Arrange
        var availableAt = EnqueuedAt.AddHours(1);
        var scheduled = JobEnqueueRequest.CreateAvailableAt(
            Request.Key,
            Request.Payload,
            Request.AccountId,
            availableAt);

        // Act
        var statement = JobEnqueueStatement.Compose(Guid.CreateVersion7(), scheduled, "{}", EnqueuedAt, enqueuedTrace: null);

        // Assert
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, availableAt));
        Assert.Equal(2, statement.GetArguments().Count(argument => Equals(argument, EnqueuedAt)));
    }

    /// <summary>Every value is a parameter, so nothing an enqueuer composes reaches the statement as text.</summary>
    [Fact]
    public void Compose_AnEnqueue_PassesEveryValueAsAParameter()
    {
        // Act
        var statement = JobEnqueueStatement.Compose(Guid.CreateVersion7(), Request, """{"accountId":"a"}""", EnqueuedAt, enqueuedTrace: null);

        // Assert
        Assert.DoesNotContain("account-a", statement.Format, StringComparison.Ordinal);
        Assert.DoesNotContain("classify-email-spam", statement.Format, StringComparison.Ordinal);
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, Request.Key.Value));
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, """{"accountId":"a"}"""));
    }

    /// <summary>
    /// The statement is an insert with a conflict clause, which EF Core cannot compose over: it wraps raw SQL in a
    /// subquery as soon as anything composes over it. Enumerated directly the statement reaches the database verbatim,
    /// which is what the enqueue depends on and what no signature states.
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
        var sql = context.Database
            .SqlQuery<Guid>(JobEnqueueStatement.Compose(Guid.CreateVersion7(), Request, "{}", EnqueuedAt, enqueuedTrace: null))
            .ToQueryString();

        // Assert
        Assert.Contains("INSERT INTO jobs", sql, StringComparison.Ordinal);
        Assert.EndsWith("RETURNING \"Id\" AS \"Value\"", sql.TrimEnd(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The trace is written by the insert as two parameters, so a job carries the work that enqueued it without the
    /// statement ever naming a value in its own text.
    /// </summary>
    [Fact]
    public void Compose_AnEnqueueInsideATrace_WritesBothPropagationValuesAsParameters()
    {
        // Arrange
        var enqueuedTrace = JobTraceContext.FromTraceParent(
            "00-4bf92f3577b34da6a3ce929d0e0e4736-1a2b3c4d5e6f7081-01",
            "vendor=state");

        // Act
        var statement = JobEnqueueStatement.Compose(
            Guid.CreateVersion7(),
            Request,
            "{}",
            EnqueuedAt,
            enqueuedTrace);

        // Assert
        Assert.Contains("\"EnqueuedTraceParent\", \"EnqueuedTraceState\"", statement.Format, StringComparison.Ordinal);
        Assert.Contains(
            statement.GetArguments(),
            argument => Equals(argument, "00-4bf92f3577b34da6a3ce929d0e0e4736-1a2b3c4d5e6f7081-01"));
        Assert.Contains(statement.GetArguments(), argument => Equals(argument, "vendor=state"));
    }

    /// <summary>An enqueue nothing was tracing writes the columns as absent, which is what an attempt reads as no link.</summary>
    [Fact]
    public void Compose_AnEnqueueOutsideAnyTrace_WritesTheColumnsAsAbsent()
    {
        // Arrange

        // Act
        var statement = JobEnqueueStatement.Compose(
            Guid.CreateVersion7(),
            Request,
            "{}",
            EnqueuedAt,
            enqueuedTrace: null);

        // Assert
        Assert.Equal(2, statement.GetArguments().Count(argument => argument is null));
    }
}
