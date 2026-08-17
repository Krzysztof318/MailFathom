// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Commands;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what one invocation leaves behind in the local log, which is the only durable record of it.</summary>
/// <remarks>
/// <para>
/// The command holds no exporter and opens no span, so once the terminal's scrollback is gone this file is the whole
/// answer to what was run against a deployment and how it ended. What these assert is that the answer is there for
/// every way an invocation can end, and that nothing the operator typed is in it.
/// </para>
/// <para>
/// Driven through <see cref="CliRunner.RunAsync" /> against the real command tree rather than against the record
/// directly, because what is under test is the runner's decision about when to append and with what — a record built
/// by hand would assert the shape of a type nobody had asked to write anything.
/// </para>
/// </remarks>
public sealed class CliInvocationRecordingTests
{
    /// <summary>An invocation that succeeded is recorded under the command it named, with the code it reported.</summary>
    [Fact]
    public async Task RunAsync_AnInvocationThatSucceeded_RecordsTheCommandAndTheExitCode()
    {
        // Arrange
        var log = new RecordingCliInvocationLog();

        // Act
        var exitCode = await CliRunner.RunAsync(ContextFor(log), ["--version"], TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(CliRootCommand.CommandName, entry.Command);
        Assert.Equal(CliInvocationOutcome.Completed, entry.Outcome);
        Assert.Equal(CliExitCode.Success, entry.ExitCode);
        Assert.Null(entry.Failure);
    }

    /// <summary>An invocation the parser refused is recorded as a failure, under the command it got as far as.</summary>
    [Fact]
    public async Task RunAsync_AnInvocationTheParserRefused_RecordsItAsAFailure()
    {
        // Arrange
        var log = new RecordingCliInvocationLog();

        // Act
        var exitCode = await CliRunner.RunAsync(
            ContextFor(log),
            ["--no-such-option"],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(CliInvocationOutcome.Failed, entry.Outcome);
        Assert.Equal(exitCode, entry.ExitCode);
    }

    /// <summary>A subcommand is recorded by the path of names it was declared under, not by the one word at the end.</summary>
    [Fact]
    public async Task RunAsync_ASubcommand_RecordsThePathOfDeclaredNames()
    {
        // Arrange
        var log = new RecordingCliInvocationLog();

        // Act
        _ = await CliRunner.RunAsync(
            ContextFor(log),
            ["contact", "list", "--help"],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.Equal($"{CliRootCommand.CommandName} contact list", entry.Command);
    }

    /// <summary>A command that reached the deployment seam records the failure it printed, and names no command argument.</summary>
    /// <remarks>
    /// Driven through <c>status</c> against an empty store, which is the shortest route to the one path that both
    /// resolves a deployment and raises a <see cref="CliFailure" /> — a run that stops at the parser reaches neither,
    /// so it would satisfy an assertion about what they write without ever having written anything.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ACommandThatFailedAgainstADeployment_RecordsTheLineItPrinted()
    {
        // Arrange
        var console = new RecordingCliConsole();
        var log = new RecordingCliInvocationLog();
        const string Address = "https://mail.example.invalid:8443";

        // Act
        var exitCode = await CliRunner.RunAsync(
            ContextFor(log, console),
            ["status", "--endpoint", Address],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal($"{CliRootCommand.CommandName} status", entry.Command);
        Assert.Equal(Assert.Single(console.Errors), entry.Failure);
    }

    /// <summary>The command a record names is the declared one, whatever the operator typed after it.</summary>
    /// <remarks>
    /// The address is deliberately allowed to reach <see cref="CliInvocationEntry.Failure" /> and is asserted against
    /// the command alone for that reason — what this holds is that the field naming the operation is derived from the
    /// parser rather than from the argument list, which is where a credential would be.
    /// </remarks>
    [Fact]
    public async Task RunAsync_AnInvocationCarryingAnAddress_NamesTheCommandWithoutIt()
    {
        // Arrange
        var log = new RecordingCliInvocationLog();
        const string Address = "https://mail.example.invalid:8443";

        // Act
        _ = await CliRunner.RunAsync(
            ContextFor(log),
            ["status", "--endpoint", Address],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.DoesNotContain(Address, entry.Command, StringComparison.Ordinal);
    }

    /// <summary>A shell that turned the log off is obeyed by an invocation that ran through the whole runner.</summary>
    [Fact]
    public async Task RunAsync_AShellThatTurnedTheLogOff_AppendsNothing()
    {
        // Arrange
        var log = new RecordingCliInvocationLog();

        // Act
        _ = await CliRunner.RunAsync(
            ContextFor(log, variables: name => name == CliOptions.LogVariable ? CliOptions.LogOff : null),
            ["--version"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(log.Appended);
    }

    /// <summary>An invocation that asked for no record leaves none.</summary>
    [Fact]
    public async Task RunAsync_AnInvocationThatAskedForNoRecord_AppendsNothing()
    {
        // Arrange
        var log = new RecordingCliInvocationLog();

        // Act
        _ = await CliRunner.RunAsync(
            ContextFor(log),
            ["--version", "--no-log"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(log.Appended);
    }

    /// <summary>A log that refused the record is said once and changes nothing about what the command reported.</summary>
    /// <remarks>The case the whole seam exists for: a read-only home directory must not turn an invocation that did what it was asked into one that failed.</remarks>
    [Fact]
    public async Task RunAsync_ALogThatCouldNotBeWritten_ReportsItWithoutFailingTheCommand()
    {
        // Arrange
        var console = new RecordingCliConsole();
        var log = new RecordingCliInvocationLog { Accepts = false };

        // Act
        var exitCode = await CliRunner.RunAsync(
            ContextFor(log, console),
            ["--version"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(console.Errors, line => line.Contains(log.Location, StringComparison.Ordinal));
    }

    /// <summary>A context given no log writes nowhere, which is what every test that is not about the log relies on.</summary>
    [Fact]
    public async Task RunAsync_AContextWithNoLog_RunsTheCommandAnyway()
    {
        // Arrange
        var console = new RecordingCliConsole();

        // Act
        var exitCode = await CliRunner.RunAsync(
            ContextFor(log: null, console),
            ["--version"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(console.Errors);
    }

    /// <summary>How long the invocation took is measured against the clock it was given, not against the wall.</summary>
    [Fact]
    public async Task RunAsync_AnInvocationThatTookTime_RecordsWhatTheClockMeasured()
    {
        // Arrange
        var log = new RecordingCliInvocationLog();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));
        var context = ContextFor(log, new RecordingCliConsole(), clock);

        clock.Advance(TimeSpan.FromSeconds(3));

        // Act
        _ = await CliRunner.RunAsync(context, ["--version"], TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.Equal(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero), entry.At);
        Assert.Equal(3000, entry.DurationMilliseconds);
    }

    /// <summary>Builds a context whose environment is stated rather than inherited.</summary>
    /// <remarks>
    /// Every test here drives the whole runner, which consults the shell for <see cref="CliOptions.LogVariable" />.
    /// Reading the process's own environment would make each of these depend on whether whoever started the test run
    /// had turned the log off — which the documentation tells operators to do — so the reader is supplied and answers
    /// for nothing by default.
    /// </remarks>
    private static CliContext ContextFor(
        RecordingCliInvocationLog? log,
        RecordingCliConsole? console = null,
        TimeProvider? clock = null,
        Func<string, string?>? variables = null) => new(
        console ?? new RecordingCliConsole(),
        new CredentialStore("credentials.json", new TokenProtector("credentials.key")),
        static (_, _) => throw new InvalidOperationException("No command in this class opens a transport."),
        FakeMailboxRedirect.Silent(),
        static _ => false,
        clock ?? TimeProvider.System,
        log,
        variables ?? (static _ => null));
}
