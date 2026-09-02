// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the one command that disposes of mail: how far it goes, when it stops, and what it says it did.</summary>
/// <remarks>
/// What is asserted here is the repetition and the reporting. A folder larger than one pass is erased by asking again
/// until the deployment says nothing is left, so a command that sent one request and reported success would leave mail
/// behind while claiming the folder was empty — and a deployment's refusal has to reach the terminal as the sentence it
/// wrote rather than as a status code the operator has to look up.
/// </remarks>
public sealed class FolderCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;
    private const string Account = "work";
    private const string Folder = "archive";

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));

    /// <summary>A folder small enough for one pass is one request, and the command says the folder now holds none.</summary>
    [Fact]
    public async Task Erase_AFolderOnePassEmpties_ErasesItInOneRequestAndReportsTheCount()
    {
        // Arrange
        using var deployment = FakeFolderDeployment.Erasing(
            FakeFolderDeployment.Pass(erasedEmailCount: 12, emailsRemain: false));

        // Act
        var exitCode = await this.EraseAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.ErasureRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("12 stored emails erased", StringComparison.Ordinal)
                && line.Contains("ARCHIVE", StringComparison.Ordinal));
    }

    /// <summary>
    /// The bounding and the repetition together. One pass is a transaction rather than a mailbox, so a folder larger
    /// than one is erased by asking again — and a command that stopped after the first would report an empty folder
    /// that still held mail.
    /// </summary>
    [Fact]
    public async Task Erase_AFolderLargerThanOnePass_KeepsAskingUntilTheDeploymentSaysNothingIsLeft()
    {
        // Arrange
        using var deployment = FakeFolderDeployment.Erasing(
            FakeFolderDeployment.Pass(erasedEmailCount: 500, emailsRemain: true),
            FakeFolderDeployment.Pass(erasedEmailCount: 500, emailsRemain: true),
            FakeFolderDeployment.Pass(erasedEmailCount: 43, emailsRemain: false));

        // Act
        var exitCode = await this.EraseAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(3, deployment.ErasureRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("1043 stored emails erased", StringComparison.Ordinal));
    }

    /// <summary>A long erasure says where it has got to, so a terminal is not silent while a mailbox is disposed of.</summary>
    [Fact]
    public async Task Erase_APassThatLeavesMailBehind_ReportsTheRunningTotalBeforeAskingAgain()
    {
        // Arrange
        using var deployment = FakeFolderDeployment.Erasing(
            FakeFolderDeployment.Pass(erasedEmailCount: 500, emailsRemain: true),
            FakeFolderDeployment.Pass(erasedEmailCount: 1, emailsRemain: false));

        // Act
        await this.EraseAsync(deployment);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("500 stored emails erased so far", StringComparison.Ordinal));
    }

    /// <summary>
    /// The command repeats until the deployment says nothing is left, so an answer that removed nothing while claiming
    /// more remains would repeat forever. Nothing this deployment's own eraser can produce says that, which is why it
    /// is reported rather than retried.
    /// </summary>
    [Fact]
    public async Task Erase_APassThatRemovedNothingWhileClaimingMoreRemains_StopsRatherThanAskingForever()
    {
        // Arrange
        using var deployment = FakeFolderDeployment.Erasing(
            FakeFolderDeployment.Pass(erasedEmailCount: 0, emailsRemain: true));

        // Act
        var exitCode = await this.EraseAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(1, deployment.ErasureRequestCount());
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("would not make progress", StringComparison.Ordinal));
    }

    /// <summary>Running it against a folder that holds nothing is the ordinary end of every erasure, not a failure.</summary>
    [Fact]
    public async Task Erase_AFolderHoldingNothing_SucceedsAndSaysNothingWasErased()
    {
        // Arrange
        using var deployment = FakeFolderDeployment.Erasing(
            FakeFolderDeployment.Pass(erasedEmailCount: 0, emailsRemain: false));

        // Act
        var exitCode = await this.EraseAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("stored nothing", StringComparison.Ordinal));
    }

    /// <summary>The deployment knows why it refused, so the sentence it wrote is what the operator reads.</summary>
    [Fact]
    public async Task Erase_AFolderTheDeploymentStillMirrors_ReportsTheRefusalItStated()
    {
        // Arrange
        const string Refusal =
            "The folder 'ARCHIVE' is still mirrored, so erasing it would only cost a remirror. Switch its Synchronize off, or remove its mapping, and ask again.";
        using var deployment = FakeFolderDeployment.Erasing((
            HttpStatusCode.BadRequest,
            $$"""{"detail":"{{Refusal}}"}"""));

        // Act
        var exitCode = await this.EraseAsync(deployment);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(Refusal, this.harness.Console.Errors);
    }

    /// <summary>The two names the deployment needs, and nothing else: no rule, no filter, no mail.</summary>
    [Fact]
    public async Task Erase_AnAccountAndAFolder_AsksTheDeploymentForExactlyThoseTwo()
    {
        // Arrange
        using var deployment = FakeFolderDeployment.Erasing(
            FakeFolderDeployment.Pass(erasedEmailCount: 0, emailsRemain: false));

        // Act
        await this.EraseAsync(deployment);

        // Assert
        Assert.Equal(
            $$"""{"account":"{{Account}}","folder":"{{Folder}}"}""",
            deployment.LastErasureRequest()?.Replace("\n", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal));
    }

    /// <summary>Both names are required, because guessing either would be guessing whose mail is disposed of.</summary>
    [Theory]
    [InlineData("--account", Account)]
    [InlineData("--folder", Folder)]
    public async Task Erase_OneOfTheTwoNamesLeftOut_RefusesWithoutReachingTheDeployment(string option, string value)
    {
        // Arrange
        using var deployment = FakeFolderDeployment.Erasing(
            FakeFolderDeployment.Pass(erasedEmailCount: 0, emailsRemain: false));

        // Act
        var exitCode = await this.RunAsync(deployment, "folder", "erase", option, value, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ErasureRequestCount());
    }

    private Task<int> EraseAsync(FakeHttpMessageHandler deployment) => this.RunAsync(
        deployment,
        "folder",
        "erase",
        "--account",
        Account,
        "--folder",
        Folder,
        "--endpoint",
        Endpoint);

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
