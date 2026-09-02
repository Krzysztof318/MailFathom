// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Runtime.InteropServices;
using MailFathom.Cli.Diagnostics;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the file the record is appended to: its shape, its ceiling, and what it does when it cannot be written.</summary>
/// <remarks>
/// Against a real directory under the temporary path, as <see cref="CredentialStoreTests" /> is, because what is under
/// test is file behaviour — appending, rolling over, and refusing — and a substitute for the file system would assert
/// the substitute. Each instance owns a directory named after a fresh identifier and removes it, so nothing here is
/// shared with another test.
/// </remarks>
public sealed class FileCliInvocationLogTests : IDisposable
{
    private readonly string directory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-cli-log-tests-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this.directory))
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }

    /// <summary>A record is appended as one line of JSON, in a file created under a directory that did not exist.</summary>
    [Fact]
    public void TryAppend_TheFirstRecord_WritesOneLineAndCreatesTheDirectory()
    {
        // Arrange
        var log = new FileCliInvocationLog(this.Location());

        // Act
        var written = log.TryAppend(Entry("mfctl status"));

        // Assert
        var lines = File.ReadAllLines(this.Location());

        Assert.True(written);
        Assert.Equal("mfctl status", Assert.Single(lines).Read().Command);
    }

    /// <summary>A second record joins the first rather than replacing it, which is what makes the file a history.</summary>
    [Fact]
    public void TryAppend_ASecondRecord_LeavesTheFirstInPlace()
    {
        // Arrange
        var log = new FileCliInvocationLog(this.Location());

        // Act
        _ = log.TryAppend(Entry("mfctl status"));
        _ = log.TryAppend(Entry("mfctl profiles"));

        // Assert
        var commands = File.ReadAllLines(this.Location()).Select(line => line.Read().Command).ToArray();

        Assert.Equal(["mfctl status", "mfctl profiles"], commands);
    }

    /// <summary>Every field the record carries survives the round trip, including the ones that may be absent.</summary>
    [Fact]
    public void TryAppend_ARecordOfAFailedInvocation_WritesEveryFieldItCarries()
    {
        // Arrange
        var log = new FileCliInvocationLog(this.Location());
        var entry = Entry("mfctl contacts delete") with
        {
            Outcome = CliInvocationOutcome.Failed,
            ExitCode = CliExitCode.Failure,
            Deployment = "production",
            Failure = "The deployment answered 404 rather than a contact.",
        };

        // Act
        _ = log.TryAppend(entry);

        // Assert
        var written = Assert.Single(File.ReadAllLines(this.Location())).Read();

        Assert.Equal(entry, written);
    }

    /// <summary>A field the record does not carry is left out rather than written as a null, so a line stays readable.</summary>
    [Fact]
    public void TryAppend_ARecordThatReachedNoDeployment_WritesNoDeploymentField()
    {
        // Arrange
        var log = new FileCliInvocationLog(this.Location());

        // Act
        _ = log.TryAppend(Entry("mfctl status"));

        // Assert
        var line = Assert.Single(File.ReadAllLines(this.Location()));

        Assert.DoesNotContain("deployment", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure", line, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Past its ceiling the file is moved aside and a new one started, so the log cannot grow without limit.</summary>
    [Fact]
    public void TryAppend_AFileThatReachedItsCeiling_StartsANewOneAndKeepsTheOld()
    {
        // Arrange
        var log = new FileCliInvocationLog(this.Location());

        Directory.CreateDirectory(this.directory);
        File.WriteAllBytes(this.Location(), new byte[FileCliInvocationLog.MaximumBytes]);

        // Act
        var written = log.TryAppend(Entry("mfctl status"));

        // Assert
        var rolled = this.Location() + FileCliInvocationLog.RolledSuffix;

        Assert.True(written);
        Assert.Equal("mfctl status", Assert.Single(File.ReadAllLines(this.Location())).Read().Command);
        Assert.Equal(FileCliInvocationLog.MaximumBytes, new FileInfo(rolled).Length);
    }

    /// <summary>A file still under its ceiling is appended to rather than rolled over.</summary>
    /// <remarks>
    /// The control for the test above: a rollover on every append would satisfy that assertion just as well and would
    /// leave the log holding one record.
    /// </remarks>
    [Fact]
    public void TryAppend_AFileUnderItsCeiling_KeepsWritingToIt()
    {
        // Arrange
        var log = new FileCliInvocationLog(this.Location());

        Directory.CreateDirectory(this.directory);
        File.WriteAllBytes(this.Location(), new byte[FileCliInvocationLog.MaximumBytes - 1]);

        // Act
        _ = log.TryAppend(Entry("mfctl status"));

        // Assert
        Assert.False(File.Exists(this.Location() + FileCliInvocationLog.RolledSuffix));
        Assert.True(new FileInfo(this.Location()).Length > FileCliInvocationLog.MaximumBytes - 1);
    }

    /// <summary>
    /// The log names which deployments an operator administers and when, which is what makes it worth reading on a
    /// shared machine, so it is created on the credential store's terms: readable by its owner and nobody else, and set
    /// as the file is created rather than tightened afterwards. Windows carries no mode to read back, as
    /// <see cref="CredentialStoreTests" /> says of the store itself.
    /// </summary>
    [Fact]
    public void TryAppend_OnAPlatformWithFileModes_CreatesTheLogReadableByItsOwnerAlone()
    {
        // Arrange
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var log = new FileCliInvocationLog(this.Location());

        // Act
        _ = log.TryAppend(Entry("mfctl status"));

        // Assert
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(this.Location()));
    }

    /// <summary>A path that cannot be written to is reported rather than raised, which is what keeps a command's own answer intact.</summary>
    [Fact]
    public void TryAppend_APathThatIsADirectory_ReportsThatItCouldNotBeWritten()
    {
        // Arrange
        var log = new FileCliInvocationLog(this.Location());

        Directory.CreateDirectory(this.Location());

        // Act
        var written = log.TryAppend(Entry("mfctl status"));

        // Assert
        Assert.False(written);
    }

    private static CliInvocationEntry Entry(string command) => new(
        new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero),
        command,
        CliInvocationOutcome.Completed,
        DurationMilliseconds: 42)
    {
        ExitCode = CliExitCode.Success,
    };

    private string Location() => Path.Combine(this.directory, FileCliInvocationLog.FileName);
}
