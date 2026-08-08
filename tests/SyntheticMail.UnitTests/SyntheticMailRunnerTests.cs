// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using MimeKit;

using Xunit;

namespace MailFathom.SyntheticMail.UnitTests;

/// <summary>One invocation, end to end, without a mail server and without the wall clock.</summary>
public sealed class SyntheticMailRunnerTests
{
    private static readonly DateTimeOffset Today = new(2026, 8, 8, 11, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_ADryRun_ListsTheCorpusAndOpensNothing()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport),
            ["developer@example.com", "--dry-run", "--count", "6", "--seed", "42", "--until", "2026-08-08"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);
        Assert.Equal(6, console.Output.Count);
        Assert.Equal(0, transport.Opened);
        Assert.Empty(transport.Submissions);
    }

    [Fact]
    public async Task RunAsync_TwoDryRunsOfOneSeed_ListTheSameCorpus()
    {
        // Arrange
        string[] invocation = ["developer@example.com", "--dry-run", "--count", "25", "--seed", "808", "--until", "2026-08-08"];
        var first = new RecordingSyntheticMailConsole();
        var second = new RecordingSyntheticMailConsole();

        // Act
        await SyntheticMailRunner.RunAsync(Context(first), invocation, TestContext.Current.CancellationToken);
        await SyntheticMailRunner.RunAsync(Context(second), invocation, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Output, second.Output);
    }

    [Fact]
    public async Task RunAsync_TwoDryRunsOfDifferentSeeds_ListDifferentCorpora()
    {
        // Arrange
        var first = new RecordingSyntheticMailConsole();
        var second = new RecordingSyntheticMailConsole();

        // Act
        await SyntheticMailRunner.RunAsync(
            Context(first),
            ["developer@example.com", "--dry-run", "--count", "25", "--seed", "808"],
            TestContext.Current.CancellationToken);
        await SyntheticMailRunner.RunAsync(
            Context(second),
            ["developer@example.com", "--dry-run", "--count", "25", "--seed", "809"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(first.Output, second.Output);
    }

    [Fact]
    public async Task RunAsync_NoSeed_ReportsTheOneItChoseAndAnInvocationThatRepeatsTheBatch()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();

        // Act
        await SyntheticMailRunner.RunAsync(
            Context(console),
            ["developer@example.com", "--dry-run", "--count", "3"],
            TestContext.Current.CancellationToken);

        // Assert
        var repeat = Assert.Single(console.Diagnostics, line => line.StartsWith("Repeat this batch with:", StringComparison.Ordinal));

        Assert.Contains("--seed ", repeat, StringComparison.Ordinal);
        Assert.Contains("--until 2026-08-08", repeat, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ABatchTheServerAccepts_DeliversEveryMessageAndReportsIt()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport),
            ["developer@example.com", "--count", "4", "--seed", "1", "--interval", "0"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);
        Assert.Equal(1, transport.Opened);
        Assert.Equal(4, transport.Submissions.Count);
        Assert.True(transport.Disposed);
        Assert.Contains(
            console.Diagnostics,
            line => line == "Delivered 4 of 4 to developer@example.com.");
    }

    [Fact]
    public async Task RunAsync_AMessageTheServerRefuses_ReportsItAndFails()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        var submitted = 0;
        await using var transport = new RecordingSyntheticMailTransport(
            _ => ++submitted % 3 == 0 ? "550 no such mailbox" : null);

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport),
            ["developer@example.com", "--count", "9", "--seed", "42", "--interval", "0"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Failure, exitCode);
        Assert.Contains(console.Diagnostics, line => line.Contains("550 no such mailbox", StringComparison.Ordinal));
        Assert.Contains(console.Diagnostics, line => line.StartsWith("Delivered ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_NoCredentialFile_SaysWhatToWriteAndDeliversNothing()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        var missing = Path.Combine(AppContext.BaseDirectory, $"nothing-writes-this-{Guid.NewGuid():N}.local.json");

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, SendingAccountFile.Read),
            ["developer@example.com", "--config", missing],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Failure, exitCode);
        Assert.Equal(0, transport.Opened);
        Assert.Contains(console.Diagnostics, line => line.Contains(missing, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_AnArgumentOutsideItsBounds_SaysSoAndDeliversNothing()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport),
            ["developer@example.com", "--count", "999999"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Failure, exitCode);
        Assert.Equal(0, transport.Opened);
        Assert.Contains(console.Diagnostics, line => line.Contains("--count", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ANullArgument_IsRefused()
    {
        // Arrange, Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => SyntheticMailRunner.RunAsync(
            Context(new RecordingSyntheticMailConsole()),
            null!,
            TestContext.Current.CancellationToken));
    }

    private static SyntheticMailContext Context(
        RecordingSyntheticMailConsole console,
        ISyntheticMailTransport? transport = null,
        Func<string, SendingAccount>? readAccount = null) => new(
            console,
            readAccount ?? (_ => Account()),
            _ => transport ?? new RecordingSyntheticMailTransport(),
            new FakeTimeProvider(Today));

    private static SendingAccount Account() => new(
        "smtp.example.test",
        587,
        SmtpTransportSecurity.StartTls,
        new MailboxAddress("Throwaway", "throwaway@example.test"),
        "throwaway@example.test",
        "not-a-real-password",
        SyntheticAuthorIdentity.Fabricated);
}
