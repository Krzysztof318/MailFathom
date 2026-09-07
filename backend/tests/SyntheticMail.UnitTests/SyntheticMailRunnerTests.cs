// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Corpus;
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
            Context(console, transport, path => SendingAccountFile.Read(path, UnconfiguredUserSecrets.Store())),
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

    [Fact]
    public async Task RunAsync_AnExportWhoseGenerationFails_LeavesNoFileBehindForTheRetryToTripOver()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        using var written = new MemoryStream();
        var discarded = new List<string>();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(
                console,
                transport,
                mailbox: mailbox,
                aiContentSource: new ScriptedAiEmailContentSource(
                    new SyntheticMailFailure("The model's answer carried no attachment.")),
                createCorpus: _ => new UnclosedStream(written),
                discardCorpus: discarded.Add),
            [
                "user@example.test",
                "--conversation",
                "--sensitive-percentage",
                "0",
                "--count",
                "6",
                "--ai",
                "--export",
                "corpus.zip",
            ],
            TestContext.Current.CancellationToken);

        // Assert
        // The file is reserved before generation so a typo cannot cost a paid batch, and that reservation must not
        // outlive the run: the identical command retried would otherwise be refused for a corpus that never existed.
        Assert.Equal(SyntheticMailExitCode.Failure, exitCode);
        Assert.Equal(["corpus.zip"], discarded);
    }

    [Fact]
    public async Task RunAsync_AnExportToAPathThatCannotBeWritten_IsRefusedBeforeAnythingIsGenerated()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        var source = new ScriptedAiEmailContentSource(new AiEmailContent("Subject", "Body", "<p>Body</p>"));
        var discarded = new List<string>();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(
                console,
                transport,
                mailbox: mailbox,
                aiContentSource: source,
                createCorpus: path => throw new SyntheticMailFailure($"'{path}' already exists."),
                discardCorpus: discarded.Add),
            [
                "user@example.test",
                "--conversation",
                "--sensitive-percentage",
                "0",
                "--count",
                "6",
                "--ai",
                "--export",
                "corpus.zip",
            ],
            TestContext.Current.CancellationToken);

        // Assert
        // The file is reserved on the same terms as the accounts are read: a path already taken, misspelled, or not
        // writable would otherwise be found after a provider has been paid to write the whole batch.
        Assert.Equal(SyntheticMailExitCode.Failure, exitCode);
        Assert.Empty(source.Requests);
        Assert.Contains("already exists", string.Join("\n", console.Diagnostics), StringComparison.Ordinal);

        // And nothing is discarded, because the file that refused the reservation is somebody else's corpus rather
        // than this run's: deleting it is the loss the refusal exists to prevent.
        Assert.Empty(discarded);
    }

    [Fact]
    public async Task RunAsync_AnExport_WritesTheCorpusAndOpensNothing()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        using var written = new MemoryStream();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, mailbox: mailbox, createCorpus: _ => new UnclosedStream(written)),
            [
                "user@example.test",
                "--conversation",
                "--sensitive-percentage",
                "0",
                "--count",
                "6",
                "--seed",
                "42",
                "--export",
                "corpus.zip",
            ],
            TestContext.Current.CancellationToken);

        // Assert
        // Generating is what costs money, so an export pays for the corpus once and reaches no mail server at all:
        // where it is delivered is the replay's decision and belongs to no run that generated anything.
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);
        Assert.Equal(0, transport.Opened);
        Assert.Equal(0, mailbox.Opened);

        using var source = new MemoryStream(written.ToArray(), writable: false);
        var corpus = CorpusArchive.Read(source);

        Assert.Equal(6, corpus.Exchanges.Sum(exchange => exchange.Count));
        Assert.Contains("--seed 42", corpus.Invocation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_AReplayOfACorpusItDidNotGenerate_DeliversBothHalvesAndThreadsFromWhatTheMailboxAssigned()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        await using var transport = new RecordingSyntheticMailTransport();
        await using var mailbox = new RecordingWatchedMailbox();
        using var corpus = TwoTurnCorpus();

        // Act
        var exitCode = await SyntheticMailRunner.RunAsync(
            Context(console, transport, mailbox: mailbox, openCorpus: _ => new UnclosedStream(corpus)),
            ["replay", "corpus.zip", "developer@example.com", "--interval", "0", "--delivery-timeout", "1"],
            TestContext.Current.CancellationToken);

        // Assert
        // Nothing about a replay depends on this repository's generator, and the identifiers it threads from are the
        // mailbox's rather than the corpus's — which is the whole reason an exchange is delivered a turn at a time.
        Assert.Equal(SyntheticMailExitCode.Success, exitCode);

        var submitted = Assert.Single(transport.Submissions);
        var appended = Assert.Single(mailbox.Appended);

        Assert.Equal("one@invented.test", submitted.MessageId);
        Assert.Equal(["ada@invented.test"], submitted.From);
        Assert.Equal(["developer@example.com"], submitted.To);
        Assert.Equal("one@invented.test", submitted.Marker);
        Assert.Equal(["developer@example.com"], appended.From);
        Assert.Equal(["ada@invented.test"], appended.To);
        Assert.Equal(RecordingWatchedMailbox.AssignedPrefix + "one@invented.test", appended.InReplyTo);
        Assert.Equal([RecordingWatchedMailbox.AssignedPrefix + "one@invented.test"], appended.References);
    }

    [Fact]
    public async Task RunAsync_AReplaySaysWhatItIsDeliveringAndWhatProducedIt()
    {
        // Arrange
        var console = new RecordingSyntheticMailConsole();
        using var corpus = TwoTurnCorpus();

        // Act
        await SyntheticMailRunner.RunAsync(
            Context(console, openCorpus: _ => new UnclosedStream(corpus)),
            ["replay", "corpus.zip", "developer@example.com", "--interval", "0", "--delivery-timeout", "1"],
            TestContext.Current.CancellationToken);

        // Assert
        // A run filling a mailbox says where its mail came from, so nobody has to unpack the archive to find out.
        Assert.Contains(
            console.Diagnostics,
            line => line.Contains("Replaying 2 messages in 1 exchanges", StringComparison.Ordinal));
        Assert.Contains(console.Diagnostics, line => line == "The corpus was generated with: written by hand");
    }

    [Fact]
    public async Task RunAsync_TwoReplaysOfOneCorpus_FillTwoFreshMailboxesIdentically()
    {
        // Arrange
        string[] invocation = ["replay", "corpus.zip", "developer@example.com", "--interval", "0", "--delivery-timeout", "1"];
        await using var first = new RecordingSyntheticMailTransport();
        await using var second = new RecordingSyntheticMailTransport();
        using var corpus = TwoTurnCorpus();

        // Act
        await SyntheticMailRunner.RunAsync(
            Context(new RecordingSyntheticMailConsole(), first, openCorpus: _ => new UnclosedStream(corpus)),
            invocation,
            TestContext.Current.CancellationToken);
        await SyntheticMailRunner.RunAsync(
            Context(new RecordingSyntheticMailConsole(), second, openCorpus: _ => new UnclosedStream(corpus)),
            invocation,
            TestContext.Current.CancellationToken);

        // Assert
        // A replay generates nothing, so two runs of one corpus into two fresh mailboxes leave the same mail in both —
        // which is what makes a difference between two runs of a pipeline a difference in the code. The headers are
        // compared as text because a snapshot's address lists are compared by reference otherwise.
        Assert.Equal(
            first.Submissions.Select(Describe),
            second.Submissions.Select(Describe));
    }

    /// <summary>Reduces one submission to what two runs of a corpus have to agree on.</summary>
    private static string Describe(SubmittedMessage message) => string.Join(
        " | ",
        message.MessageId,
        message.Subject,
        string.Join(",", message.From),
        string.Join(",", message.To),
        message.InReplyTo ?? "-",
        string.Join(",", message.References),
        message.Marker ?? "-");

    /// <summary>Builds a two-turn exchange nothing in this repository generated.</summary>
    private static MemoryStream TwoTurnCorpus() => HandWrittenCorpus.Build(
        """{ "invocation": "written by hand", "exchanges": [["a.eml", "b.eml"]] }""",
        ("a.eml", HandWrittenCorpus.Message("one@invented.test", "Ferry timetable", "ada@invented.test", "developer@example.com")),
        ("b.eml", HandWrittenCorpus.Message("two@invented.test", "Re: Ferry timetable", "developer@example.com", "ada@invented.test", "one@invented.test")));

    private static SyntheticMailContext Context(
        RecordingSyntheticMailConsole console,
        ISyntheticMailTransport? transport = null,
        Func<string, SendingAccount>? readAccount = null,
        string? aiConfigurationPath = null,
        IAiEmailContentSource? aiContentSource = null,
        Func<string, WatchedMailboxAccount>? readWatchedMailbox = null,
        IWatchedMailbox? mailbox = null,
        Func<string, Stream>? createCorpus = null,
        Action<string>? discardCorpus = null,
        Func<string, Stream>? openCorpus = null)
    {
        // A path named by a test is read by the real reader, which is what makes the missing-file message the one a
        // developer actually gets; a run that names none is handed a configuration that is never used, because a
        // test that reaches for content supplies the source rather than the file it would come from.
        Func<string, AiProviderConfiguration> readAiProvider = aiConfigurationPath is not null
            ? path => SyntheticAiProviderFile.Read(path, UnconfiguredUserSecrets.Store())
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
            createCorpus ?? (_ => throw new SyntheticMailFailure("the test has not configured a corpus to write")),
            discardCorpus ?? (_ => { }),
            openCorpus ?? (_ => throw new SyntheticMailFailure("the test has not configured a corpus to read")),
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
