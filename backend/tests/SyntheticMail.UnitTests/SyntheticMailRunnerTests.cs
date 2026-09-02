// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailFathom.SyntheticMail.Generation;
using MailFathom.SyntheticMail.Generation.AiContent;
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

    [Fact]
    public async Task RunAsync_AIContentWithoutAProviderFile_SaysWhatToWriteAndGeneratesNothing()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        var missing = Path.Combine(AppContext.BaseDirectory, $"nothing-writes-this-{Guid.NewGuid():N}.local.json");

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, aiConfigurationPath: missing),
            ["developer@example.com", "--ai", "--ai-config", missing, "--count", "6", "--seed", "42", "--until", "2026-08-08"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Failure, exitCode);
        Assert.Equal(0, transport.Opened);
        Assert.Empty(console.Output);
        Assert.Contains(console.Diagnostics, line => line.Contains(missing, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_ANAiDryRun_ListsTheCorpusWithItsLanguageAndTopicAndSubmitsNothing()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        var source = new ScriptedAiEmailContentSource(new AiEmailContent("Quarterly figures", "Hello,\n\nFigures attached.\n\nRegards\nAnna", "<html><body><p>Figures attached.</p></body></html>"));

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, aiContentSource: source),
            ["developer@example.com", "--ai", "--dry-run", "--count", "6", "--seed", "42", "--language", "pl", "--topic", "travel", "--until", "2026-08-08"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);
        Assert.Equal(6, console.Output.Count);
        Assert.Equal(0, transport.Opened);
        Assert.Equal(6, source.Requests.Count);
        Assert.All(source.Requests, request =>
        {
            Assert.Equal("pl", request.LanguageCode);
            Assert.Equal(SyntheticMailTopic.Travel, request.Topic);
        });
        Assert.All(console.Output, line =>
        {
            Assert.Contains("language=pl topic=travel", line, StringComparison.Ordinal);
            Assert.Contains("Quarterly figures", line, StringComparison.Ordinal);
        });
        Assert.Contains(console.Diagnostics, line => line.Contains("AI content in pl over travel", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_AProviderThatRefusesTheKey_FailsTheRunNamingTheMoveAndSendsNothing()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        var source = new ScriptedAiEmailContentSource(new SyntheticMailFailure("The endpoint refused the API key: check 'apiKey'."));

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, aiContentSource: source),
            ["developer@example.com", "--ai", "--count", "6", "--seed", "42", "--interval", "0", "--until", "2026-08-08"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Failure, exitCode);
        Assert.Equal(0, transport.Opened);
        Assert.Empty(transport.Submissions);
        Assert.Contains(console.Diagnostics, line => line.Contains("refused the API key", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_AIContentThatTheServerAccepts_DeliversEveryMessageAndReportsIt()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        var source = new ScriptedAiEmailContentSource(new AiEmailContent("Quarterly figures", "Hello,\n\nFigures attached.\n\nRegards\nAnna", "<html><body><p>Figures attached.</p></body></html>"));

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, aiContentSource: source),
            ["developer@example.com", "--ai", "--count", "4", "--seed", "1", "--interval", "0", "--until", "2026-08-08"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);
        Assert.Equal(1, transport.Opened);
        Assert.Equal(4, transport.Submissions.Count);
        Assert.True(transport.Disposed);
        Assert.Contains(console.Diagnostics, line => line == "Delivered 4 of 4 to developer@example.com.");
    }

    [Fact]
    public async Task RunAsync_AConversationDryRun_ListsEveryThreadAndItsTurnsAndConnectsToNothing()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, mailbox: mailbox),
            ["developer@example.com", "--conversation", "--dry-run", "--count", "12", "--seed", "42", "--until", "2026-08-08"],
            TestContext.Current.CancellationToken);

        // Assert
        // A dry run needs no credential for the mailbox either: its address is the recipient the invocation named,
        // which is all the generator needs to author half the turns.
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);
        Assert.Equal(12, console.Output.Count);
        Assert.Equal(0, transport.Opened);
        Assert.Equal(0, mailbox.Opened);
        Assert.All(console.Output, line => Assert.Contains("thread=", line, StringComparison.Ordinal));
        Assert.Contains(console.Output, line => line.Contains("side=Mailbox", StringComparison.Ordinal));
        Assert.Contains(console.Diagnostics, line => line.Contains("Delivered as exchanges with developer@example.com", StringComparison.Ordinal));
        Assert.Contains(console.Diagnostics, line => line.Contains("--conversation --delivery-timeout 120", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_AConversationTheServerAccepts_SubmitsOneHalfFilesTheOtherAndReportsThemAll()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, mailbox: mailbox),
            ["developer@example.com", "--conversation", "--count", "10", "--seed", "42", "--interval", "0", "--until", "2026-08-08"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);
        Assert.Equal(1, transport.Opened);
        Assert.Equal(1, mailbox.Opened);
        Assert.Equal(10, transport.Submissions.Count + mailbox.Appended.Count);
        Assert.NotEmpty(mailbox.Appended);
        Assert.True(mailbox.Disposed);
        Assert.Contains(console.Diagnostics, line => line == "Delivered 10 of 10 to developer@example.com.");
    }

    [Fact]
    public async Task RunAsync_AConversationInAiMode_ReachesTheSourceForEveryTurnAndDeliversWhatItAnswered()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var source = new ScriptedAiEmailContentSource(new AiEmailContent(
            "Quarterly figures",
            "Hello,\n\nFigures attached.\n\nRegards\nAnna",
            "<html><body><h1>Quarterly figures</h1><p>Figures attached.</p></body></html>"));

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, mailbox: mailbox, aiContentSource: source),
            ["developer@example.com", "--conversation", "--ai", "--count", "8", "--seed", "42", "--interval", "0", "--until", "2026-08-08"],
            TestContext.Current.CancellationToken);

        // Assert
        // The two modes compose rather than merely coexisting: the seed still decides the exchanges and the sides, and
        // every turn of every one of them is content the source answered — including the replies, which is what the
        // parent opening in each request after a thread's first is for.
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);
        Assert.Equal(8, source.Requests.Count);
        Assert.Equal(8, transport.Submissions.Count + mailbox.Appended.Count);
        Assert.NotEmpty(mailbox.Appended);
        Assert.Contains(source.Requests, request => request.ParentOpening is not null);
        Assert.All(transport.Submissions, submission =>
            Assert.Contains("Quarterly figures", submission.Subject, StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunAsync_AConversationToAMailboxTheFileDoesNotConfigure_IsRefusedNamingBothAddresses()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, readWatchedMailbox: _ => WatchedMailbox("somebody.else@example.com")),
            ["developer@example.com", "--conversation", "--count", "10", "--seed", "42", "--interval", "0"],
            TestContext.Current.CancellationToken);

        // Assert
        // An exchange delivers to a mailbox, reads it back, and files in it, so a run against two addresses would fill
        // one with half a thread and the other with replies to messages it never received.
        Assert.Equal(SyntheticMailExitCode.Failure, exitCode);
        Assert.Empty(transport.Submissions);
        Assert.Contains(console.Diagnostics, line =>
            line.Contains("developer@example.com", StringComparison.Ordinal)
            && line.Contains("somebody.else@example.com", StringComparison.Ordinal));
    }

    private static SyntheticMailContext Context(
        RecordingSyntheticMailConsole console,
        ISyntheticMailTransport? transport = null,
        Func<string, SendingAccount>? readAccount = null,
        string? aiConfigurationPath = null,
        IAiEmailContentSource? aiContentSource = null,
        Func<string, WatchedMailboxAccount>? readWatchedMailbox = null,
        IWatchedMailbox? mailbox = null)
    {
        // A path named by a test is read by the real reader, which is what makes the missing-file message the one a
        // developer actually gets; a run that names none is handed a configuration that is never used, because a
        // test that reaches for content supplies the source rather than the file it would come from.
        Func<string, AiProviderConfiguration> readAiProvider = aiConfigurationPath is not null
            ? SyntheticAiProviderFile.Read
            : _ => new AiProviderConfiguration("not-a-real-key", "gpt-test", null);

        Func<AiProviderConfiguration, IAiEmailContentSource> openAiContentSource = aiContentSource is { } source
            ? _ => source
            : _ => throw new SyntheticMailFailure("the test has not configured an AI content source");

        return new SyntheticMailContext(
            console,
            readAccount ?? (_ => Account()),
            readWatchedMailbox ?? (_ => WatchedMailbox()),
            readAiProvider,
            _ => transport ?? new RecordingSyntheticMailTransport(),
            _ => mailbox ?? new RecordingWatchedMailbox(),
            openAiContentSource,
            new FakeTimeProvider(Today));
    }

    private static SendingAccount Account() => new(
        "smtp.example.test",
        587,
        MailTransportSecurity.StartTls,
        new MailboxAddress("Throwaway", "throwaway@example.test"),
        "throwaway@example.test",
        "not-a-real-password",
        SyntheticAuthorIdentity.Fabricated);

    private static WatchedMailboxAccount WatchedMailbox(string address = "developer@example.com") => new(
        "imap.example.test",
        993,
        MailTransportSecurity.ImplicitTls,
        new MailboxAddress("Developer", address),
        address,
        "not-a-real-password",
        SentFolder: null);
}
