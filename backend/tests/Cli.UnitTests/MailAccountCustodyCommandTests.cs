// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the two commands an account's custody is read and changed through.</summary>
/// <remarks>
/// One of these ends with a mail server no longer holding a copy of somebody's mailbox, which is what decides what is
/// asserted: that an operator who does not agree leaves the deployment asked for nothing, that a custody the operator
/// mistyped never reaches the deployment at all, and that every sentence a refusal carries is printed rather than
/// collapsed into a status code an operator cannot act on.
/// </remarks>
public sealed class MailAccountCustodyCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private const string RecordIdentity = "0199a7c4-6d21-7a55-9f1e-2c7d3b9a1f04";

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The drain is only ever visible as counts, so what the command prints is the whole of what an operator learns.</summary>
    [Fact]
    public async Task Show_AHeldAccount_ReportsItsCustodyAndWhatTheSourceStillHolds()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "custody", "show", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Requested: HoldMailbox", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Phase:     Held", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("4812", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Awaiting source removal: 7", StringComparison.Ordinal));
    }

    /// <summary>
    /// An unanswered append is the one thing MailFathom refuses to decide, so the record has to be nameable: a count
    /// alone says work is outstanding and nothing an operator could act on.
    /// </summary>
    [Fact]
    public async Task Show_ARestoringAccount_ReportsWhatIsLeftToPutBackAndNamesEveryAppendToSettle()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering(
            state: FakeMailAccountCustodyDeployment.Restoring());

        // Act
        var exitCode = await this.RunAsync(deployment, "account", "custody", "show", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Phase:     Restoring", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Awaiting append:         318", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Unanswered appends:      1", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("0199a7c4-6d21-7a55-9f1e-2c7d3b9a1f04", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("archive", StringComparison.Ordinal));
    }

    /// <summary>A mirrored account has nothing to put back, so nothing about a restore is printed at it.</summary>
    [Fact]
    public async Task Show_AnAccountWithNothingToPutBack_PrintsNothingAboutARestore()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();

        // Act
        await this.RunAsync(deployment, "account", "custody", "show", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.DoesNotContain(this.harness.Console.Lines, line => line.Contains("Awaiting append:", StringComparison.Ordinal));
        Assert.DoesNotContain(this.harness.Console.Lines, line => line.Contains("Unanswered appends:", StringComparison.Ordinal));
    }

    /// <summary>
    /// The two verdicts do opposite things to somebody's mailbox — one leaves a copy where it is and the other puts a
    /// second message into the folder if the first was wrong — so a command that guessed either would be the guess the
    /// record exists to refuse.
    /// </summary>
    [Theory]
    [InlineData("--found", "--missing")]
    [InlineData(null, null)]
    public async Task Settle_TheOperatorNamedBothVerdictsOrNeither_AsksTheDeploymentForNothing(
        string? first,
        string? second)
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();
        string[] verdict = first is null ? [] : [first, second!];

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            ["account", "custody", "settle", "--account", "work", "--record", RecordIdentity, .. verdict, "--endpoint", Endpoint]);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            0,
            deployment.RequestCount(HttpMethod.Post, AdminEndpointRoutes.MailAccountRestoreSettlementPath));
    }

    /// <summary>
    /// The verdict is asserted in the request rather than in the sentence, because the sentence and the exit code both
    /// come from the command's own reading of what it was given: a client that inverted the verdict or dropped the
    /// record would print the right words about the wrong act.
    /// </summary>
    [Theory]
    [InlineData("--found", "holds that copy", "true")]
    [InlineData("--missing", "does not hold that copy", "false")]
    public async Task Settle_TheOperatorSaidWhatTheFolderHolds_RecordsThatVerdictAndSaysWhatFollowsFromIt(
        string verdict,
        string expected,
        string sent)
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account", "custody", "settle", "--account", "work", "--record", RecordIdentity, verdict, "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(
            1,
            deployment.RequestCount(HttpMethod.Post, AdminEndpointRoutes.MailAccountRestoreSettlementPath));

        var posted = Assert.Single(
            deployment.RecordedRequests,
            request => request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailAccountRestoreSettlementPath);
        var body = posted.ContentAsUtf8String();

        Assert.Contains("\"account\": \"work\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"record\": \"{RecordIdentity}\"", body, StringComparison.Ordinal);
        Assert.Contains($"\"sourceHoldsTheCopy\": {sent}", body, StringComparison.Ordinal);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains(expected, StringComparison.Ordinal));
    }

    /// <summary>A record nothing is standing for is a verdict on nothing, and reporting it as recorded would be a lie.</summary>
    [Fact]
    public async Task Settle_NoAppendIsStandingUnderThatRecord_SaysSoRatherThanReportingAVerdict()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering(
            settlement: FakeMailAccountCustodyDeployment.Settled(wasSettled: false));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account", "custody", "settle", "--account", "work", "--record", RecordIdentity, "--found", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("No append of that account is standing", StringComparison.Ordinal));
    }

    /// <summary>Reading which copy of a mailbox is the truth may never reach the route that empties a mail server.</summary>
    [Fact]
    public async Task Show_AnAccount_AsksTheReadRouteAndNeverTheOneThatSwitches()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();

        // Act
        await this.RunAsync(deployment, "account", "custody", "show", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(
            1,
            deployment.RequestCount(HttpMethod.Get, AdminEndpointRoutes.MailAccountCustodyPath));
        Assert.Equal(
            0,
            deployment.RequestCount(HttpMethod.Post, AdminEndpointRoutes.MailAccountCustodySwitchPath));
        Assert.Contains(
            "account=work",
            deployment.QuerySentTo(AdminEndpointRoutes.MailAccountCustodyPath),
            StringComparison.Ordinal);
    }

    /// <summary>An account still moving towards what was asked for is told so, because the counts alone read as a stalled drain.</summary>
    [Fact]
    public async Task Show_AnAccountStillMovingTowardsWhatWasAsked_SaysTheSwitchIsUnderWay()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering(
            FakeMailAccountCustodyDeployment.Held(phase: "Mirrored", isSwitchPending: true));

        // Act
        await this.RunAsync(deployment, "account", "custody", "show", "--account", "work", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("switch is under way", StringComparison.Ordinal));
    }

    /// <summary>The question names what happens to the mail, and an operator who does not agree leaves the source untouched.</summary>
    [Fact]
    public async Task Switch_AnOperatorWhoDeclines_AsksTheDeploymentForNothing()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();
        this.harness.Console.AnswerToGive = false;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account", "custody", "switch", "--account", "work", "--to", "HoldMailbox", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Equal(
            0,
            deployment.RequestCount(HttpMethod.Post, AdminEndpointRoutes.MailAccountCustodySwitchPath));
        Assert.Contains(
            this.harness.Console.Questions,
            question => question.Contains("nothing puts those copies back", StringComparison.Ordinal));
    }

    /// <summary>An agreed switch sends the custody the operator named, whatever they cased it as.</summary>
    [Fact]
    public async Task Switch_AnAgreedSwitch_SendsTheDeclaredCustodyHoweverItWasCased()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();
        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account", "custody", "switch", "--account", "work", "--to", "holdmailbox", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);

        var sent = Assert.Single(
            deployment.RecordedRequests,
            request => request.RequestUri?.AbsolutePath == AdminEndpointRoutes.MailAccountCustodySwitchPath);

        Assert.Contains("\"custody\": \"HoldMailbox\"", sent.ContentAsUtf8String(), StringComparison.Ordinal);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("empty the source", StringComparison.Ordinal));
    }

    /// <summary>Switching back is a phase rather than an undo, so the sentence may not promise the source gets its mail back.</summary>
    [Fact]
    public async Task Switch_BackToMirroringTheSource_SaysTheDrainStopsRatherThanThatTheMailReturns()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering(
            outcome: FakeMailAccountCustodyDeployment.Accepted("MirrorSource", "Held"));

        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account", "custody", "switch", "--account", "work", "--to", "MirrorSource", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("Restoring", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Questions,
            question => question.Contains(
                "nothing the drain already removed returns to the source server",
                StringComparison.Ordinal));
    }

    /// <summary>A custody the operator mistyped is refused here rather than sent, so nothing is asked of the deployment on a typo.</summary>
    [Fact]
    public async Task Switch_ACustodyThatNamesNeitherValue_IsRefusedWithoutReachingTheDeployment()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();
        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account", "custody", "switch", "--account", "work", "--to", "HoldMailboxes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(deployment.RecordedRequests);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("MirrorSource", StringComparison.Ordinal));
    }

    /// <summary>Every reason a deployment refuses is something an operator has to act on, so each one is printed.</summary>
    [Fact]
    public async Task Switch_ADeploymentThatRefuses_PrintsEveryReasonItGave()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering(
            outcome: FakeMailAccountCustodyDeployment.Refused(
                "Replica 'replica-2' holds a live work lease under a build that does not know this mode.",
                "The account synchronizes a folder playing a virtual role."));

        this.harness.Console.AnswerToGive = true;

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account", "custody", "switch", "--account", "work", "--to", "HoldMailbox", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("replica-2", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("virtual role", StringComparison.Ordinal));
    }

    /// <summary>The confirmation is the operator's, so a deployment asked without one was never agreed to.</summary>
    [Fact]
    public async Task Switch_ConfirmedUpFront_AsksNothingAndSwitchesAnyway()
    {
        // Arrange
        using var deployment = FakeMailAccountCustodyDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "account", "custody", "switch", "--account", "work", "--to", "HoldMailbox", "--yes", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.harness.Console.Questions);
        Assert.Equal(
            1,
            deployment.RequestCount(HttpMethod.Post, AdminEndpointRoutes.MailAccountCustodySwitchPath));
    }

    /// <inheritdoc />
    public void Dispose() => this.harness.Dispose();

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);
}
