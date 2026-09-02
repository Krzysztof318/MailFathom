// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Text;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using Xunit;

namespace MailFathom.IntegrationTests.Secrets;

/// <summary>Proves the two readers that reach outside the process behave as their ports promise.</summary>
/// <remarks>
/// <para>
/// Both are deliberately thin, and both are unreachable from a unit test for the same reason: what they do is call the
/// platform. The file reader's whole contract is that a real file system's refusals — an absent path, a directory, a
/// malformed target — arrive as one named failure rather than as an exception carrying the path, and the environment
/// reader's is that it reads the process it runs in.
/// </para>
/// <para>
/// No orchestrated infrastructure is involved, so this class joins no collection and runs beside the tests that share
/// the database. Its files live in a directory of its own under the system temporary path, and its environment variables
/// carry names unique to each test, so nothing here collides with a test running alongside it.
/// </para>
/// </remarks>
public sealed class SecretMaterialReaderTests : IDisposable
{
    /// <summary>The material a provisioned file carries, ending in the newline such files routinely have.</summary>
    private const string ProvisionedMaterial = "orchestrated-secret-material";

    private readonly string secretDirectory;
    private readonly FileSystemSecretFileReader reader = new(TimeProvider.System);

    public SecretMaterialReaderTests()
    {
        this.secretDirectory = Path.Combine(Path.GetTempPath(), $"mailfathom-secrets-{Guid.NewGuid():N}");

        Directory.CreateDirectory(this.secretDirectory);
    }

    [Fact]
    public async Task ReadAsync_ForAProvisionedFile_ReturnsItsMaterialWithTheTextViewNewlineRemoved()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(this.secretDirectory, "provisioned");
        var fileContent = $"{ProvisionedMaterial}\n";
        await File.WriteAllTextAsync(path, fileContent, cancellationToken);

        // Act
        var result = await this.reader.ReadAsync(
            path,
            SecretMaterialLimits.MaximumMaterialByteCount,
            cancellationToken);

        // Assert
        Assert.True(result.Succeeded);
        Assert.Equal(SecretMaterialSource.SchemeAdapter, result.Source);

        using var material = result.Secret!;

        // The bytes are what the file held, and the text view is what a password-taking framework call receives: the
        // trailing newline a deployment's secret file carries must not present as part of a credential.
        Assert.Equal(Encoding.UTF8.GetBytes(fileContent), material.RevealBytes().ToArray());
        Assert.Equal(ProvisionedMaterial, material.RevealAsString());
    }

    /// <summary>Proves every refusal a real file system produces arrives as one result rather than as an exception.</summary>
    /// <remarks>
    /// The three targets are one behavior over three inputs, and they are asserted together because what matters is that
    /// none of them is the exception the platform would otherwise throw: a missing path, a directory, and a path holding
    /// a NUL character each reach a different <c>FileStream</c> failure, and any one of them escaping would put the
    /// target into an unhandled startup exception's message.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ForATargetTheFileSystemRefuses_ReportsMaterialNotFoundWithoutRevealingIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        string[] refusedTargets =
        [
            Path.Combine(this.secretDirectory, "never-provisioned"),
            this.secretDirectory,
            Path.Combine(this.secretDirectory, "malformed\0target"),
        ];

        // Act
        var results = new List<SecretResolutionResult>();
        foreach (var refusedTarget in refusedTargets)
        {
            results.Add(await this.reader.ReadAsync(
                refusedTarget,
                SecretMaterialLimits.MaximumMaterialByteCount,
                cancellationToken));
        }

        // Assert
        Assert.All(results, result =>
        {
            Assert.False(result.Succeeded);
            Assert.Equal(SecretResolutionFailure.MaterialNotFound, result.Failure);
            Assert.Null(result.Secret);
        });
    }

    /// <summary>Proves the ceiling is enforced while reading rather than after the file has been loaded.</summary>
    [Fact]
    public async Task ReadAsync_ForAFileLargerThanTheCeiling_ReportsMaterialTooLarge()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(this.secretDirectory, "oversized");
        await File.WriteAllTextAsync(path, new string('m', 64), cancellationToken);

        // Act
        var result = await this.reader.ReadAsync(path, maximumByteCount: 8, cancellationToken);

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(SecretResolutionFailure.MaterialTooLarge, result.Failure);
        Assert.Null(result.Secret);
    }

    /// <summary>Proves a device-backed pseudo-file is reported as the target it is rather than as an oversized secret.</summary>
    /// <remarks>
    /// Only the real platform establishes this. .NET publishes no portable file type, so what separates a regular file
    /// from a character device is what an opened handle reports and yields, and a substitute asserting that would only
    /// be asserting its own arrangement.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ForACharacterDevice_ReportsTargetNotRegularFile()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var result = await this.reader.ReadAsync(
            "/dev/zero",
            SecretMaterialLimits.MaximumMaterialByteCount,
            cancellationToken);

        // Assert
        Assert.Equal(SecretResolutionFailure.TargetNotRegularFile, result.Failure);
        Assert.Null(result.Secret);
    }

    /// <summary>Proves the defect this deadline exists for: an open the kernel never returns from fails rather than hangs.</summary>
    /// <remarks>
    /// A FIFO with no writer is the one target no unit test can reproduce, because what makes it dangerous is that the
    /// blocking happens below every cancellation token .NET has. The test pays the deadline in real time once, which
    /// is the price of proving that startup reports this rather than waiting for it forever.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ForAFifoNoWriterHasOpened_ReportsRetrievalTimedOutRatherThanBlocking()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var path = Path.Combine(this.secretDirectory, "fifo");
        using (var mkfifo = Process.Start("mkfifo", path))
        {
            await mkfifo.WaitForExitAsync(cancellationToken);

            Assert.Equal(0, mkfifo.ExitCode);
        }

        // Act
        var result = await this.reader.ReadAsync(
            path,
            SecretMaterialLimits.MaximumMaterialByteCount,
            cancellationToken);

        // Assert
        Assert.Equal(SecretResolutionFailure.RetrievalTimedOut, result.Failure);
        Assert.Null(result.Secret);

        // Release the thread the abandoned open left in the kernel. A FIFO's reader returns the moment a writer
        // arrives, and leaving one blocked for the rest of the run is the accumulation the retrieval bound exists to
        // cap rather than something a test should contribute to.
        var openTheWriteEnd = Task.Run(
            () =>
            {
                using var writeEnd = new FileStream(path, FileMode.Open, FileAccess.Write);
            },
            cancellationToken);

        await openTheWriteEnd.WaitAsync(TimeSpan.FromMinutes(1), cancellationToken);
    }

    [Fact]
    public void GetValue_ForAVariableTheProcessCarries_ReturnsWhatWasSetOnIt()
    {
        // Arrange
        var name = $"MAILFATHOM_INTEGRATION_SECRET_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(name, ProvisionedMaterial);

        try
        {
            // Act
            var value = new ProcessEnvironmentVariableReader().GetValue(name);

            // Assert
            Assert.Equal(ProvisionedMaterial, value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [Fact]
    public void GetValue_ForAVariableTheProcessDoesNotCarry_ReturnsNull()
    {
        // Arrange
        var name = $"MAILFATHOM_INTEGRATION_ABSENT_{Guid.NewGuid():N}";

        // Act
        var value = new ProcessEnvironmentVariableReader().GetValue(name);

        // Assert
        Assert.Null(value);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.reader.Dispose();

        Directory.Delete(this.secretDirectory, recursive: true);
    }
}
