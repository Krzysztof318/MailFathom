// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Commands;
using MailFathom.Cli.Credentials;
using MailFathom.Cli.Diagnostics;
using MailFathom.TestSupport;
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
public sealed class CliInvocationRecordingTests : IDisposable
{
    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-cli-recording-tests-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }

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
        Assert.Contains("--no-such-option", entry.Failure ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>An invocation that succeeded records no failure, which is what makes the field mean something.</summary>
    /// <remarks>The control for the assertion above: reading the parser's errors unconditionally would put text on every record, and a refused invocation would look no different from one that worked.</remarks>
    [Fact]
    public async Task RunAsync_AnInvocationThatSucceeded_RecordsNoFailure()
    {
        // Arrange
        var log = new RecordingCliInvocationLog();

        // Act
        _ = await CliRunner.RunAsync(ContextFor(log), ["--version"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(log.Appended).Failure);
    }

    /// <summary>A command that refused by printing rather than raising still records what it printed.</summary>
    /// <remarks>
    /// A dozen commands treat a refusal as an ordinary outcome — a contact the book does not hold, a job no longer
    /// dead-lettered, a confirmation declined — so they write one sentence and return a failing code without raising.
    /// Nothing about that reaches the runner on its own, and those invocations were being recorded as failures with
    /// nothing said about them.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ACommandThatRefusedWithoutRaising_RecordsTheSentenceItPrinted()
    {
        // Arrange
        using var deployment = FakeContactDeployment.Holding();

        var log = new RecordingCliInvocationLog();

        // Act
        var exitCode = await CliRunner.RunAsync(
            this.ContextReaching(deployment, log),
            ["contact", "show", "--address", "nobody@example.test"],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(CliInvocationOutcome.Failed, entry.Outcome);
        Assert.NotNull(entry.Failure);
        Assert.DoesNotContain("nobody@example.test", entry.Failure, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What a command reported on its way to doing what it was asked is not a failure on the record of it.</summary>
    /// <remarks>
    /// The control for the test above: recording the last line written to standard error whatever the invocation
    /// returned would put a failure on every record of a command that reports its progress there, and the sign-in is
    /// the one that reports the most.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ACommandThatReportedOnStandardErrorAndSucceeded_RecordsNoFailure()
    {
        // Arrange
        using var deployment = FakeAdminEndpoint.Accepting("workstation");

        var console = new RecordingCliConsole { SecretToSupply = "not-a-real-key" };
        var log = new RecordingCliInvocationLog();

        // Act
        var exitCode = await CliRunner.RunAsync(
            this.ContextReaching(deployment, log, console),
            ["login", "--endpoint", "https://mail.example.test:8443"],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Null(entry.Failure);
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

    /// <summary>A command that reached a deployment names it, which is the field the whole seam through the access layer exists for.</summary>
    /// <remarks>
    /// Against a stored profile and a deployment that answers, because that is the only arrangement in which
    /// <c>DeploymentAccess.ReachAsync</c> gets past resolving the profile. A test that stops at the failure — which the
    /// two above deliberately do — never reaches the line that writes this field, so removing it would break every
    /// successful invocation's record with nothing to say so.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ACommandThatReachedADeployment_RecordsTheProfileItActedThrough()
    {
        // Arrange
        using var deployment = FakeAdminEndpoint.Accepting("workstation");

        var log = new RecordingCliInvocationLog();

        // Act
        var exitCode = await CliRunner.RunAsync(
            this.ContextReaching(deployment, log),
            ["status"],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal("production", entry.Deployment);
    }

    /// <summary>A sign-in names the profile it established, which no other command's path would have recorded for it.</summary>
    /// <remarks>
    /// <c>login</c> is the one command that reaches a deployment without going through the access seam the test above
    /// covers — it establishes a profile rather than resolving one — so the field it fills is filled by a line of its
    /// own. Removing that line would leave the command that gives every deployment its name as the only one whose own
    /// record does not carry one, and the test above would stay green.
    /// </remarks>
    [Fact]
    public async Task RunAsync_ASignIn_RecordsTheProfileItEstablished()
    {
        // Arrange
        using var deployment = FakeAdminEndpoint.Accepting("workstation");

        var console = new RecordingCliConsole { SecretToSupply = "not-a-real-key" };
        var log = new RecordingCliInvocationLog();

        // Act
        var exitCode = await CliRunner.RunAsync(
            this.ContextReaching(deployment, log, console),
            ["login", "--endpoint", "https://mail.example.test:8443"],
            TestContext.Current.CancellationToken);

        // Assert
        var entry = Assert.Single(log.Appended);

        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal("mail.example.test", entry.Deployment);
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

    /// <summary>Builds a context with a stored profile and a deployment that answers, which is what a command has to reach one.</summary>
    /// <remarks>
    /// The profile is saved up front because <c>DeploymentAccess.ReachAsync</c> resolves one before it opens anything,
    /// so a command driven against a bare store stops before the deployment is involved at all. <c>login</c> is given
    /// the same arrangement and simply establishes a second profile over it.
    /// </remarks>
    private CliContext ContextReaching(
        FakeHttpMessageHandler deployment,
        RecordingCliInvocationLog log,
        RecordingCliConsole? console = null)
    {
        var store = new CredentialStore(
            Path.Combine(this.storeDirectory, "credentials.json"),
            new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

        store.Save("production", new Uri("https://mail.example.test:8443"), "not-a-real-key", "workstation");

        return new CliContext(
            console ?? new RecordingCliConsole(),
            store,
            (endpoint, trust) => FakeDeploymentTransport.Over(deployment, endpoint, trust),
            FakeMailboxRedirect.Silent(),
            static _ => false,
            Stopped(),
            log,
            static _ => null);
    }

    /// <summary>A clock that does not move, which is what a test asserting anything but a duration wants.</summary>
    /// <remarks>
    /// Every test here would otherwise take the wall clock, so a record's timestamp and duration would be whatever the
    /// machine was doing while it ran. Nothing asserts either of those today, which makes the absence of a flake
    /// incidental rather than intended — and the policy is about the dependency rather than about the flake.
    /// </remarks>
    private static FakeTimeProvider Stopped() => new(new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero));

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
        clock ?? Stopped(),
        log,
        variables ?? (static _ => null));
}
