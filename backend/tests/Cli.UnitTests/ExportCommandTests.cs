// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the six commands a mailbox leaves this deployment through.</summary>
/// <remarks>
/// What is asserted here is the agreement, the sequence, and what reaches the disk. An export is a second full copy of
/// somebody's whole mailbox, so an operator who does not agree must leave the deployment holding nothing extra; the
/// measurement has to be shown before the question rather than after it; and a download that overwrote a file would
/// destroy the one copy of a drained mailbox somebody was carrying away.
/// </remarks>
public sealed class ExportCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private static readonly string ExportArgument = FakeExportDeployment.ExportId.ToString("D");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

    private readonly string downloadDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-export-tests-{Guid.NewGuid():N}");

    /// <summary>Measuring reads recorded lengths rather than payloads, so it says what it would carry and writes nothing.</summary>
    [Fact]
    public async Task Measure_AMailbox_ReportsWhatItWouldCarryAndStartsNothing()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();

        // Act
        var exitCode = await this.RunAsync(deployment, "export", "measure", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.StartRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("1,240 messages carrying 8,388,608 bytes", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("export start", StringComparison.Ordinal));
    }

    /// <summary>An operator past the size limit exports one folder at a time, so which folder holds the weight is the answer they need.</summary>
    [Fact]
    public async Task Measure_AMailboxMeasuredPerFolder_ReportsWhatEachFolderContributes()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting(
            FakeExportDeployment.Measurement(
                folders: $"[{FakeExportDeployment.Folder("INBOX", 900, 6_000_000)},{FakeExportDeployment.Folder("Archive", 340, 2_388_608)}]"));

        // Act
        await this.RunAsync(deployment, "export", "measure", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("INBOX", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("2,388,608", StringComparison.Ordinal));
    }

    /// <summary>The folder narrows the question rather than the answer, so it travels to the deployment rather than being filtered here.</summary>
    [Fact]
    public async Task Measure_OneFolderOfAMailbox_AsksTheDeploymentAboutThatFolder()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();

        // Act
        await this.RunAsync(
            deployment,
            "export", "measure", "--account", "work", "--folder", "Archive", "--endpoint", Endpoint);

        // Assert
        var query = deployment.QuerySentTo(AdminEndpointRoutes.MailboxExportMeasurementPath);
        Assert.Contains("account=work", query, StringComparison.Ordinal);
        Assert.Contains("folder=Archive", query, StringComparison.Ordinal);
    }

    /// <summary>The figure the operator agrees to is the deployment's own, so it is measured and shown before the question.</summary>
    [Fact]
    public async Task Start_AnAgreedExport_MeasuresAndReportsItBeforeAskingForTheExport()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();
        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(deployment, "export", "start", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.RequestCount(HttpMethod.Get, AdminEndpointRoutes.MailboxExportMeasurementPath));
        Assert.Equal(1, deployment.StartRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("1,240 messages carrying 8,388,608 bytes", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("export download", StringComparison.Ordinal));
    }

    /// <summary>An operator who does not agree leaves the deployment holding nothing it was not already holding.</summary>
    [Fact]
    public async Task Start_AnOperatorWhoDeclines_AsksForNothing()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();
        this.harness.Console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "export", "start", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.StartRequestCount());
        Assert.Contains("Nothing was exported.", this.harness.Console.Errors);
    }

    /// <summary>
    /// A redirected input has nobody to ask, and reading the answer out of whatever was piped in would turn a stray line
    /// into an agreement to write a second full copy of somebody's mailbox.
    /// </summary>
    [Fact]
    public async Task Start_NobodyAtTheTerminal_RefusesRatherThanGuessing()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();
        this.harness.Console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "export", "start", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.StartRequestCount());
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    /// <summary>The flag is an operator stating the agreement in the command, which is what a scripted export needs.</summary>
    [Fact]
    public async Task Start_TheAgreementStatedInTheCommand_AsksWithoutAsking()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();
        this.harness.Console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "start", "--account", "work", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.StartRequestCount());
    }

    /// <summary>
    /// Asking again for an export already being written answers with that one rather than starting a second, and saying
    /// so is the difference between an operator waiting and an operator asking a third time.
    /// </summary>
    [Fact]
    public async Task Start_AnExportAlreadyBeingWritten_SaysNothingWasStartedOver()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting(
            start: FakeExportDeployment.Start(
                FakeExportDeployment.Export(state: "Running", finished: false),
                wasAlreadyRunning: true));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "start", "--account", "work", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("already being written", StringComparison.Ordinal));
    }

    /// <summary>An operator who has lost the identity lists what the account has and finds it there.</summary>
    [Fact]
    public async Task Status_AnAccountWithExports_ReportsThemAsAListing()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting(
            listing: FakeExportDeployment.Listing(
                FakeExportDeployment.Export(),
                FakeExportDeployment.Export(state: "Running", folder: "Archive", finished: false)));

        // Act
        var exitCode = await this.RunAsync(deployment, "export", "status", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("whole mailbox", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Archive", StringComparison.Ordinal));
    }

    /// <summary>An account nobody has exported is not a failure, so it is said in a line rather than reported as one.</summary>
    [Fact]
    public async Task Status_AnAccountWithNoExports_SaysSoAndSucceeds()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting(listing: FakeExportDeployment.Listing());

        // Act
        var exitCode = await this.RunAsync(deployment, "export", "status", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("no exports", StringComparison.Ordinal));
    }

    /// <summary>Naming an export narrows the answer to that export, which is what an operator following one asked for.</summary>
    [Fact]
    public async Task Status_OneNamedExport_ReportsWhereThatExportStands()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "status", "--account", "work", "--export", ExportArgument, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.RequestCount(HttpMethod.Get, AdminEndpointRoutes.MailboxExportPath(FakeExportDeployment.ExportId)));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("finished at 2026-09-15 11:20:00Z", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("deleted at 2026-09-17 11:20:00Z", StringComparison.Ordinal));
    }

    /// <summary>A failed export says which error stopped it, because the code is what an operator matches against the deployment's log.</summary>
    [Fact]
    public async Task Status_AFailedExport_ReportsTheErrorThatStoppedIt()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting(
            export: FakeExportDeployment.Export(state: "Failed", finished: false, failureCode: 14_210));

        // Act
        await this.RunAsync(
            deployment,
            "export", "status", "--account", "work", "--export", ExportArgument, "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("failed with error 14210", StringComparison.Ordinal));
    }

    /// <summary>The archive is written to disk as it arrives, and the command reports where it went and how large it is.</summary>
    [Fact]
    public async Task Download_AFinishedArchive_WritesItToTheNamedFileAndReportsItsSize()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();
        var destination = this.DownloadPath("carried.zip");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "download", "--account", "work", "--export", ExportArgument, "--output", destination,
            "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(FakeExportDeployment.Archive(), await File.ReadAllBytesAsync(destination, TestContext.Current.CancellationToken));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("4,100 bytes", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("export delete", StringComparison.Ordinal));
    }

    /// <summary>
    /// The archive is the only copy of a drained mailbox somebody is carrying away, so a file already there is left
    /// exactly as it was rather than written over.
    /// </summary>
    [Fact]
    public async Task Download_AFileThatAlreadyExists_LeavesItAsItWasAndReportsTheRefusal()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();
        var destination = this.DownloadPath("already-there.zip");
        await File.WriteAllTextAsync(destination, "somebody else's archive", TestContext.Current.CancellationToken);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "download", "--account", "work", "--export", ExportArgument, "--output", destination,
            "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            "somebody else's archive",
            await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A body that cannot be read leaves no half-written archive at the path the operator named. The cleanup runs only
    /// over a file this attempt created itself, so it has to create one before it can be reached — which is why the
    /// destination here is a path nothing occupies.
    /// </summary>
    [Fact]
    public async Task Download_AResponseWhoseBodyCannotBeRead_LeavesNothingAtThePathItHadAlreadyCreated()
    {
        // Arrange
        using var deployment = FakeExportDeployment.FailingWhileTheArchiveIsRead();
        var destination = this.DownloadPath("half-written.zip");

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "download", "--account", "work", "--export", ExportArgument, "--output", destination,
            "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.False(File.Exists(destination));
    }

    /// <summary>
    /// The file already at the path is not this attempt's to remove, however the attempt ends. For a drained account
    /// an earlier archive there is the mailbox's only copy, so a download that refuses the path leaves it byte for
    /// byte — which is what the creation happening before the response is read is for.
    /// </summary>
    [Fact]
    public async Task Download_AFailingResponseOverAFileThatAlreadyExists_LeavesTheEarlierArchiveWhereItIs()
    {
        // Arrange
        using var deployment = FakeExportDeployment.FailingWhileTheArchiveIsRead();
        var destination = this.DownloadPath("earlier-export.zip");
        await File.WriteAllTextAsync(destination, "an earlier archive", TestContext.Current.CancellationToken);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "download", "--account", "work", "--export", ExportArgument, "--output", destination,
            "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            "an earlier archive",
            await File.ReadAllTextAsync(destination, TestContext.Current.CancellationToken));
    }

    /// <summary>Cancelling deletes what the export had written, so an operator who does not agree stops nothing.</summary>
    [Fact]
    public async Task Cancel_AnOperatorWhoDeclines_StopsNothing()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();
        this.harness.Console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "cancel", "--account", "work", "--export", ExportArgument, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            0,
            deployment.RequestCount(HttpMethod.Post, AdminEndpointRoutes.MailboxExportCancellationPath(FakeExportDeployment.ExportId)));
        Assert.Contains("Nothing was cancelled.", this.harness.Console.Errors);
    }

    /// <summary>An agreed cancellation reaches the cancellation path and reports the export as it now stands.</summary>
    [Fact]
    public async Task Cancel_AnAgreedCancellation_StopsTheExportAndReportsWhereItStands()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting(
            export: FakeExportDeployment.Export(state: "Cancelled", finished: false));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "cancel", "--account", "work", "--export", ExportArgument, "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(
            1,
            deployment.RequestCount(HttpMethod.Post, AdminEndpointRoutes.MailboxExportCancellationPath(FakeExportDeployment.ExportId)));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("cancelled", StringComparison.Ordinal));
    }

    /// <summary>Deleting frees the storage the second copy occupied, and an operator who does not agree leaves it there.</summary>
    [Fact]
    public async Task Delete_AnOperatorWhoDeclines_LeavesTheArchiveWhereItIs()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting();
        this.harness.Console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "delete", "--account", "work", "--export", ExportArgument, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            0,
            deployment.RequestCount(HttpMethod.Delete, AdminEndpointRoutes.MailboxExportPath(FakeExportDeployment.ExportId)));
        Assert.Contains("Nothing was deleted.", this.harness.Console.Errors);
    }

    /// <summary>An agreed deletion removes the archive and reports the export with nothing left to keep.</summary>
    [Fact]
    public async Task Delete_AnAgreedDeletion_RemovesTheArchiveAndReportsThatNothingIsKept()
    {
        // Arrange
        using var deployment = FakeExportDeployment.Exporting(
            export: FakeExportDeployment.Export(state: "Deleted", finished: false));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "delete", "--account", "work", "--export", ExportArgument, "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(
            1,
            deployment.RequestCount(HttpMethod.Delete, AdminEndpointRoutes.MailboxExportPath(FakeExportDeployment.ExportId)));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("no archive to keep", StringComparison.Ordinal));
    }

    /// <summary>
    /// An identity the account holds nothing under is what an operator typing the wrong one meets, and it has to name
    /// the export rather than reading as a deployment that cannot be reached.
    /// </summary>
    [Fact]
    public async Task Status_AnExportTheAccountDoesNotHold_ReportsThatRatherThanAnUnreachableDeployment()
    {
        // Arrange
        using var deployment = FakeExportDeployment.WithNoSuchExport();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "export", "status", "--account", "work", "--export", ExportArgument, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("export", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.harness.Dispose();

        if (Directory.Exists(this.downloadDirectory))
        {
            Directory.Delete(this.downloadDirectory, recursive: true);
        }
    }

    private string DownloadPath(string fileName)
    {
        Directory.CreateDirectory(this.downloadDirectory);

        return Path.Combine(this.downloadDirectory, fileName);
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
