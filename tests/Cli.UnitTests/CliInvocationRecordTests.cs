// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the record itself decides, apart from when the runner asks it to.</summary>
public sealed class CliInvocationRecordTests
{
    /// <summary>A failure longer than the ceiling is cut to it, so one record stays one line of the log.</summary>
    /// <remarks>
    /// The messages this command raises are sentences, so nothing reaches the ceiling today — which is exactly why it
    /// needs a test. A message that grew, or a bound that stopped being applied, would otherwise be found as a log file
    /// somebody could no longer read a line at a time.
    /// </remarks>
    [Fact]
    public void Ended_AFailureLongerThanTheCeiling_RecordsExactlyTheCeiling()
    {
        // Arrange
        var record = new CliInvocationRecord(new FakeTimeProvider());
        var failure = new string('x', CliInvocationRecord.MaximumFailureLength + 100);

        // Act
        var entry = record.Ended("mfctl status", CliExitCode.Failure, failure);

        // Assert
        Assert.Equal(CliInvocationRecord.MaximumFailureLength, entry.Failure?.Length);
    }

    /// <summary>A failure the ceiling does not reach is recorded whole, which is what makes the bound a bound.</summary>
    [Fact]
    public void Ended_AFailureShorterThanTheCeiling_RecordsAllOfIt()
    {
        // Arrange
        var record = new CliInvocationRecord(new FakeTimeProvider());
        const string Failure = "The deployment answered 404 rather than a contact.";

        // Act
        var entry = record.Ended("mfctl contact delete", CliExitCode.Failure, Failure);

        // Assert
        Assert.Equal(Failure, entry.Failure);
    }

    /// <summary>A failure whose ceiling falls between the halves of one character is cut before it, not through it.</summary>
    /// <remarks>
    /// The consequence is worse than a mangled message: an unpaired surrogate is not valid UTF-16, the JSON writer
    /// refuses one, and the append runs in the runner's <c>finally</c> — so the exception would replace whatever the
    /// invocation was already reporting, which is the one thing keeping a record must never do.
    /// </remarks>
    [Fact]
    public void Ended_AFailureWhoseCeilingFallsInsideACharacter_CutsBeforeIt()
    {
        // Arrange
        var record = new CliInvocationRecord(new FakeTimeProvider());
        var failure = new string('x', CliInvocationRecord.MaximumFailureLength - 1) + "\U0001F600 and more";

        // Act
        var entry = record.Ended("mfctl status", CliExitCode.Failure, failure);

        // Assert
        Assert.Equal(CliInvocationRecord.MaximumFailureLength - 1, entry.Failure?.Length);
        Assert.DoesNotContain(entry.Failure!, char.IsSurrogate);
    }

    /// <summary>What the command raised is recorded as its type and as nothing else about it.</summary>
    [Fact]
    public void Faulted_AnInvocationThatRaised_RecordsTheTypeAndNoExitCode()
    {
        // Arrange
        var record = new CliInvocationRecord(new FakeTimeProvider());

        // Act
        var entry = record.Faulted("mfctl status", new InvalidOperationException("A message written for a developer."));

        // Assert
        Assert.Equal(CliInvocationOutcome.Faulted, entry.Outcome);
        Assert.Equal(typeof(InvalidOperationException).FullName, entry.Fault);
        Assert.Null(entry.ExitCode);
        Assert.Null(entry.Failure);
    }

    /// <summary>The message of what was raised is left out, because a defect's message quotes what it was working on.</summary>
    [Fact]
    public void Faulted_AnInvocationWhoseFailureQuotedItsWork_RecordsNoneOfThatText()
    {
        // Arrange
        var record = new CliInvocationRecord(new FakeTimeProvider());
        const string Quoted = "subject: Q3 numbers, from: someone@example.invalid";

        // Act
        var entry = record.Faulted("mfctl status", new InvalidOperationException(Quoted));

        // Assert
        Assert.DoesNotContain(Quoted, entry.Fault ?? string.Empty, StringComparison.Ordinal);
        Assert.Null(entry.Failure);
    }

    /// <summary>An invocation the operator stopped is told apart from one that crashed.</summary>
    [Fact]
    public void Cancelled_AnInvocationTheOperatorStopped_IsNotRecordedAsAFault()
    {
        // Arrange
        var record = new CliInvocationRecord(new FakeTimeProvider());

        // Act
        var entry = record.Cancelled("mfctl status");

        // Assert
        Assert.Equal(CliInvocationOutcome.Cancelled, entry.Outcome);
        Assert.Null(entry.Fault);
        Assert.Null(entry.ExitCode);
    }

    /// <summary>The deployment a command settled on is carried onto whatever ends the invocation.</summary>
    [Fact]
    public void ReachedDeployment_AnInvocationThatFaultedAfterwards_StillNamesTheDeployment()
    {
        // Arrange
        var record = new CliInvocationRecord(new FakeTimeProvider());

        // Act
        record.ReachedDeployment("production");

        // Assert
        Assert.Equal("production", record.Faulted("mfctl status", new InvalidOperationException()).Deployment);
    }
}
