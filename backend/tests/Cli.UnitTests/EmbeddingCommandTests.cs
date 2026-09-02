// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what the embedding commands decide before, and instead of, starting a provider bill.</summary>
/// <remarks>
/// One of these commands spends money per unit of mail, so most of what is asserted here is the request that was
/// <em>not</em> sent: a declined prompt, a redirected input with nobody to ask, and an estimate the deployment's own
/// ceiling refuses each have to leave the activation unstarted rather than merely unreported.
/// </remarks>
public sealed class EmbeddingCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private const string SpendingAssessment = """
        {
          "declared": {
            "fingerprint": "a1b2c3",
            "provider": "a-provider",
            "model": "a-model",
            "modelVersion": null,
            "dimension": 1536,
            "distanceMetric": "Cosine"
          },
          "forecast": "WouldStartReindex",
          "estimate": {
            "searchableEmailCount": 500,
            "embeddedEmailCount": 0,
            "outstandingEmailCount": 500,
            "outstandingPassageCount": 2000,
            "outstandingCharacterCount": 200000,
            "approximateTokenCount": 50000
          },
          "spend": {
            "periodStartsAt": "2026-08-08T00:00:00+00:00",
            "periodEndsAt": "2026-08-09T00:00:00+00:00",
            "consumedInputCharacterCount": 0,
            "ceilingInputCharacterCount": 1000000,
            "remainingInputCharacterCount": 1000000
          },
          "exceedsSpendCeiling": false
        }
        """;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 8, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The declaration and the two generations are all in the one answer, because any of them can be the reason search is quiet.</summary>
    [Fact]
    public async Task Status_ADeploymentEmbeddingUnderTheDeclaredModel_ReportsTheModelAndHowFarItHasCome()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(status: """
            {
              "declared": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
              "activationOutstanding": false,
              "serving": {
                "profileId": "0199c3d0-0000-7000-8000-000000000001",
                "geometry": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
                "progress": {"searchableEmailCount":500,"embeddedEmailCount":500,"outstandingEmailCount":0,"outstandingPassageCount":0,"outstandingCharacterCount":0,"approximateTokenCount":0}
              },
              "building": null,
              "provider": {"state":"Serving","observedAt":"2026-08-08T11:59:00+00:00"},
              "spend": {"periodStartsAt":"2026-08-08T00:00:00+00:00","periodEndsAt":"2026-08-09T00:00:00+00:00","consumedInputCharacterCount":1200,"ceilingInputCharacterCount":1000000,"remainingInputCharacterCount":998800}
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(AdminEndpointRoutes.EmbeddingStatusPath, deployment.LastPath());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("a-model", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("nothing outstanding", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Serving", StringComparison.Ordinal));
    }

    /// <summary>
    /// The one answer this command exists for. Editing configuration starts nothing, so an operator who expected search
    /// results to change is told which command would have changed them.
    /// </summary>
    [Fact]
    public async Task Status_ADeclarationNobodyActivated_SaysSoAndNamesTheCommandThatTakesItUp()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(status: """
            {
              "declared": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-newer-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
              "activationOutstanding": true,
              "serving": null,
              "building": null,
              "provider": {"state":"Unobserved","observedAt":null},
              "spend": {"periodStartsAt":"2026-08-08T00:00:00+00:00","periodEndsAt":"2026-08-09T00:00:00+00:00","consumedInputCharacterCount":0,"ceilingInputCharacterCount":null,"remainingInputCharacterCount":null}
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("mfctl embedding activate", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("answered lexically", StringComparison.Ordinal));
    }

    /// <summary>
    /// The line an operator reads when a freshly activated deployment looks broken. Nothing serving, nothing embedded,
    /// and a provider nothing has been asked of are the same three readings a failing instance gives, and the scheduled
    /// pass is what separates them.
    /// </summary>
    [Fact]
    public async Task Status_ADeploymentWaitingForItsNextBackfillPass_ReportsWhenThatPassIsDue()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(status: """
            {
              "declared": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
              "activationOutstanding": false,
              "serving": null,
              "building": {
                "profileId": "0199c3d0-0000-7000-8000-000000000002",
                "geometry": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
                "progress": {"searchableEmailCount":12,"embeddedEmailCount":0,"outstandingEmailCount":12,"outstandingPassageCount":40,"outstandingCharacterCount":8000,"approximateTokenCount":2000}
              },
              "provider": {"state":"Unobserved","observedAt":null},
              "spend": {"periodStartsAt":"2026-08-08T00:00:00+00:00","periodEndsAt":"2026-08-09T00:00:00+00:00","consumedInputCharacterCount":0,"ceilingInputCharacterCount":null,"remainingInputCharacterCount":null},
              "nextBackfillPassDueAt": "2026-08-08T12:00:30+00:00"
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.StartsWith("Next pass:", StringComparison.Ordinal)
                && line.Contains("due at 2026-08-08 12:00:30Z", StringComparison.Ordinal));
    }

    /// <summary>A deployment whose walk is turned off schedules nothing, and the line says which setting does that.</summary>
    [Fact]
    public async Task Status_ADeploymentSchedulingNoBackfillPass_NamesBothCausesWithoutAssertingEither()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(status: """
            {
              "declared": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
              "activationOutstanding": false,
              "serving": null,
              "building": null,
              "provider": {"state":"Unobserved","observedAt":null},
              "spend": {"periodStartsAt":"2026-08-08T00:00:00+00:00","periodEndsAt":"2026-08-09T00:00:00+00:00","consumedInputCharacterCount":0,"ceilingInputCharacterCount":null,"remainingInputCharacterCount":null},
              "nextBackfillPassDueAt": null
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        var nextPass = Assert.Single(
            this.harness.Console.Lines,
            line => line.StartsWith("Next pass:", StringComparison.Ordinal));
        Assert.Contains("EmbeddingBackfill:Enabled", nextPass, StringComparison.Ordinal);

        // Both causes, because a deployment that has only just started reports the absence as truthfully as one whose
        // walk is turned off, and naming only the setting sends an operator to a value that is already what they want.
        Assert.Contains("only just started", nextPass, StringComparison.Ordinal);
    }

    /// <summary>The estimate is written before the question is asked, so what is agreed to is a number rather than a word.</summary>
    [Fact]
    public async Task Activate_ATerminalThatAgrees_ReportsTheEstimateFirstAndThenStartsTheReindex()
    {
        // Arrange
        this.harness.Console.AnswerToGive = true;
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment,
            activation: (HttpStatusCode.OK, ActivationAnswer("ReindexStarted")));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.True(deployment.WasAskedToActivate());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("200,000 characters", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("started a reindex", StringComparison.Ordinal));
    }

    /// <summary>
    /// The sentence an operator reads when the reindex was already running says what actually happened to it, which is
    /// that a pass was asked for rather than that anything was built.
    /// </summary>
    [Fact]
    public async Task Activate_TheGeometryAlreadyBeingBuilt_SaysTheReindexWasLeftRunningAndAPassBroughtForward()
    {
        // Arrange
        this.harness.Console.AnswerToGive = true;
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment,
            activation: (HttpStatusCode.OK, ActivationAnswer("AlreadyBuilding")));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains(
                "the reindex was left running and the next backfill pass was brought forward",
                StringComparison.Ordinal));
    }

    /// <summary>Declining leaves the provider bill unstarted, which is the whole of what the prompt is for.</summary>
    [Fact]
    public async Task Activate_ATerminalThatDeclines_ActivatesNothing()
    {
        // Arrange
        this.harness.Console.AnswerToGive = false;
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment,
            activation: (HttpStatusCode.OK, ActivationAnswer("ReindexStarted")));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.False(deployment.WasAskedToActivate());
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("Nothing was activated", StringComparison.Ordinal));
    }

    /// <summary>A scripted run states the agreement in the command, which is what the flag is for.</summary>
    [Fact]
    public async Task Activate_TheFlagSuppliedWithNobodyAtTheTerminal_ActivatesWithoutAsking()
    {
        // Arrange
        this.harness.Console.AnswersQuestions = false;
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment,
            activation: (HttpStatusCode.OK, ActivationAnswer("ReindexStarted")));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint, "--yes");

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.True(deployment.WasAskedToActivate());
        Assert.Empty(this.harness.Console.Questions);
    }

    /// <summary>
    /// A redirected input has nobody to ask, and reading the answer out of whatever was piped in would turn a stray line
    /// into an agreement to a provider bill.
    /// </summary>
    [Fact]
    public async Task Activate_NobodyAtTheTerminalAndNoFlag_RefusesAndActivatesNothing()
    {
        // Arrange
        this.harness.Console.AnswersQuestions = false;
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment,
            activation: (HttpStatusCode.OK, ActivationAnswer("ReindexStarted")));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.False(deployment.WasAskedToActivate());
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("--yes", StringComparison.Ordinal));
    }

    /// <summary>Asking somebody to confirm a spend the deployment has already said it will not permit is a question with no answer that works.</summary>
    [Fact]
    public async Task Activate_AnEstimateTheCeilingRefuses_ReportsBothNumbersAndActivatesNothing()
    {
        // Arrange
        this.harness.Console.AnswerToGive = true;
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment.Replace(
                "\"exceedsSpendCeiling\": false",
                "\"exceedsSpendCeiling\": true",
                StringComparison.Ordinal),
            activation: (HttpStatusCode.OK, ActivationAnswer("ReindexStarted")));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.False(deployment.WasAskedToActivate());
        Assert.Empty(this.harness.Console.Questions);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("200,000 characters", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("1,000,000", StringComparison.Ordinal));
    }

    /// <summary>Re-activating what already serves spends nothing, so it is performed without a question in the way.</summary>
    [Fact]
    public async Task Activate_TheDeclarationAlreadyServing_ReportsItWithoutAsking()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment.Replace(
                "\"forecast\": \"WouldStartReindex\"",
                "\"forecast\": \"AlreadyServing\"",
                StringComparison.Ordinal),
            activation: (HttpStatusCode.OK, ActivationAnswer("AlreadyServing")));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Empty(this.harness.Console.Questions);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Nothing was started", StringComparison.Ordinal));
    }

    /// <summary>
    /// A refusal the deployment stated is repeated rather than replaced, because it holds the reason and the numbers.
    /// No question is asked on the way: an activation whose only answer leads to a refusal is worse than no question.
    /// </summary>
    [Fact]
    public async Task Activate_ADeploymentRefusingBecauseAnotherReindexRuns_RepeatsWhatItSaidWithoutAsking()
    {
        // Arrange
        this.harness.Console.AnswerToGive = true;
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment.Replace(
                "\"forecast\": \"WouldStartReindex\"",
                "\"forecast\": \"DifferentReindexRunning\"",
                StringComparison.Ordinal),
            activation: (
                HttpStatusCode.Conflict,
                """{"detail":"A reindex into a different generation is already running. Cancel it first."}"""));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Empty(this.harness.Console.Questions);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("Cancel it first", StringComparison.Ordinal));
    }

    /// <summary>
    /// The deployment owns the set of forecasts, so a later build can report one this command has never heard of.
    /// Asking a question that was not needed costs a keystroke; skipping one that was costs a mailbox.
    /// </summary>
    [Fact]
    public async Task Activate_AForecastThisVersionDoesNotRecognize_AsksBeforeActivatingAnything()
    {
        // Arrange
        this.harness.Console.AnswerToGive = false;
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment.Replace(
                "\"forecast\": \"WouldStartReindex\"",
                "\"forecast\": \"SomethingALaterBuildReports\"",
                StringComparison.Ordinal),
            activation: (HttpStatusCode.OK, ActivationAnswer("ReindexStarted")));

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Single(this.harness.Console.Questions);
        Assert.False(deployment.WasAskedToActivate());
    }

    /// <summary>A reindex under way is reported beside what is serving, because the two are different questions.</summary>
    [Fact]
    public async Task Status_AReindexRunning_ReportsHowFarItHasComeBesideWhatIsServing()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(status: """
            {
              "declared": {"fingerprint":"d4e5f6","provider":"a-provider","model":"a-newer-model","modelVersion":null,"dimension":3072,"distanceMetric":"Cosine"},
              "activationOutstanding": false,
              "serving": {
                "profileId": "0199c3d0-0000-7000-8000-000000000001",
                "geometry": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
                "progress": {"searchableEmailCount":500,"embeddedEmailCount":500,"outstandingEmailCount":0,"outstandingPassageCount":0,"outstandingCharacterCount":0,"approximateTokenCount":0}
              },
              "building": {
                "profileId": "0199c3d0-0000-7000-8000-000000000002",
                "geometry": {"fingerprint":"d4e5f6","provider":"a-provider","model":"a-newer-model","modelVersion":null,"dimension":3072,"distanceMetric":"Cosine"},
                "progress": {"searchableEmailCount":500,"embeddedEmailCount":180,"outstandingEmailCount":320,"outstandingPassageCount":1280,"outstandingCharacterCount":128000,"approximateTokenCount":32000}
              },
              "provider": {"state":"Serving","observedAt":"2026-08-08T11:59:00+00:00"},
              "spend": {"periodStartsAt":"2026-08-08T00:00:00+00:00","periodEndsAt":"2026-08-09T00:00:00+00:00","consumedInputCharacterCount":72000,"ceilingInputCharacterCount":1000000,"remainingInputCharacterCount":928000}
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.StartsWith("Reindex:", StringComparison.Ordinal)
                && line.Contains("180 of 500 messages embedded", StringComparison.Ordinal)
                && line.Contains("1,280 passages left", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.StartsWith("Serving:", StringComparison.Ordinal)
                && line.Contains("nothing outstanding", StringComparison.Ordinal));
    }

    /// <summary>A credential the deployment no longer accepts is what the stored profile cannot tell you on its own.</summary>
    [Fact]
    public async Task Status_ACredentialTheDeploymentRefuses_SaysSoRatherThanReportingAnEmptyState()
    {
        // Arrange
        using var deployment = FakeAdminEndpoint.Answering(HttpStatusCode.Unauthorized);

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("refused the credential", StringComparison.Ordinal));
    }

    /// <summary>
    /// An address that answers with a success status is not yet a MailFathom deployment — a proxy or an unrelated
    /// service can do that — so a body this command cannot read is reported as reaching the wrong thing.
    /// </summary>
    [Fact]
    public async Task Status_AnAddressAnsweringSomethingElse_ReportsThatRatherThanFailing()
    {
        // Arrange
        using var deployment = FakeAdminEndpoint.AnsweringBody(HttpStatusCode.OK, "<html>not MailFathom</html>");

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains("not with anything MailFathom would send", StringComparison.Ordinal));
    }

    /// <summary>
    /// The administrative endpoint binds a listener of its own and is off unless a deployment enabled it, so a `404`
    /// is a port to check rather than a missing resource.
    /// </summary>
    [Fact]
    public async Task Status_APortServingNoAdministrativeEndpoint_NamesThePathItAsked()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering();

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(
            this.harness.Console.Errors,
            line => line.Contains(AdminEndpointRoutes.EmbeddingStatusPath, StringComparison.Ordinal));
    }

    /// <summary>An unreachable deployment is a sentence about the address rather than a stack trace.</summary>
    [Fact]
    public async Task Status_AnUnreachableDeployment_ReportsTheAddressRatherThanFailing()
    {
        // Arrange
        using var deployment = FakeAdminEndpoint.Unreachable();

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.Contains(this.harness.Console.Errors, line => line.Contains("could not be reached", StringComparison.Ordinal));
    }

    /// <summary>Finding no reindex to stop is reported as what happened rather than as a failure.</summary>
    [Fact]
    public async Task CancelReindex_NoReindexRunning_ReportsItAndSucceeds()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(cancellation: """{"outcome":"NothingBuilding"}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "cancel-reindex", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(AdminEndpointRoutes.EmbeddingReindexCancellationPath, deployment.LastPath());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("No reindex was running", StringComparison.Ordinal));
    }

    /// <summary>Stopping one abandons the generation being built and leaves search answered from what was serving.</summary>
    [Fact]
    public async Task CancelReindex_AReindexRunning_ReportsWhatWasAbandoned()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(cancellation: """{"outcome":"Cancelled"}""");

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "cancel-reindex", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("Stopped the reindex", StringComparison.Ordinal));
    }

    private static string ActivationAnswer(string outcome) => $$"""
        {
          "outcome": "{{outcome}}",
          "profileId": "0199c3d0-0000-7000-8000-000000000001",
          "estimate": {"searchableEmailCount":500,"embeddedEmailCount":0,"outstandingEmailCount":500,"outstandingPassageCount":2000,"outstandingCharacterCount":200000,"approximateTokenCount":50000}
        }
        """;

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
