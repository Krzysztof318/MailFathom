// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the four commands an operator carries a deployment's stored content into the bucket with.</summary>
/// <remarks>
/// What is asserted here is the agreement and the reading. A move rewrites where somebody's mail is held and runs for
/// hours, so an operator who does not agree must leave the deployment exactly as it was; and the backlog, the state, and
/// what to do next are what the operator acts on, so a command that reported a figure without saying which of the four
/// answers it is would leave a finished move and a stopped one looking the same.
/// </remarks>
public sealed class ContentMoveCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The backlog is read and put on the screen before anything is asked, whichever answer follows.</summary>
    [Fact]
    public async Task Move_AnAgreedMove_ReportsTheBacklogAndThenAsksForIt()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(
            FakeContentDeployment.Report(remainingPayloadCount: 22_500, remainingByteCount: 1_048_576));
        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "move", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.MoveRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("22,500 payloads carrying 1,048,576 bytes", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("content move-status", StringComparison.Ordinal));
    }

    /// <summary>An operator who does not agree leaves the deployment holding its content exactly where it was.</summary>
    [Fact]
    public async Task Move_AnOperatorWhoDeclines_AsksForNothing()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(
            FakeContentDeployment.Report(remainingPayloadCount: 22_500));
        this.harness.Console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "move", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.MoveRequestCount());
        Assert.Contains("Nothing was moved.", this.harness.Console.Errors);
    }

    /// <summary>
    /// A redirected input has nobody to ask, and reading the answer out of whatever was piped in would turn a stray
    /// line into an agreement to rewrite where somebody's whole mailbox is held.
    /// </summary>
    [Fact]
    public async Task Move_NobodyAtTheTerminal_RefusesRatherThanGuessing()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(FakeContentDeployment.Report());
        this.harness.Console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "move", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.MoveRequestCount());
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    /// <summary>The flag is an operator stating the agreement in the command, which is what a scripted move needs.</summary>
    [Fact]
    public async Task Move_TheAgreementStatedInTheCommand_AsksWithoutAsking()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(FakeContentDeployment.Report());
        this.harness.Console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "move", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.MoveRequestCount());
        Assert.Empty(this.harness.Console.Questions);
    }

    /// <summary>Watching a move asks for none, because a status command that started work would be a trap.</summary>
    [Fact]
    public async Task MoveStatus_AMoveUnderWay_ReportsHowFarItHasComeWithoutAskingForAnother()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(FakeContentDeployment.Report(
            FakeContentDeployment.Run(copiedPayloadCount: 1_043, movedByteCount: 4_096),
            remainingPayloadCount: 21_457));

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "move-status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.MoveRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("1,043 moved carrying 4,096 bytes", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("under way", StringComparison.Ordinal));
    }

    /// <summary>A deployment nobody has asked is told what to run, rather than left reporting a figure and nothing else.</summary>
    [Fact]
    public async Task MoveStatus_NoMoveEverAskedFor_SaysHowToStartOne()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(
            FakeContentDeployment.Report(remainingPayloadCount: 22_500));

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "move-status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("No move has been asked for", StringComparison.Ordinal)
                && line.Contains("content move", StringComparison.Ordinal));
    }

    /// <summary>
    /// A deployment that stores its content in the database answers the backlog and is told why nothing will move it,
    /// because the figure is exactly what an operator weighs before selecting the other backend.
    /// </summary>
    [Fact]
    public async Task MoveStatus_NoObjectBackendConfigured_ReportsTheBacklogAndWhyItStaysThere()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(FakeContentDeployment.Report(
            available: false,
            remainingPayloadCount: 22_500));

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "move-status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("names no object-storage endpoint", StringComparison.Ordinal));
    }

    /// <summary>A stopped move is one an operator resumes, and the command that does it is named rather than implied.</summary>
    [Fact]
    public async Task MoveStatus_AMoveStopped_SaysHowToSetItGoingAgain()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(FakeContentDeployment.Report(
            FakeContentDeployment.Run(state: "paused", copiedPayloadCount: 12),
            remainingPayloadCount: 8));

        // Act
        await this.RunAsync(deployment, "content", "move-status", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("content move-resume", StringComparison.Ordinal));
    }

    /// <summary>
    /// A finished move with content still in the database is the one answer that reads as a contradiction, so the
    /// command says what it means: those are the payloads a copy could not be vouched for, and a further move reaches
    /// them once the reason is repaired.
    /// </summary>
    [Fact]
    public async Task MoveStatus_AMoveThatFinishedWithContentLeftBehind_SaysAFurtherMoveWalksIt()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(FakeContentDeployment.Report(
            FakeContentDeployment.Run(
                state: "completed",
                copiedPayloadCount: 22_498,
                failedPayloadCount: 2,
                ended: true),
            remainingPayloadCount: 2));

        // Act
        await this.RunAsync(deployment, "content", "move-status", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("finished at 2026-08-24 14:20:00Z", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("could not be verified", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("walks what it left behind", StringComparison.Ordinal));
    }

    /// <summary>Stopping and resuming are separate paths, so each command writes to its own and to no other.</summary>
    [Theory]
    [InlineData("move-pause", AdminEndpointRoutes.ContentMovePausePath, AdminEndpointRoutes.ContentMoveResumePath)]
    [InlineData("move-resume", AdminEndpointRoutes.ContentMoveResumePath, AdminEndpointRoutes.ContentMovePausePath)]
    public async Task MoveDecision_ADeploymentWithAMove_WritesToItsOwnPathAlone(
        string verb,
        string expectedPath,
        string otherPath)
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(
            FakeContentDeployment.Report(FakeContentDeployment.Run()),
            FakeContentDeployment.Run(state: verb is "move-pause" ? "paused" : "running", copiedPayloadCount: 12));

        // Act
        var exitCode = await this.RunAsync(deployment, "content", verb, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.DecisionCount(expectedPath));
        Assert.Equal(0, deployment.DecisionCount(otherPath));
        Assert.Equal(0, deployment.MoveRequestCount());
    }

    /// <summary>
    /// A deployment nobody asked for a move answers with nothing to act on, and the command says so rather than
    /// reporting a move that does not exist.
    /// </summary>
    [Theory]
    [InlineData("move-pause")]
    [InlineData("move-resume")]
    public async Task MoveDecision_NoMoveEverAskedFor_ReportsThereIsNoneToActOn(string verb)
    {
        // Arrange
        using var deployment = FakeContentDeployment.WithNoMoveToActOn(FakeContentDeployment.Report());

        // Act
        var exitCode = await this.RunAsync(deployment, "content", verb, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("never been asked to move its stored content", StringComparison.Ordinal));
    }

    public void Dispose() => this.harness.Dispose();

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
