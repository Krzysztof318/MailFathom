// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Credentials;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what a version pair permits, and what a command does with the answer.</summary>
/// <remarks>
/// The decision is asserted against literal pairs, because it is a rule about release lines rather than about this
/// build. What a command does with it is asserted against the version this assembly was actually stamped with, since
/// that is the only version a deployment can report for the two to agree and a literal would fail the release that
/// moves the declared prefix.
/// </remarks>
public sealed class DeploymentVersionAgreementTests : IDisposable
{
    private const string Endpoint = "https://mail.example.test:8443";

    private static readonly Uri EndpointAddress = new(Endpoint);

    private readonly string storeDirectory =
        Path.Combine(Path.GetTempPath(), $"mailfathom-version-tests-{Guid.NewGuid():N}");

    private readonly RecordingCliConsole console = new();

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The same version on both sides is the ordinary case, and it says nothing at all.</summary>
    [Fact]
    public void Settle_TheSameVersionOnBothSides_PermitsCommandsAndSaysNothing()
    {
        // Act
        var agreement = DeploymentVersionAgreement.Settle("0.5.0", "0.5.0");

        // Assert
        Assert.True(agreement.PermitsCommands);
        Assert.Null(agreement.Concern);
    }

    /// <summary>A patch and a prerelease identifier are both within the line, which ADR 0004 makes a compatible pair.</summary>
    [Theory]
    [InlineData("0.5.0", "0.5.1")]
    [InlineData("0.5.3", "0.5.0")]
    [InlineData("0.5.0", "0.5.0-nightly.41")]
    [InlineData("0.5.0-nightly.41", "0.5.0-nightly.42")]
    [InlineData("0.5.0", "0.5.0+3f1c9ab")]
    [InlineData("1.2.0", "1.2.9")]
    public void Settle_TheSameReleaseLineBuiltDifferently_PermitsCommandsAndWarns(
        string commandVersion,
        string deploymentVersion)
    {
        // Act
        var agreement = DeploymentVersionAgreement.Settle(commandVersion, deploymentVersion);

        // Assert
        Assert.True(agreement.PermitsCommands);
        Assert.Contains("not the same build and problems may occur", agreement.Concern, StringComparison.Ordinal);
        Assert.Contains(commandVersion, agreement.Concern, StringComparison.Ordinal);
        Assert.Contains(deploymentVersion, agreement.Concern, StringComparison.Ordinal);
    }

