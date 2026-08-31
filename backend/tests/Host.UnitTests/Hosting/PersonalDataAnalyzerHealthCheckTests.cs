// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Host.Hosting;
using MailFathom.TestSupport;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting;

/// <summary>Covers what a readiness probe learns about the analyzer a fail-closed scanner cannot serve without.</summary>
public sealed class PersonalDataAnalyzerHealthCheckTests
{
    private const string AnalyzerEndpoint = "http://analyzer.example.test:3000/";

    [Fact]
    public async Task CheckHealthAsync_AnAnalyzerThatAnswers_IsHealthy()
    {
        // Arrange
        var probe = Substitute.For<IPersonalDataAnalyzerProbe>();
        var check = new PersonalDataAnalyzerHealthCheck(
            probe,
            ScanningForPersonalData(),
            NullLogger<PersonalDataAnalyzerHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        await probe.Received(1).VerifyAvailableAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The scanner fails closed, so an instance whose analyzer cannot answer refuses every read, derived write, and egress
    /// it guards, whichever of the three ways the analyzer failed. Degraded would keep it in the load balancer answering
    /// nothing.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AnAnalyzerThatDoesNot_IsUnhealthyRatherThanDegraded()
    {
        // Arrange
        Exception[] failures =
        [
            NotReached(),
            PersonalDataAnalyzerUnavailableException.Refused(AnalyzerEndpoint, "503 ServiceUnavailable"),
            PersonalDataAnalyzerUnavailableException.DetectsNothingFor(
                AnalyzerEndpoint,
                SensitiveContentCategory.Create("NationalIdentifier")),
        ];
        var reported = new List<HealthStatus>();

        // Act
        foreach (var failure in failures)
        {
            var check = CheckOverAnAnalyzerFailing(failure, NullLogger<PersonalDataAnalyzerHealthCheck>.Instance);

            reported.Add((await check.CheckHealthAsync(
                new HealthCheckContext(),
                TestContext.Current.CancellationToken)).Status);
        }

        // Assert
        Assert.Equal([HealthStatus.Unhealthy, HealthStatus.Unhealthy, HealthStatus.Unhealthy], reported);
    }

    /// <summary>A probe that failed in a way its port does not declare is still an analyzer that did not answer.</summary>
    [Fact]
    public async Task CheckHealthAsync_AProbeThatFailedUndeclared_IsUnhealthyRatherThanThrowing()
    {
        // Arrange
        var check = CheckOverAnAnalyzerFailing(
            new InvalidOperationException("the probe broke"),
            NullLogger<PersonalDataAnalyzerHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>A scrape the caller abandoned says nothing about the analyzer, so it must not take an instance out of traffic.</summary>
    [Fact]
    public async Task CheckHealthAsync_AScrapeTheCallerCancelled_PropagatesRatherThanReportingUnhealthy()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var check = CheckOverAnAnalyzerFailing(
            new OperationCanceledException(),
            NullLogger<PersonalDataAnalyzerHealthCheck>.Instance);

        // Act, Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => check.CheckHealthAsync(new HealthCheckContext(), cancellation.Token));
    }

    /// <summary>
    /// The probe response is one word by design, so the log is where the reason lives. An instance that came up with no
    /// analyzer says so on its first scrape rather than staying silent because nothing changed, and says it once rather
    /// than on every scrape of the outage.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AnAnalyzerThatDoesNotAnswer_LogsTheFailureAtErrorOnce()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var check = CheckOverAnAnalyzerFailing(
            NotReached(),
            loggerFactory.CreateLogger<PersonalDataAnalyzerHealthCheck>());

        // Act
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(loggerFactory.Records);

        Assert.Equal(LogLevel.Error, record.Level);
        Assert.IsType<PersonalDataAnalyzerUnavailableException>(record.Failure);
        Assert.Contains("SensitiveContent:PersonalDataAnalyzer:Endpoint", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("analyzer.example.test", record.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The first observation of either kind is a transition, so an instance whose analyzer answers from the very first
    /// scrape says so once. Without it a regression that logged only the failure half would leave an operator reading an
    /// outage that had already ended.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AnAnalyzerAnsweringFromTheFirstScrape_LogsItOnceAtInformation()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var check = new PersonalDataAnalyzerHealthCheck(
            Substitute.For<IPersonalDataAnalyzerProbe>(),
            ScanningForPersonalData(),
            loggerFactory.CreateLogger<PersonalDataAnalyzerHealthCheck>());

        // Act
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(loggerFactory.Records);

        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Null(record.Failure);
    }

    /// <summary>
    /// A cancellation the caller did not ask for is the analyzer failing to answer inside its budget, not a scrape
    /// somebody abandoned. It must report unready rather than propagate, which is what the <c>when</c> clause on the
    /// cancellation catch decides.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_AProbeCancelledByItsOwnBudget_IsUnhealthyRatherThanPropagating()
    {
        // Arrange
        var check = CheckOverAnAnalyzerFailing(
            new OperationCanceledException(),
            NullLogger<PersonalDataAnalyzerHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    /// <summary>A record saying the outage began is worth little without the one saying it ended.</summary>
    [Fact]
    public async Task CheckHealthAsync_AnAnalyzerThatCameBack_LogsBothTransitionsAndNothingBetween()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var probe = Substitute.For<IPersonalDataAnalyzerProbe>();
        var answering = false;
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>())
            .Returns(_ => answering ? Task.CompletedTask : Task.FromException(NotReached()));

        var check = new PersonalDataAnalyzerHealthCheck(
            probe,
            ScanningForPersonalData(),
            loggerFactory.CreateLogger<PersonalDataAnalyzerHealthCheck>());

        // Act
        var refused = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        answering = true;
        var recovered = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, refused.Status);
        Assert.Equal(HealthStatus.Healthy, recovered.Status);
        Assert.Equal(
            [LogLevel.Error, LogLevel.Information],
            loggerFactory.Records.Select(record => record.Level));
    }

    /// <summary>
    /// The scanner is switched on per owner, so an analyzer nobody's mail is scanned by guards nothing and its silence
    /// refuses nothing. Reporting unready over it would take an instance out of traffic for a dependency no request on
    /// it uses, which is the state a deployment reaches by standing the analyzer up before anybody has opted in.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_NoOwnerScannedForPersonalData_IsHealthyWithoutAskingTheAnalyzer()
    {
        // Arrange
        var probe = Substitute.For<IPersonalDataAnalyzerProbe>();
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>()).ThrowsAsync(NotReached());
        var check = new PersonalDataAnalyzerHealthCheck(
            probe,
            Substitute.For<ISensitiveContentPostures>(),
            NullLogger<PersonalDataAnalyzerHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        await probe.DidNotReceive().VerifyAvailableAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The other way an outage ends is the last owner scanned for personal data stopping, which flips the probe back to
    /// ready without the analyzer having answered. An operator watching the Error record would otherwise have nothing
    /// saying the instance is back in traffic, and would go on reading a log that ends at the refusal.
    /// </summary>
    [Fact]
    public async Task CheckHealthAsync_TheLastOwnerScannedStoppingDuringAnOutage_LogsTheInstanceIsReadyAgain()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var probe = Substitute.For<IPersonalDataAnalyzerProbe>();
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>()).ThrowsAsync(NotReached());

        var postures = Substitute.For<ISensitiveContentPostures>();
        var scanned = true;
        postures.RunsForAnyOwner(SensitiveContentScannerKind.Pii).Returns(_ => scanned);

        var check = new PersonalDataAnalyzerHealthCheck(
            probe,
            postures,
            loggerFactory.CreateLogger<PersonalDataAnalyzerHealthCheck>());

        // Act
        var refused = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        scanned = false;
        var ready = await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);
        await check.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, refused.Status);
        Assert.Equal(HealthStatus.Healthy, ready.Status);
        Assert.Equal(
            [LogLevel.Error, LogLevel.Information],
            loggerFactory.Records.Select(record => record.Level));
    }

    /// <summary>The three decisions that make this check what it is, asserted where a registration can lose one silently.</summary>
    [Fact]
    public void Registration_Always_IsUnhealthyOnFailureAndReachesTheReadinessProbeAlone()
    {
        // Act
        var registration = PersonalDataAnalyzerHealthCheck.Registration();

        // Assert
        Assert.Equal(PersonalDataAnalyzerHealthCheck.Name, registration.Name);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Equal([HealthProbe.Readiness.Tag], registration.Tags);
        Assert.True(HealthProbe.Readiness.Selects(registration));
        Assert.False(HealthProbe.Liveness.Selects(registration));
        Assert.False(HealthProbe.Startup.Selects(registration));
    }

    [Fact]
    public void Constructor_WithoutItsCollaborators_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new PersonalDataAnalyzerHealthCheck(
            null!,
            ScanningForPersonalData(),
            NullLogger<PersonalDataAnalyzerHealthCheck>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PersonalDataAnalyzerHealthCheck(
            Substitute.For<IPersonalDataAnalyzerProbe>(),
            null!,
            NullLogger<PersonalDataAnalyzerHealthCheck>.Instance));
        Assert.Throws<ArgumentNullException>(() => new PersonalDataAnalyzerHealthCheck(
            Substitute.For<IPersonalDataAnalyzerProbe>(),
            ScanningForPersonalData(),
            null!));
    }

    private static PersonalDataAnalyzerUnavailableException NotReached() =>
        PersonalDataAnalyzerUnavailableException.NotReached(
            AnalyzerEndpoint,
            new HttpRequestException("connection refused"));

    private static PersonalDataAnalyzerHealthCheck CheckOverAnAnalyzerFailing(
        Exception failure,
        ILogger<PersonalDataAnalyzerHealthCheck> logger)
    {
        var probe = Substitute.For<IPersonalDataAnalyzerProbe>();
        probe.VerifyAvailableAsync(Arg.Any<CancellationToken>()).ThrowsAsync(failure);

        return new PersonalDataAnalyzerHealthCheck(probe, ScanningForPersonalData(), logger);
    }

    /// <summary>The postures of a deployment where at least one owner's mail is scanned for personal data.</summary>
    private static ISensitiveContentPostures ScanningForPersonalData()
    {
        var postures = Substitute.For<ISensitiveContentPostures>();
        postures.RunsForAnyOwner(SensitiveContentScannerKind.Pii).Returns(true);

        return postures;
    }
}
