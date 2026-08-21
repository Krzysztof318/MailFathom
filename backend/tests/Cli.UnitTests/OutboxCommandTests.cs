// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what an operator can find out about the outbox, and what each decision refuses to do.</summary>
/// <remarks>
/// The refusals are the subject. A message that has begun transmitting cannot be withdrawn and a permanently refused
/// one is not offered again on anybody's behalf, so each of those has to reach the operator as a sentence saying what
/// happened rather than as a status they would have to look up. Beside them sits the one privacy claim these commands
/// make: a listing prints no address, while the reading of one named message prints the addresses it is offered to.
/// </remarks>
public sealed class OutboxCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private const string Recipient = "anna@example.test";

    private static readonly Guid Message = new("6b1e2f4a-3c5d-4e7f-8a9b-0c1d2e3f4a5b");

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The summary is the first question asked of an outbox, so it prints one figure per stage.</summary>
    [Fact]
    public async Task Status_AnOutboxWithMessagesWaiting_PrintsACountForEachStageAndWhatIsOutstanding()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            FakeOutboxDeployment.Summary(3, "Recorded:2", "TransmissionBegun:1", "Sent:41"));

        // Act
        var exitCode = await this.RunAsync(deployment, "outbox", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var listing = DrawnListing.ReadFrom(this.harness.Console.Lines, "Stage", "Messages");
        Assert.Equal("2", listing.Cell(listing.Rows[0], "Messages"));
        Assert.Equal("TransmissionBegun", listing.Cell(listing.Rows[1], "Stage"));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("3 message(s) are still waiting", StringComparison.Ordinal));
    }

    /// <summary>An empty outbox is the ordinary state of a healthy instance and says so rather than printing zeros alone.</summary>
    [Fact]
    public async Task Status_AnOutboxWithNothingWaiting_SaysNothingIsWaiting()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(FakeOutboxDeployment.Summary(0, "Recorded:0"));

        // Act
        var exitCode = await this.RunAsync(deployment, "outbox", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Nothing is waiting", StringComparison.Ordinal));
    }

    /// <summary>Every value lands under the heading naming it, which is the whole of what the column order is worth.</summary>
    [Fact]
    public async Task List_ARecordedSend_SetsEveryReadingUnderItsOwnHeading()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            listing: FakeOutboxDeployment.Page(FakeOutboxDeployment.Entry(Message)));

        // Act
        await this.RunAsync(deployment, "outbox", "list", "--endpoint", Endpoint);

        // Assert
        var listing = DrawnListing.ReadFrom(
            this.harness.Console.Lines, "Recorded", "Message", "Account", "Stage", "Attempts", "Failed", "Due");
        var row = Assert.Single(listing.Rows);

        Assert.Equal("2026-08-19 09:00:00Z", listing.Cell(row, "Recorded"));
        Assert.Equal(Message.ToString("D"), listing.Cell(row, "Message"));
        Assert.Equal("work", listing.Cell(row, "Account"));
        Assert.Equal("Recorded", listing.Cell(row, "Stage"));
        Assert.Equal("2", listing.Cell(row, "Attempts"));
        Assert.Equal("failure 27001, reply 451", listing.Cell(row, "Failed"));
        Assert.Equal("2026-08-19 09:30:00Z", listing.Cell(row, "Due"));
    }

    /// <summary>The one ending that waits for a person is called out, because a stage name alone does not say so.</summary>
    [Fact]
    public async Task List_ASendWhoseOutcomeNobodyKnows_SaysNothingTransmitsItAgainOnItsOwn()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            listing: FakeOutboxDeployment.Page(FakeOutboxDeployment.Entry(Message, "TransmissionBegun")));

        // Act
        await this.RunAsync(deployment, "outbox", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("its server never answered", StringComparison.Ordinal));
    }

    /// <summary>An empty outbox says so rather than printing nothing, which reads as a command that failed.</summary>
    [Fact]
    public async Task List_AnOutboxHoldingNothing_SucceedsAndSaysNothingIsRecorded()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(listing: FakeOutboxDeployment.Page());

        // Act
        var exitCode = await this.RunAsync(deployment, "outbox", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("has been asked to send nothing", StringComparison.Ordinal));
    }

    /// <summary>The filters reach the deployment as a query string it can read, escaped in one place rather than at the call site.</summary>
    [Fact]
    public async Task List_FiltersNamedByTheOperator_ReachTheDeploymentAsAQueryString()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(listing: FakeOutboxDeployment.Page());

        // Act
        await this.RunAsync(
            deployment,
            "outbox",
            "list",
            "--account",
            "work",
            "--stage",
            "TransmissionBegun",
            "--page-size",
            "10",
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(
            "?account=work&stage=TransmissionBegun&pageSize=10",
            deployment.LastOutboxQuery());
    }

    /// <summary>A page of an outbox is a page of who this owner writes to, so no address reaches the listing at all.</summary>
    [Fact]
    public async Task List_ASendAddressedToSomebody_PrintsNoAddress()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            listing: FakeOutboxDeployment.Page(FakeOutboxDeployment.Entry(Message)));

        // Act
        await this.RunAsync(deployment, "outbox", "list", "--endpoint", Endpoint);

        // Assert
        Assert.DoesNotContain(this.harness.Console.Lines, line => line.Contains(Recipient, StringComparison.Ordinal));
    }

    /// <summary>A decision about a send nobody knows the outcome of needs the addresses, so the one reading that was asked by identity prints them.</summary>
    [Fact]
    public async Task Show_ASendAskedForByIdentity_PrintsItsRecipientsAndWhatEachWasTold()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            send: FakeOutboxDeployment.Send(Message, recipient: Recipient));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "outbox",
            "show",
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains(Recipient, StringComparison.Ordinal)
                && line.Contains("Pending", StringComparison.Ordinal));
    }

    /// <summary>The risk of a second copy is the whole of what this decision costs, so the reading states it before the operator takes it.</summary>
    [Fact]
    public async Task Show_ASendWhoseOutcomeNobodyKnows_WarnsThatOfferingItAgainMayDeliverASecondCopy()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(send: FakeOutboxDeployment.Send(Message));

        // Act
        await this.RunAsync(
            deployment,
            "outbox",
            "show",
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("second copy", StringComparison.Ordinal));
    }

    /// <summary>
    /// This is the one route whose <c>404</c> is about the record rather than the port, and an operator acting on a
    /// listing a few minutes old reaches it ordinarily. Telling them to check the endpoint's port would send them after
    /// a deployment that answered them correctly.
    /// </summary>
    [Fact]
    public async Task Show_ASendThisDeploymentDoesNotHold_SaysSoRatherThanBlamingTheEndpoint()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving();

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "outbox",
            "show",
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("holds no queued message", StringComparison.Ordinal));
        Assert.DoesNotContain(
            this.harness.Console.Errors,
            line => line.Contains("Check the port", StringComparison.Ordinal));
    }

    /// <summary>The decision reaches the deployment, and the command says what it means for the message.</summary>
    [Fact]
    public async Task Cancel_ASendThatHasNotLeft_AsksTheDeploymentAndReportsThatNobodyReceivedIt()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            cancellation: FakeOutboxDeployment.Decision(Message, "Accepted"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "outbox",
            "cancel",
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.OutboxDecisionRequestCount(AdminEndpointRoutes.OutboxCancellationPath));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("no recipient of it received anything", StringComparison.Ordinal));
    }

    /// <summary>A message that has begun transmitting cannot be recalled, and saying so is the point of the refusal.</summary>
    [Fact]
    public async Task Cancel_ASendThatHasBegunTransmitting_ReportsItRatherThanClaimingSuccess()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            cancellation: FakeOutboxDeployment.Decision(Message, "StageDoesNotAllowIt"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "outbox",
            "cancel",
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("moved past the point this decision applies at", StringComparison.Ordinal));
    }

    /// <summary>A delivery attempt holding the message is a race the operator waits out rather than one they answer.</summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("requeue")]
    public async Task Decision_ASendADeliveryAttemptIsHolding_SaysTheLeaseIsWhatFreesIt(string decision)
    {
        // Arrange
        var answer = FakeOutboxDeployment.Decision(Message, "AttemptUnderWay");
        using var deployment = FakeOutboxDeployment.Serving(cancellation: answer, requeue: answer);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "outbox",
            decision,
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("Its lease is what frees it", StringComparison.Ordinal));
    }

    /// <summary>An identifier matching no send of this deployment is the mistake an operator makes with two deployments open.</summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("requeue")]
    public async Task Decision_ASendTheDeploymentDoesNotHold_ReportsItRatherThanClaimingSuccess(string decision)
    {
        // Arrange
        var answer = FakeOutboxDeployment.Decision(Message, "RecordUnknown");
        using var deployment = FakeOutboxDeployment.Serving(cancellation: answer, requeue: answer);

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "outbox",
            decision,
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("holds no queued message", StringComparison.Ordinal));
    }

    /// <summary>The decision reaches the deployment, and the command says who the message will and will not be offered to.</summary>
    [Fact]
    public async Task Requeue_ASendWhoseOutcomeNobodyKnows_AsksTheDeploymentAndReportsWhoItIsOfferedTo()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            requeue: FakeOutboxDeployment.Decision(Message, "Accepted"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "outbox",
            "requeue",
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(1, deployment.OutboxDecisionRequestCount(AdminEndpointRoutes.OutboxRequeuePath));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("addresses still outstanding", StringComparison.Ordinal));
    }

    /// <summary>Offering a permanently refused message again is a decision to disbelieve the record, so the refusal names the word to add.</summary>
    [Fact]
    public async Task Requeue_APermanentlyRefusedSendWithoutTheRestatement_NamesTheOptionThatRestatesIt()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            requeue: FakeOutboxDeployment.Decision(Message, "RefusalNotRestated"));

        // Act
        var exitCode = await this.RunAsync(
            deployment,
            "outbox",
            "requeue",
            "--message",
            Message.ToString("D"),
            "--endpoint",
            Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("--despite-refusal", StringComparison.Ordinal));
    }

    /// <summary>The restatement is what the deployment reads, so the operator's word has to reach it rather than stop at the command.</summary>
    [Fact]
    public async Task Requeue_WithTheRefusalRestated_TellsTheDeploymentTheOperatorMeansIt()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            requeue: FakeOutboxDeployment.Decision(Message, "Accepted"));

        // Act
        await this.RunAsync(
            deployment,
            "outbox",
            "requeue",
            "--message",
            Message.ToString("D"),
            "--despite-refusal",
            "--endpoint",
            Endpoint);

        // Assert
        using var body = JsonDocument.Parse(
            deployment.LastDecisionBody(AdminEndpointRoutes.OutboxRequeuePath) ?? string.Empty);
        Assert.True(body.RootElement.GetProperty("refusalRestated").GetBoolean());
    }

    /// <summary>Each decision acts on one specific message somebody is waiting for, so there is no send worth guessing.</summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("requeue")]
    [InlineData("show")]
    public async Task Command_WithNoMessageNamed_RefusesWithoutReachingTheDeployment(string command)
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving();

        // Act
        var exitCode = await this.RunAsync(deployment, "outbox", command, "--endpoint", Endpoint);

        // Assert
        Assert.NotEqual(CliExitCode.Success, exitCode);
        Assert.Equal(0, deployment.OutboxDecisionRequestCount(AdminEndpointRoutes.OutboxCancellationPath));
        Assert.Equal(0, deployment.OutboxDecisionRequestCount(AdminEndpointRoutes.OutboxRequeuePath));
    }

    /// <summary>
    /// A credential the deployment admitted and then refused the operation to is a grant to widen rather than a key to
    /// rotate, so the refusal names the permission and where it is written instead of saying the credential was refused.
    /// </summary>
    [Fact]
    public async Task List_ADeploymentRefusingTheOperationForWantOfAGrant_SaysWhatToGrant()
    {
        // Arrange
        using var deployment = FakeOutboxDeployment.Serving(
            listing: (
                System.Net.HttpStatusCode.Forbidden,
                """{"detail":"The credential is not granted 'mailfathom.admin.read'.","permission":"mailfathom.admin.read"}"""));

        // Act
        var exitCode = await this.RunAsync(deployment, "outbox", "list", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("mailfathom.admin.read", StringComparison.Ordinal)
                && line.Contains("AdminEndpoint:Authentication", StringComparison.Ordinal));
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
