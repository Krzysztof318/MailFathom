// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Jobs;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Jobs;

/// <summary>
/// Reads the two decisions as the text they are. Both are conditional updates whose condition is the safety property:
/// without it a retry resets a job under the worker holding it and a drop closes one that already succeeded, and
/// neither failure says anything at run time beyond a row count that still reads as one.
/// </summary>
/// <remarks>
/// The column names are asserted through <see cref="JobEntity" /> rather than as text, so renaming a property fails
/// this test at compile time instead of leaving a statement PostgreSQL refuses at run time.
/// </remarks>
public sealed class DeadLetteredJobDecisionStatementsTests
{
    private static readonly Guid JobIdentity = new("6b0e1a52-4c8d-4f31-9a77-1d2e3f4a5b60");
    private static readonly DateTimeOffset DecidedAt = new(2026, 8, 20, 9, 30, 0, TimeSpan.Zero);

    /// <summary>Only a dead-lettered job may be offered again, which is what keeps a claimed one with its holder.</summary>
    [Fact]
    public void ComposeRetry_ADecision_TakesOnlyADeadLetteredJob()
    {
        // Act
        var statement = DeadLetteredJobDecisionStatements.ComposeRetry(JobIdentity, DecidedAt);

        // Assert
        Assert.Contains($"""WHERE "{nameof(JobEntity.Id)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains($"""AND "{nameof(JobEntity.State)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains(nameof(JobState.DeadLettered), statement.GetArguments().OfType<string>());
    }

    /// <summary>
    /// The attempt count goes back to nothing and the available instant to now, so the next pass may claim the job
    /// rather than the backoff the failed attempt had written holding it back.
    /// </summary>
    [Fact]
    public void ComposeRetry_ADecision_ClearsTheAttemptsAndMakesTheJobAvailableAtOnce()
    {
        // Act
        var statement = DeadLetteredJobDecisionStatements.ComposeRetry(JobIdentity, DecidedAt);

        // Assert
        Assert.Contains($"""SET "{nameof(JobEntity.State)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains($"""    "{nameof(JobEntity.AvailableAt)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains($"""    "{nameof(JobEntity.AttemptCount)}" = 0""", statement.Format, StringComparison.Ordinal);
        Assert.Contains($"""    "{nameof(JobEntity.StateChangedAt)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains(nameof(JobState.Pending), statement.GetArguments().OfType<string>());
    }

    /// <summary>
    /// The turn comes forward with the available instant, because the one the row is carrying was decided against a
    /// queue that has since drained. Left where it was, a job dead-lettered last week would be claimed before every
    /// owner's due work the moment an operator offered it again — and a sitting spent returning a dozen of them would
    /// put the whole dozen there.
    /// </summary>
    [Fact]
    public void ComposeRetry_ADecision_BringsTheTurnForwardWithoutEverMovingItBack()
    {
        // Act
        var statement = DeadLetteredJobDecisionStatements.ComposeRetry(JobIdentity, DecidedAt);

        // Assert
        Assert.Contains(
            $"""    "{nameof(JobEntity.TurnAt)}" = GREATEST("{nameof(JobEntity.TurnAt)}", """,
            statement.Format,
            StringComparison.Ordinal);
    }

    /// <summary>The failure columns say why the job stopped, and a decision about that account must not erase it.</summary>
    [Fact]
    public void ComposeRetry_ADecision_LeavesTheFailureColumnsWhereTheyAre()
    {
        // Act
        var statement = DeadLetteredJobDecisionStatements.ComposeRetry(JobIdentity, DecidedAt);

        // Assert
        Assert.DoesNotContain(nameof(JobEntity.LastFailureReason), statement.Format, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(JobEntity.LastFailureClassification), statement.Format, StringComparison.Ordinal);
    }

    /// <summary>A drop is terminal, so it reaches a job nothing else has decided about since.</summary>
    [Fact]
    public void ComposeDrop_ADecision_TakesOnlyADeadLetteredJob()
    {
        // Act
        var statement = DeadLetteredJobDecisionStatements.ComposeDrop(JobIdentity, DecidedAt);

        // Assert
        Assert.Contains($"""WHERE "{nameof(JobEntity.Id)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains($"""AND "{nameof(JobEntity.State)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains(nameof(JobState.DeadLettered), statement.GetArguments().OfType<string>());
        Assert.Contains(nameof(JobState.Dropped), statement.GetArguments().OfType<string>());
    }

    /// <summary>A drop changes the state and when it changed, and nothing else about the job.</summary>
    [Fact]
    public void ComposeDrop_ADecision_WritesTheStateAndTheInstantAlone()
    {
        // Act
        var statement = DeadLetteredJobDecisionStatements.ComposeDrop(JobIdentity, DecidedAt);

        // Assert
        Assert.Contains($"""SET "{nameof(JobEntity.State)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.Contains($"""    "{nameof(JobEntity.StateChangedAt)}" =""", statement.Format, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(JobEntity.AttemptCount), statement.Format, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(JobEntity.AvailableAt), statement.Format, StringComparison.Ordinal);
    }

    /// <summary>Every value is a parameter, so nothing a caller composes reaches either statement as text.</summary>
    [Fact]
    public void Compose_EitherDecision_PassesEveryValueAsAParameter()
    {
        // Act
        var retry = DeadLetteredJobDecisionStatements.ComposeRetry(JobIdentity, DecidedAt);
        var drop = DeadLetteredJobDecisionStatements.ComposeDrop(JobIdentity, DecidedAt);

        // Assert
        Assert.DoesNotContain(JobIdentity.ToString(), retry.Format, StringComparison.Ordinal);
        Assert.DoesNotContain(JobIdentity.ToString(), drop.Format, StringComparison.Ordinal);
        Assert.Contains(retry.GetArguments(), argument => Equals(argument, JobIdentity));
        Assert.Contains(retry.GetArguments(), argument => Equals(argument, DecidedAt));
        Assert.Contains(drop.GetArguments(), argument => Equals(argument, JobIdentity));
        Assert.Contains(drop.GetArguments(), argument => Equals(argument, DecidedAt));
    }
}
