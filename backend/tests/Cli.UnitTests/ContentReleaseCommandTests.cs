// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the one command that removes mail, and every reason it declines to.</summary>
/// <remarks>
/// Releasing frees the last copy of a message this deployment holds outside its bucket, so what is asserted here is the
/// agreement, the three refusals, and the loop. The loop matters as much as the refusals: the deployment frees a bounded
/// batch per request, so a command that asked once would leave a mailbox part released and report success.
/// </remarks>
public sealed class ContentReleaseCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

    /// <summary>What is duplicated is read and put on the screen before anything is freed, whichever answer follows.</summary>
    [Fact]
    public async Task Release_AnAgreedRelease_ReportsWhatIsDuplicatedAndThenFreesIt()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Releasing(
            FakeContentDeployment.ReleaseReport(retainedPayloadCount: 22_500, retainedByteCount: 1_048_576),
            FakeContentDeployment.ReleaseReport(releasedPayloadCount: 22_500, releasedByteCount: 1_048_576));
        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "release", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.ReleaseRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("22,500 payloads carrying 1,048,576 bytes", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("only copy of that content", StringComparison.Ordinal));
    }

    /// <summary>One request frees one batch, so the command sends them until the deployment retains nothing.</summary>
    [Fact]
    public async Task Release_MoreCopiesThanOneBatchFrees_SendsBatchesUntilNothingIsRetained()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Releasing(
            FakeContentDeployment.ReleaseReport(retainedPayloadCount: 500, retainedByteCount: 3_000),
            FakeContentDeployment.ReleaseReport(
                releasedPayloadCount: 200,
                releasedByteCount: 1_200,
                retainedPayloadCount: 300,
                retainedByteCount: 1_800),
            FakeContentDeployment.ReleaseReport(
                releasedPayloadCount: 200,
                releasedByteCount: 1_200,
                retainedPayloadCount: 100,
                retainedByteCount: 600),
            FakeContentDeployment.ReleaseReport(releasedPayloadCount: 100, releasedByteCount: 600));

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "release", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(3, deployment.ReleaseRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("500 payloads carrying 3,000 bytes", StringComparison.Ordinal));
    }

    /// <summary>
    /// Copies the safety interval is still holding are freed by no request, so a batch that freed nothing while copies
    /// remain ends the command rather than starting a loop the deployment would answer identically forever.
    /// </summary>
    [Fact]
    public async Task Release_CopiesTheSafetyIntervalStillHolds_StopsAndNamesTheSetting()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Releasing(
            FakeContentDeployment.ReleaseReport(retainedPayloadCount: 500, retainedByteCount: 3_000),
            FakeContentDeployment.ReleaseReport(
                releasedPayloadCount: 200,
                releasedByteCount: 1_200,
                retainedPayloadCount: 300,
                retainedByteCount: 1_800),
            FakeContentDeployment.ReleaseReport(retainedPayloadCount: 300, retainedByteCount: 1_800));

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "release", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(2, deployment.ReleaseRequestCount());
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("ContentStorage:Release:SafetyInterval", StringComparison.Ordinal));
    }

    /// <summary>A deployment whose move is unfinished is refused here rather than at the endpoint, and told what to run.</summary>
    [Fact]
    public async Task Release_ContentStillAwaitingTheMove_RefusesAndNamesTheMove()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Releasing(FakeContentDeployment.ReleaseReport(
            retainedPayloadCount: 500,
            awaitingMovePayloadCount: 12));
        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "release", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ReleaseRequestCount());
        Assert.Empty(this.harness.Console.Questions);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("content move", StringComparison.Ordinal));
    }

    /// <summary>A deployment holding no copy of what its bucket has is finished rather than in need of a request.</summary>
    [Fact]
    public async Task Release_NothingRetained_SaysSoAndAsksForNothing()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Releasing(FakeContentDeployment.ReleaseReport());

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "release", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.ReleaseRequestCount());
        Assert.Empty(this.harness.Console.Questions);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("nothing to release", StringComparison.Ordinal));
    }

    /// <summary>An operator who does not agree leaves the deployment holding both copies of everything.</summary>
    [Fact]
    public async Task Release_AnOperatorWhoDeclines_FreesNothing()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Releasing(
            FakeContentDeployment.ReleaseReport(retainedPayloadCount: 500, retainedByteCount: 3_000));
        this.harness.Console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "release", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ReleaseRequestCount());
        Assert.Contains("Nothing was released.", this.harness.Console.Errors);
    }

    /// <summary>
    /// A redirected input has nobody to ask, and reading the answer out of whatever was piped in would turn a stray line
    /// into an agreement to destroy the last copy of somebody's mail outside a bucket.
    /// </summary>
    [Fact]
    public async Task Release_NobodyAtTheTerminal_RefusesRatherThanGuessing()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Releasing(
            FakeContentDeployment.ReleaseReport(retainedPayloadCount: 500, retainedByteCount: 3_000));
        this.harness.Console.AnswersQuestions = false;

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "release", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(0, deployment.ReleaseRequestCount());
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    /// <summary>The duplication is part of where a deployment holds its mail, so watching the move reports it too.</summary>
    [Fact]
    public async Task MoveStatus_AFinishedMoveWhoseCopiesAreStillHeld_ReportsThemAndNamesTheReleaseCommand()
    {
        // Arrange
        using var deployment = FakeContentDeployment.Moving(
            FakeContentDeployment.Report(
                FakeContentDeployment.Run(state: "completed", copiedPayloadCount: 22_500, ended: true)),
            retained: FakeContentDeployment.ReleaseReport(
                retainedPayloadCount: 22_500,
                retainedByteCount: 1_048_576));

        // Act
        var exitCode = await this.RunAsync(deployment, "content", "move-status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("22,500 payloads carrying 1,048,576 bytes", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("content release", StringComparison.Ordinal));
    }

    public void Dispose() => this.harness.Dispose();

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
