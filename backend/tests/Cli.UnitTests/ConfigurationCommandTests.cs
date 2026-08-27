// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Administration.Configuration;
using MailFathom.Cli.Commands.Configuration;
using MailFathom.Cli.Transport;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the configuration commands send, what they refuse on their own, and what they print.</summary>
/// <remarks>
/// <para>
/// Three things are asserted throughout. What a command sends, because every change is composed over the version the
/// reading before it carried, and one composed over a version fetched separately is the lost update the deployment's
/// version guard exists to refuse. What a command refuses without reaching a deployment, because an adoption is the
/// one act in MailFathom that moves a decision out of a file and into a database and must never happen unasked. And
/// what reaches which stream, because a scripted run reads an exit code and a captured error rather than a screen.
/// </para>
/// <para>
/// The editing session is covered from every side it can end on: abandoned by emptying the buffer, saved unchanged,
/// saved over a version somebody else moved past, and never opened at all because no editor is named. What the write
/// itself decides is the deployment's and is covered against it; nothing here re-asserts it.
/// </para>
/// </remarks>
public sealed class ConfigurationCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;
    private const string SnippetsPerEmail = "MailboxSearch:SnippetsPerEmail";

    /// <summary>A document as the deployment answers with one, with the marker where a secret reference stands.</summary>
    /// <remarks>Named once so the buffer a test opens and the document the fake answered with are visibly the same string, which is the whole of what the editing session is asserted to do with it.</remarks>
    private const string RedactedChatDocument = """{ "Chat": { "ApiKey": { "SecretReference": "(redacted)" } } }""";

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// The two codes this client acts on are the deployment's own, written here as literals because the wire carries
    /// a number rather than the type. A literal that stopped naming the same refusal would leave the command silently
    /// treating a superseded write as an ordinary one, so the agreement is asserted rather than assumed.
    /// </summary>
    [Fact]
    public void Codes_TheRefusalsTheCommandsActOn_AreTheOnesTheDeploymentPublishes()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal(ConfigurationWriteAnswer.VersionSuperseded, MailFathomErrorCode.ConfigurationVersionSuperseded.Value);
        Assert.Equal(ConfigurationWriteAnswer.WriteShadowed, MailFathomErrorCode.ConfigurationWriteShadowed.Value);
    }

    /// <summary>A setting is reported with the layer that decided it, because that is where an operator changes it.</summary>
    [Fact]
    public async Task Get_ASettingAFileSupplies_PrintsTheValueAndTheFileThatDecidedIt()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            reading: FakeConfigurationDeployment.Reading(
                version: 1,
                (SnippetsPerEmail, "3", "file", "10-deployment.json", false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "get", SnippetsPerEmail, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("10-deployment.json", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains(SnippetsPerEmail, StringComparison.Ordinal));

        // The prefix reaches the deployment rather than being applied to a whole reading here. A command that dropped
        // it would ask for every setting the deployment composed and be answered with all of them or with the
        // too-broad refusal, and every assertion above would still hold.
        Assert.Equal(
            $"?prefix={Uri.EscapeDataString(SnippetsPerEmail)}",
            deployment.LastReadingQuery());
    }

    /// <summary>
    /// A path is a prefix of the settings beneath it, so a reading that matched a section reports nothing about the
    /// path itself. Handing back the first entry the prefix matched would answer a question nobody asked — and saying
    /// no source supplies it would be false in the one case the command can already see is false, since the reading it
    /// discarded is the proof. What the operator typed was a section, so it names how many settings sit beneath it and
    /// the command that reads them.
    /// </summary>
    [Fact]
    public async Task Get_APathTheReadingCoversOnlyAsASection_PointsAtTheCommandThatReadsASection()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            reading: FakeConfigurationDeployment.Reading(
                version: 1,
                (SnippetsPerEmail, "3", "file", "10-deployment.json", false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "get", "MailboxSearch", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("1 setting sits beneath it", StringComparison.Ordinal)
                && line.Contains("mfctl config show MailboxSearch", StringComparison.Ordinal));
    }

    /// <summary>
    /// A deployment answering more than this command buffers is sorted apart from one that could not be reached. The
    /// two send an operator to different places, and this one is reachable from a deployment behaving exactly as its
    /// own contract allows — a persisted document up to a megabyte, or a reading of every setting it composed — so
    /// reporting it as an unreachable host would send them after the address, the port, and the firewall for a
    /// deployment that answered correctly.
    /// </summary>
    [Fact]
    public async Task Get_ADeploymentAnsweringPastWhatTheCommandBuffers_SaysSoRatherThanThatItCouldNotBeReached()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            reading: FakeConfigurationDeployment.Reading(
                version: 1,
                (SnippetsPerEmail, new string('9', DeploymentTransport.ResponseSizeLimitInBytes + 1), "file", null, false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "get", SnippetsPerEmail, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("answered with more than", StringComparison.Ordinal)
                && line.Contains("KiB this command reads", StringComparison.Ordinal)
                && line.Contains("mfctl config show <prefix>", StringComparison.Ordinal));
        Assert.DoesNotContain(
            this.harness.Console.Errors,
            line => line.Contains("could not be reached", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same answer over a write says only that the answer was not read, because an adoption commits before it
    /// answers: the version has moved, so telling the operator nothing was read — or sending them to narrow a reading
    /// — would invite them to run a committed adoption a second time.
    /// </summary>
    [Fact]
    public async Task Adopt_ADeploymentAnsweringPastWhatTheCommandBuffers_DoesNotSayTheWriteDidNothing()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            adoptable: FakeConfigurationDeployment.Reading(
                version: 8,
                (SnippetsPerEmail, "3", "file", "10-deployment.json", false)),
            write: FakeConfigurationDeployment.Committed(
                version: 9,
                before: ("3", "file"),
                after: (new string('9', DeploymentTransport.ResponseSizeLimitInBytes + 1), "persisted-layer")));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "config",
            "adopt",
            "MailboxSearch",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("cannot say what the deployment did with the request", StringComparison.Ordinal));
        Assert.DoesNotContain(
            this.harness.Console.Errors,
            line => line.Contains("mfctl config show <prefix>", StringComparison.Ordinal));
    }

    /// <summary>A secret-bearing setting says so, because a marker read as a value is a value an operator would persist.</summary>
    [Fact]
    public async Task Get_ASecretBearingSetting_SaysTheValueIsRedactedRatherThanWhatItReads()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            reading: FakeConfigurationDeployment.Reading(
                version: 1,
                ("Chat:ApiKey:SecretReference", "(redacted)", "persisted-layer", null, true)));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "config",
            "get",
            "Chat:ApiKey:SecretReference",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("bears a secret", StringComparison.Ordinal));
    }

    /// <summary>A section is printed as the tree its paths describe, with the source beside every leaf.</summary>
    [Fact]
    public async Task Show_ASection_PrintsTheSettingsBeneathItAsATreeWithTheirSources()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            reading: FakeConfigurationDeployment.Reading(
                version: 6,
                (SnippetsPerEmail, "3", "persisted-layer", null, false),
                ("MailboxSearch:WordsPerSnippet", "12", "file", "10-deployment.json", false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "show", "MailboxSearch", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line == "MailboxSearch:");
        Assert.Contains(this.harness.Console.Lines, line => line == "  SnippetsPerEmail = 3 [persisted-layer]");
        Assert.Contains(this.harness.Console.Lines, line => line == "  WordsPerSnippet = 12 [file (10-deployment.json)]");
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("version 6", StringComparison.Ordinal));
        Assert.Equal("?prefix=MailboxSearch", deployment.LastReadingQuery());
    }

    /// <summary>A path no source supplies is a fact about the deployment rather than a failure of the command.</summary>
    [Fact]
    public async Task Show_APathNoSourceSupplies_SaysSoAndSucceeds()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            reading: FakeConfigurationDeployment.NoSettings(version: 1));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "show", "MailboxSearch", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("No source supplies any setting beneath MailboxSearch", StringComparison.Ordinal));
    }

    /// <summary>The change is composed over the version the reading carried, which is the whole of the concurrency contract.</summary>
    [Fact]
    public async Task Set_ASetting_SendsTheChangeOverTheVersionTheReadingCarried()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            reading: FakeConfigurationDeployment.Reading(
                version: 9,
                (SnippetsPerEmail, "3", "file", "10-deployment.json", false)),
            write: FakeConfigurationDeployment.Committed(
                version: 10,
                before: ("3", "file"),
                after: ("5", "persisted-layer")));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "set", SnippetsPerEmail, "5", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = SentWrite(deployment);
        Assert.Equal(9, sent.GetProperty("version").GetInt64());
        Assert.False(sent.GetProperty("evenIfShadowed").GetBoolean());

        var change = Assert.Single(sent.GetProperty("changes").EnumerateArray().ToArray());
        Assert.Equal(SnippetsPerEmail, change.GetProperty("path").GetString());
        Assert.Equal("5", change.GetProperty("value").GetString());
    }

    /// <summary>The commit is reported with what the setting read as on each side, which is what says the write took effect.</summary>
    [Fact]
    public async Task Set_ACommittedChange_PrintsTheVersionAndBothReadings()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            write: FakeConfigurationDeployment.Committed(
                version: 4,
                before: ("3", "file"),
                after: ("5", "persisted-layer")));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "set", SnippetsPerEmail, "5", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("Committed persisted configuration version 4", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("before: 3 (from file)", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("now:    5 (from persisted-layer)", StringComparison.Ordinal));
    }

    /// <summary>
    /// A refusal about a setting an override supplies is a failure, because the write was meant and did not happen —
    /// and the flag that answers it is named, since staging a value beneath an override is a thing an operator means.
    /// </summary>
    [Fact]
    public async Task Set_ASettingAnOverrideSupplies_FailsAndNamesTheFlagThatAnswersTheRefusal()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            write: FakeConfigurationDeployment.Refused(
                ConfigurationWriteAnswer.WriteShadowed,
                version: 1,
                "An environment variable already supplies this setting."));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "set", SnippetsPerEmail, "5", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("environment variable", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines.Concat(this.harness.Console.Errors),
            line => line.Contains("--even-if-shadowed", StringComparison.Ordinal));
    }

    /// <summary>The flag reaches the deployment, because it is the deployment that refuses a shadowed write.</summary>
    [Fact]
    public async Task Set_TheShadowingFlag_StatesItInTheWriteRatherThanActingOnItHere()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            write: FakeConfigurationDeployment.Committed(version: 2));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "config",
            "set",
            SnippetsPerEmail,
            "5",
            "--even-if-shadowed",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.True(SentWrite(deployment).GetProperty("evenIfShadowed").GetBoolean());
    }

    /// <summary>
    /// A write that changed nothing is a success, because the deployment already reads as the operator asked for and a
    /// script repeating a command is not a script that has gone wrong.
    /// </summary>
    [Fact]
    public async Task Set_AValueTheDocumentAlreadyCarries_SucceedsAndSaysNothingWasWritten()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            write: FakeConfigurationDeployment.ChangedNothing(
                version: 1,
                "The persisted document already carries this value."));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "set", SnippetsPerEmail, "5", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("already carries this value", StringComparison.Ordinal));
        Assert.Empty(this.harness.Console.Failures);
    }

    /// <summary>
    /// Unsetting sends an absent value rather than an empty one. The persisted layer is sparse, so the first restores
    /// the file beneath and the second shadows it with nothing, and the two are opposite acts.
    /// </summary>
    [Fact]
    public async Task Unset_ASetting_SendsAnAbsentValueRatherThanAnEmptyOne()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            write: FakeConfigurationDeployment.Committed(
                version: 3,
                before: ("5", "persisted-layer"),
                after: ("3", "file")));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "unset", SnippetsPerEmail, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var change = Assert.Single(SentWrite(deployment).GetProperty("changes").EnumerateArray().ToArray());
        Assert.Equal(JsonValueKind.Null, change.GetProperty("value").ValueKind);
    }

    /// <summary>Nothing is opened without an editor to open it in, because choosing one for an operator is not this command's to do.</summary>
    [Fact]
    public async Task Edit_NoEditorNamedByTheShell_FailsWithoutReadingTheDocument()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding();

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains(OperatorEditor.VisualVariable, StringComparison.Ordinal));

        // Every request rather than the writes alone. Reading the document is a GET, so a command reordered to fetch
        // before it looks for an editor would write a full description of the deployment's persisted configuration
        // into a temporary file on a run that was always going to refuse, and a write count would report nothing.
        Assert.Empty(deployment.RecordedRequests);
    }

    /// <summary>What the operator saved is what is committed, over the version the buffer was opened at.</summary>
    [Fact]
    public async Task Edit_AnEditedBuffer_CommitsWhatWasSavedOverTheVersionItWasOpenedAt()
    {
        // Arrange
        const string edited = """{ "MailboxSearch": { "SnippetsPerEmail": "7" } }""";

        using var deployment = FakeConfigurationDeployment.Holding(
            documents: [FakeConfigurationDeployment.Document(version: 5, """{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""")],
            write: FakeConfigurationDeployment.Committed(version: 6));

        this.EditsTheBufferInto(edited);

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = SentWrite(deployment);
        Assert.Equal(5, sent.GetProperty("version").GetInt64());
        Assert.Equal(edited, sent.GetProperty("document").GetString());
    }

    /// <summary>
    /// The buffer is the document the deployment answered with, byte for byte. What the buffer may carry is the
    /// deployment's decision — <c>SettingRedaction.ApplyToDocument</c> makes it, and its own suite covers it — so what
    /// is asserted here is that this command neither composes a document of its own nor rewrites the one it was given.
    /// </summary>
    [Fact]
    public async Task Edit_ADocumentTheDeploymentRedacted_OpensTheBufferOnWhatTheDeploymentAnswered()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            documents:
            [
                FakeConfigurationDeployment.Document(version: 1, RedactedChatDocument),
            ]);

        var opened = string.Empty;
        this.OpensTheBufferWith((_, path) =>
        {
            opened = File.ReadAllText(path);

            return true;
        });

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(RedactedChatDocument, opened);
        Assert.Equal(0, deployment.ConfigurationWriteCount());
    }

    /// <summary>
    /// The buffer is a complete description of what a deployment does, and it is written into the machine's temporary
    /// directory — which on a shared host belongs to everybody. It is created readable by its owner alone, and the
    /// command deliberately leaves it there when the delete fails, so the mode is the whole of what protects it.
    /// </summary>
    [Fact]
    public async Task Edit_OnAPlatformWithFileModes_OpensABufferReadableByItsOwnerAlone()
    {
        // Arrange
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var deployment = FakeConfigurationDeployment.Holding(
            documents: [FakeConfigurationDeployment.Document(version: 1, RedactedChatDocument)]);

        UnixFileMode mode = default;
        this.OpensTheBufferWith((_, path) =>
        {
            // Guarded again inside the callback, which is the only place the mode can be read: the buffer is deleted
            // when the session ends. The platform analyzer reads a guard within the body it is protecting rather than
            // one in the method that supplied the callback, so the early return above cannot stand for this.
            if (!OperatingSystem.IsWindows())
            {
                mode = File.GetUnixFileMode(path);
            }

            return true;
        });

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    /// <summary>An emptied buffer is how every editor-driven command an operator has met is abandoned, and it is honoured as one.</summary>
    [Fact]
    public async Task Edit_AnEmptiedBuffer_WritesNothingAndSucceeds()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            documents: [FakeConfigurationDeployment.Document(version: 1, """{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""")]);

        this.EditsTheBufferInto(string.Empty);

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("emptied", StringComparison.Ordinal));
        Assert.Equal(0, deployment.ConfigurationWriteCount());
    }

    /// <summary>A buffer saved unchanged asks for nothing, so nothing is sent and no version is spent.</summary>
    [Fact]
    public async Task Edit_ABufferSavedUnchanged_WritesNothingAndSucceeds()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            documents: [FakeConfigurationDeployment.Document(version: 1, """{ "MailboxSearch": { "SnippetsPerEmail": "3" } }""")]);

        this.OpensTheBufferWith((_, _) => true);

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("saved unchanged", StringComparison.Ordinal));
        Assert.Equal(0, deployment.ConfigurationWriteCount());
    }

    /// <summary>An editor that did not finish is a session that produced nothing, so nothing is read back and nothing is sent.</summary>
    [Fact]
    public async Task Edit_AnEditorThatDidNotFinish_FailsWithoutWritingAnything()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding();

        this.OpensTheBufferWith((_, _) => false);

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("--wait", StringComparison.Ordinal));
        Assert.Equal(0, deployment.ConfigurationWriteCount());
    }

    /// <summary>
    /// An editor the operating system never started is a different repair from one that ran and returned early, so the
    /// system's own words are reported and the wait flag — which fixes nothing here — is not.
    /// </summary>
    [Fact]
    public async Task Edit_AnEditorThatNeverStarted_ReportsWhyRatherThanTheWaitFlag()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding();

        this.OpensTheBufferWith((_, _) => EditingSession.NeverStarted("No such file or directory"));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("No such file or directory", StringComparison.Ordinal));
        Assert.DoesNotContain(
            this.harness.Console.Errors,
            line => line.Contains("--wait", StringComparison.Ordinal));
        Assert.Equal(0, deployment.ConfigurationWriteCount());
    }

    /// <summary>
    /// A session refused for a version somebody else moved past is told what they changed, because that is the one
    /// thing the operator cannot work out from the buffer in front of them. Nothing of this session is applied on top.
    /// </summary>
    [Fact]
    public async Task Edit_AVersionAnotherWriterMovedPast_NamesWhatMovedAndCommitsNothingOnTopOfIt()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            documents:
            [
                FakeConfigurationDeployment.Document(version: 5, """{"MailboxSearch":{"SnippetsPerEmail":"3"}}"""),
                FakeConfigurationDeployment.Document(version: 6, """{"MailboxSearch":{"SnippetsPerEmail":"9","WordsPerSnippet":"12"}}"""),
            ],
            write: FakeConfigurationDeployment.Refused(
                ConfigurationWriteAnswer.VersionSuperseded,
                version: 6,
                "The document was composed over version 5 and version 6 is in force."));

        this.EditsTheBufferInto("""{"MailboxSearch":{"SnippetsPerEmail":"7"}}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "edit", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(1, deployment.ConfigurationWriteCount());
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("version 6 is in force", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains(SnippetsPerEmail, StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("MailboxSearch:WordsPerSnippet", StringComparison.Ordinal));
    }

    /// <summary>
    /// An adoption is previewed before it is agreed to, because afterwards the files stop deciding those settings. A
    /// declined one ends on standard error with a failing code, as every other confirmation-driven command does, so a
    /// wrapper cannot read it as an adoption that committed.
    /// </summary>
    [Fact]
    public async Task Adopt_TheQuestionAnsweredNo_PreviewsAndFailsWithoutAdoptingAnything()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            adoptable: FakeConfigurationDeployment.Reading(
                version: 1,
                (SnippetsPerEmail, "3", "file", "10-deployment.json", false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "config", "adopt", "MailboxSearch", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("10-deployment.json", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("Nothing was adopted", StringComparison.Ordinal));
        Assert.Equal(0, deployment.ConfigurationWriteCount());
    }

    /// <summary>The agreement stated in the command is what an unattended run has instead of somebody at the terminal.</summary>
    [Fact]
    public async Task Adopt_TheAgreementStatedInTheCommand_AdoptsOverThePreviewsVersion()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            adoptable: FakeConfigurationDeployment.Reading(
                version: 8,
                (SnippetsPerEmail, "3", "file", "10-deployment.json", false)),
            write: FakeConfigurationDeployment.Committed(
                version: 9,
                before: ("3", "file"),
                after: ("3", "persisted-layer")));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "config",
            "adopt",
            "MailboxSearch",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = SentWrite(deployment);
        Assert.Equal(8, sent.GetProperty("version").GetInt64());
        Assert.Equal("MailboxSearch", sent.GetProperty("prefix").GetString());
    }

    /// <summary>A path whose settings the layer already carries is nothing to adopt, and is said rather than confirmed.</summary>
    [Fact]
    public async Task Adopt_APathTheLayerAlreadyCarries_SaysThereIsNothingToAdopt()
    {
        // Arrange
        using var deployment = FakeConfigurationDeployment.Holding(
            adoptable: FakeConfigurationDeployment.NoSettings(version: 1));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "config",
            "adopt",
            "MailboxSearch",
            "--yes",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("nothing to adopt", StringComparison.Ordinal));
        Assert.Equal(0, deployment.ConfigurationWriteCount());
    }

    /// <inheritdoc />
    public void Dispose() => this.harness.Dispose();

    /// <summary>Reads back the body of the write the command sent.</summary>
    private static JsonElement SentWrite(FakeHttpMessageHandler deployment) =>
        JsonDocument.Parse(deployment.LastConfigurationWrite() ?? "{}").RootElement;

    /// <summary>Stands in for the operator, saving the document they would have typed into the buffer.</summary>
    private void EditsTheBufferInto(string saved) =>
        this.OpensTheBufferWith((_, path) =>
        {
            File.WriteAllText(path, saved);

            return true;
        });

    /// <summary>Names an editor for the shell and states what the session does to the buffer.</summary>
    /// <remarks>
    /// Both halves, because the command reads the variable before it opens anything: a session scripted without one
    /// never reaches the editor at all, and the refusal it meets instead is the subject of a test of its own.
    /// </remarks>
    private void OpensTheBufferWith(Func<string, string, bool> session) =>
        this.OpensTheBufferWith((editor, path) =>
            session(editor, path) ? EditingSession.Finished : EditingSession.Failed);

    /// <summary>Names an editor for the shell and states what became of the session it opens.</summary>
    private void OpensTheBufferWith(Func<string, string, EditingSession> session)
    {
        this.harness.Variables[OperatorEditor.VisualVariable] = "vi";
        this.harness.Editor = session;
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
