// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers what <c>mailbox status</c> tells an operator whose mail is not arriving.</summary>
/// <remarks>
/// Every test here is about a reading rather than a field. The command exists because a deployment that is failing to
/// fetch mail and one whose mailbox is simply quiet present identically, so what is asserted is that the two produce
/// visibly different output and that each of the answers an operator has to act on names what to do.
/// </remarks>
public sealed class MailboxStatusCommandTests : IDisposable
{
    private const string Endpoint = CliCommandHarness.Endpoint;

    private readonly CliCommandHarness harness = new(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero));

    /// <summary>An account running now, with a folder that is still working through its backfill.</summary>
    [Fact]
    public async Task Status_AnAccountThatIsRunning_ReportsThePhaseAndHowFarEachFolderHasCome()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering("""
            {
              "synchronizationEnabled": true,
              "accounts": [
                {
                  "account": "work",
                  "phase": "Running",
                  "nextRunDueAt": null,
                  "consecutiveFailureCount": 0,
                  "lastRun": {"endedAt":"2026-08-15T11:55:00+00:00","failed":false,"scheduledFolderCount":2,"failedFolderCount":0,"mutationConvergenceFailed":false},
                  "folders": [
                    {
                      "alias": "INBOX",
                      "mirrored": true,
                      "uidValidity": 3,
                      "lastSeenUid": 6997,
                      "progressAdvancedAt": "2026-08-15T11:55:00+00:00",
                      "lastRun": {"outcome":"Synchronized","endedAt":"2026-08-15T11:55:00+00:00","storedEmailCount":100,"skippedOversizedEmailCount":0,"unreadableMimeEmailCount":0,"hasMoreEmails":true}
                    }
                  ]
                }
              ]
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "mailbox", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Equal(AdminEndpointRoutes.MailboxSynchronizationPath, deployment.LastPath());
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("running now", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("UID 6,997", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("more to fetch: True", StringComparison.Ordinal));
    }

    /// <summary>
    /// The reading the command exists for. The account is backing off and the folder's progress has not moved, and both
    /// halves are on the screen: the wait it is under, and the classification that says why the folder is not advancing.
    /// </summary>
    [Fact]
    public async Task Status_AFolderThatKeepsFailing_ReportsTheBackoffAndWhyTheFolderIsNotAdvancing()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering("""
            {
              "synchronizationEnabled": true,
              "accounts": [
                {
                  "account": "work",
                  "phase": "WaitingForNextRun",
                  "nextRunDueAt": "2026-08-15T12:20:00+00:00",
                  "consecutiveFailureCount": 4,
                  "lastRun": {"endedAt":"2026-08-15T11:55:00+00:00","failed":true,"scheduledFolderCount":2,"failedFolderCount":1,"mutationConvergenceFailed":false},
                  "folders": [
                    {
                      "alias": "archive",
                      "mirrored": true,
                      "uidValidity": 3,
                      "lastSeenUid": 6997,
                      "progressAdvancedAt": "2026-08-14T09:00:00+00:00",
                      "lastRun": {"outcome":"UnexpectedFailure","endedAt":"2026-08-15T11:55:00+00:00","storedEmailCount":0,"skippedOversizedEmailCount":0,"unreadableMimeEmailCount":0,"hasMoreEmails":false}
                    }
                  ]
                }
              ]
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "mailbox", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("2026-08-15 12:20:00Z", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("4 runs failed in a row", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("last moved at 2026-08-14 09:00:00Z", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("failed unexpectedly", StringComparison.Ordinal));
    }

    /// <summary>An alias naming no advertised folder is corrected by an edit rather than waited out, so the line says so.</summary>
    [Fact]
    public async Task Status_AnAliasThatNamesNoAdvertisedFolder_NamesTheEditThatFixesIt()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering("""
            {
              "synchronizationEnabled": true,
              "accounts": [
                {
                  "account": "work",
                  "phase": "WaitingForNextRun",
                  "nextRunDueAt": "2026-08-15T12:05:00+00:00",
                  "consecutiveFailureCount": 0,
                  "lastRun": {"endedAt":"2026-08-15T11:55:00+00:00","failed":false,"scheduledFolderCount":1,"failedFolderCount":0,"mutationConvergenceFailed":false},
                  "folders": [
                    {
                      "alias": "sent",
                      "mirrored": true,
                      "uidValidity": null,
                      "lastSeenUid": null,
                      "progressAdvancedAt": null,
                      "lastRun": {"outcome":"AliasUnresolved","endedAt":"2026-08-15T11:55:00+00:00","storedEmailCount":0,"skippedOversizedEmailCount":0,"unreadableMimeEmailCount":0,"hasMoreEmails":false}
                    }
                  ]
                }
              ]
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "mailbox", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("nothing committed yet", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("configure its remote path", StringComparison.Ordinal));
    }

    /// <summary>A folder the operator stopped mirroring is reported as that rather than left out, so it never reads as a folder that vanished.</summary>
    [Fact]
    public async Task Status_AMappedFolderNothingMirrors_SaysNoRunSchedulesIt()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering("""
            {
              "synchronizationEnabled": true,
              "accounts": [
                {
                  "account": "work",
                  "phase": "NotStarted",
                  "nextRunDueAt": null,
                  "consecutiveFailureCount": 0,
                  "lastRun": null,
                  "folders": [
                    {"alias":"archive","mirrored":false,"uidValidity":null,"lastSeenUid":null,"progressAdvancedAt":null,"lastRun":null}
                  ]
                }
              ]
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "mailbox", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("not mirrored", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("none since this deployment started", StringComparison.Ordinal));
    }

    /// <summary>The switch is the first line, because every count below it is still while it is off.</summary>
    [Fact]
    public async Task Status_ADeploymentWithSynchronizationSwitchedOff_SaysSoBeforeAnythingElse()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering("""
            {"synchronizationEnabled": false, "accounts": []}
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "mailbox", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("fetches no mail", StringComparison.Ordinal));
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("none configured", StringComparison.Ordinal));
    }

    /// <summary>A newer deployment's word for something is repeated rather than read as an absence, because inventing a reading for it would be worse than showing it.</summary>
    [Fact]
    public async Task Status_AnOutcomeThisBuildDoesNotKnow_RepeatsWhatTheDeploymentCalledIt()
    {
        // Arrange
        using var deployment = FakeMailboxDeployment.Answering("""
            {
              "synchronizationEnabled": true,
              "accounts": [
                {
                  "account": "work",
                  "phase": "SomethingNewerEntirely",
                  "nextRunDueAt": null,
                  "consecutiveFailureCount": 0,
                  "lastRun": null,
                  "folders": [
                    {
                      "alias": "INBOX",
                      "mirrored": true,
                      "uidValidity": 3,
                      "lastSeenUid": 12,
                      "progressAdvancedAt": "2026-08-15T11:55:00+00:00",
                      "lastRun": {"outcome":"DeferredForAReasonThisBuildHasNoWordFor","endedAt":"2026-08-15T11:55:00+00:00","storedEmailCount":0,"skippedOversizedEmailCount":0,"unreadableMimeEmailCount":0,"hasMoreEmails":false}
                    }
                  ]
                }
              ]
            }
            """);

        // Act
        var exitCode = await this.RunAsync(deployment, "mailbox", "status", "--endpoint", Endpoint);

        // Assert
        Assert.Equal(CliExitCode.Success, exitCode);
        Assert.Contains(this.harness.Console.Lines, line => line.Contains("SomethingNewerEntirely", StringComparison.Ordinal));
        Assert.Contains(
            this.harness.Console.Lines,
            line => line.Contains("DeferredForAReasonThisBuildHasNoWordFor", StringComparison.Ordinal));
    }

    private Task<int> RunAsync(FakeHttpMessageHandler deployment, params string[] args) =>
        this.harness.RunAsync(deployment, args);

    public void Dispose() => this.harness.Dispose();
}