    /// <summary>A minor may break any surface below <c>1.0.0</c>, so another line is refused rather than attempted.</summary>
    [Theory]
    [InlineData("0.5.0", "0.4.0")]
    [InlineData("0.5.0", "0.6.0")]
    [InlineData("0.5.0", "1.5.0")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.5.0", "0.6.0-nightly.3")]
    public void Settle_AnotherReleaseLine_RefusesAndNamesBothVersions(
        string commandVersion,
        string deploymentVersion)
    {
        // Act
        var agreement = DeploymentVersionAgreement.Settle(commandVersion, deploymentVersion);

        // Assert
        Assert.False(agreement.PermitsCommands);
        Assert.Contains("another release line", agreement.Concern, StringComparison.Ordinal);
        Assert.Contains(commandVersion, agreement.Concern, StringComparison.Ordinal);
        Assert.Contains(deploymentVersion, agreement.Concern, StringComparison.Ordinal);
    }

    /// <summary>A version nothing can read is an absence of evidence, so it warns and names the side it could not read.</summary>
    [Theory]
    [InlineData("0.5.0", "unknown", "The deployment's version is not one")]
    [InlineData("0.5.0", null, "The deployment's version is not one")]
    [InlineData("0.5.0", "", "The deployment's version is not one")]
    [InlineData("0.5.0", "0.5.0.1.2", "The deployment's version is not one")]
    [InlineData("unknown", "0.5.0", "mfctl's own version is not one")]
    [InlineData("unknown", "unknown", "Neither version is one")]
    public void Settle_AVersionNeitherSideCanRead_PermitsCommandsAndSaysWhichOne(
        string commandVersion,
        string? deploymentVersion,
        string expectedSubject)
    {
        // Act
        var agreement = DeploymentVersionAgreement.Settle(commandVersion, deploymentVersion);

        // Assert
        Assert.True(agreement.PermitsCommands);
        Assert.StartsWith(expectedSubject, agreement.Concern, StringComparison.Ordinal);
        Assert.Contains("is unchecked", agreement.Concern, StringComparison.Ordinal);
    }

    /// <summary>
    /// A version carrying only the two components that name the line is read as that line rather than as unreadable.
    /// Nothing here publishes one — Helm alone would refuse it — but the line is the whole of what the comparison acts
    /// on, and refusing a value that states it exactly would be refusing on a formality.
    /// </summary>
    [Fact]
    public void Settle_ADeploymentReportingTheLineAlone_ReadsItAsThatLine()
    {
        // Act
        var agreement = DeploymentVersionAgreement.Settle("0.5.0", "0.5");

        // Assert
        Assert.True(agreement.PermitsCommands);
        Assert.Contains("not the same build", agreement.Concern, StringComparison.Ordinal);
    }

    /// <summary>Whitespace around a stamped version is not a version difference, and reading it as one would warn on every command.</summary>
    [Fact]
    public void Settle_AVersionSurroundedByWhitespace_ReadsAsTheSameVersion()
    {
        // Act
        var agreement = DeploymentVersionAgreement.Settle("0.5.0", "  0.5.0  ");

        // Assert
        Assert.True(agreement.PermitsCommands);
        Assert.Null(agreement.Concern);
    }

    /// <summary>
    /// The refusal has to land before the request it is protecting, and this is the command that decides it: an
    /// activation is what starts a provider bill, so a deployment this build cannot be sure it speaks to must never be
    /// asked to perform one.
    /// </summary>
    [Fact]
    public async Task Activate_ADeploymentFromAnotherReleaseLine_RefusesWithoutAskingItForAnything()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment,
            activation: (HttpStatusCode.OK, ActivationAnswer),
            version: FakeAdminEndpoint.AnotherReleaseLine);

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint, "--yes");

        // Assert
        Assert.Equal(CliExitCode.Failure, exitCode);
        Assert.False(deployment.WasAskedToActivate());
        Assert.DoesNotContain(
            deployment.RecordedRequests,
            recorded => recorded.RequestUri?.AbsolutePath == AdminEndpointRoutes.EmbeddingActivationPath);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("another release line", StringComparison.Ordinal));

        // The other half of the same distinction: this one refuses the command, so it is a failure and not a caution.
        Assert.Contains(
            this.console.Failures,
            line => line.Contains("another release line", StringComparison.Ordinal));
        Assert.Empty(this.console.Warnings);
    }

    /// <summary>A build difference is a sentence and not a refusal, so the command it was reported on still runs.</summary>
    [Fact]
    public async Task Status_ADeploymentBuiltDifferentlyOnTheSameLine_WarnsAndReportsAnyway()
    {
        // Arrange
        using var deployment = FakeAdminEndpoint.Accepting("workstation", FakeAdminEndpoint.AnotherBuildOfThisLine);

        // Act
        var exitCode = await this.RunAsync(deployment, "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(
            this.console.Errors,
            line => line.Contains("not the same build and problems may occur", StringComparison.Ordinal));
        Assert.Contains(this.console.Lines, line => line.Contains("accepts the stored credential", StringComparison.Ordinal));

        // A caution, because the command ran and answered. Reporting a build difference in the colour a failure carries
        // would say the reading below it is untrustworthy, which is exactly what this concern does not claim.
        Assert.Contains(
            this.console.Warnings,
            line => line.Contains("not the same build and problems may occur", StringComparison.Ordinal));
        Assert.Empty(this.console.Failures);
    }

    /// <summary>
    /// One warning per command rather than one per request. The activation sends two, and an operator told the same
    /// thing twice about one invocation learns to read past it.
    /// </summary>
    [Fact]
    public async Task Activate_ADeploymentBuiltDifferently_WarnsOnceForTheWholeCommand()
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(
            assessment: SpendingAssessment,
            activation: (HttpStatusCode.OK, ActivationAnswer),
            version: FakeAdminEndpoint.AnotherBuildOfThisLine);

        // Act
        var exitCode = await this.RunAsync(deployment, "embedding", "activate", "--endpoint", Endpoint, "--yes");

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.True(deployment.WasAskedToActivate());
        Assert.Single(
            this.console.Errors,
            line => line.Contains("not the same build and problems may occur", StringComparison.Ordinal));
    }

    /// <summary>The check costs one session request per command, whether or not the command reads a session of its own.</summary>
    [Theory]
    [InlineData("status")]
    [InlineData("embedding")]
    public async Task ACommandAgainstAnAgreeingDeployment_ReadsTheSessionOnceAndSaysNothingAboutVersions(
        string command)
    {
        // Arrange
        using var deployment = FakeEmbeddingDeployment.Answering(status: EmbeddingStatusAnswer);

        // Act
        var exitCode = command == "status"
            ? await this.RunAsync(deployment, "status", "--endpoint", Endpoint)
            : await this.RunAsync(deployment, "embedding", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Single(
            deployment.RecordedRequests,
            recorded => recorded.RequestUri?.AbsolutePath == AdminEndpointRoutes.SessionPath);
        Assert.DoesNotContain(this.console.Errors, line => line.Contains("problems may occur", StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(this.storeDirectory))
        {
            Directory.Delete(this.storeDirectory, recursive: true);
        }
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args)
    {
        var store = new CredentialStore(
            Path.Combine(this.storeDirectory, "credentials.json"),
            new TokenProtector(Path.Combine(this.storeDirectory, "credentials.key")));

        store.Save("production", EndpointAddress, "not-a-real-key", "workstation");

        var context = new CliContext(
            this.console,
            store,
            (endpoint, trust) => FakeDeploymentTransport.Over(deployment, endpoint, trust),
            FakeMailboxRedirect.Silent(),
            _ => false,
            this.clock);

        return CliRunner.RunAsync(context, args);
    }

    private const string SpendingAssessment = """
        {
          "declared": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
          "forecast": "AlreadyServing",
          "estimate": null,
          "spend": null,
          "exceedsSpendCeiling": false
        }
        """;

    private const string ActivationAnswer = """
        {
          "outcome": "AlreadyServing",
          "profileId": "0199c3d0-0000-7000-8000-000000000001",
          "estimate": null
        }
        """;

    private const string EmbeddingStatusAnswer = """
        {
          "declared": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
          "activationOutstanding": false,
          "serving": {
            "profileId": "0199c3d0-0000-7000-8000-000000000001",
            "geometry": {"fingerprint":"a1b2c3","provider":"a-provider","model":"a-model","modelVersion":null,"dimension":1536,"distanceMetric":"Cosine"},
            "progress": {"searchableEmailCount":10,"embeddedEmailCount":10,"outstandingEmailCount":0,"outstandingPassageCount":0,"outstandingCharacterCount":0,"approximateTokenCount":0}
          },
          "building": null,
          "provider": {"state":"Serving","observedAt":"2026-08-09T11:59:00+00:00"},
          "spend": null
        }
        """;
}
