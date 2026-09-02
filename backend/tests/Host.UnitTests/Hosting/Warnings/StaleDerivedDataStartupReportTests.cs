// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Host.Configuration.SensitiveContent;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers the one place an operator learns that switching a scanner on reached nothing already stored.</summary>
public sealed class StaleDerivedDataStartupReportTests
{
    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));
    private readonly IStoredEmailExtractionBackfillStore store = Substitute.For<IStoredEmailExtractionBackfillStore>();
    private readonly RecordingLogger<StaleDerivedDataStartupReport> logger = new();

    /// <summary>The gap this whole feature exists to close: the switch is on and the mailbox predates it.</summary>
    [Fact]
    public async Task StartAsync_DerivedTextWrittenUnderAnOlderConfiguration_NamesTheCountAndTheKeyThatFixesIt()
    {
        // Arrange
        this.StoreCounts(1_284);
        using var report = this.Report(rebuildStaleDerivedData: false);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var message = Assert.Single(this.logger.Messages);

        Assert.Contains("1284", message, StringComparison.Ordinal);
        Assert.Contains("SensitiveContent:RebuildStaleDerivedData", message, StringComparison.Ordinal);
    }

    /// <summary>An operator who has already asked for the rebuild is told it is under way rather than warned again.</summary>
    [Fact]
    public async Task StartAsync_ARebuildAlreadyRequested_SaysTheWalkWillReDeriveThem()
    {
        // Arrange
        this.StoreCounts(7);
        using var report = this.Report(rebuildStaleDerivedData: true);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var message = Assert.Single(this.logger.Messages);

        Assert.Contains("MailExtractionBackfill:Enabled", message, StringComparison.Ordinal);
    }

    /// <summary>A deployment whose whole mailbox is current has to be told so, or silence reads as an unread figure.</summary>
    [Fact]
    public async Task StartAsync_NothingWrittenUnderAnOlderConfiguration_SaysTheMailboxIsCurrent()
    {
        // Arrange
        this.StoreCounts(0);
        using var report = this.Report(rebuildStaleDerivedData: false);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var message = Assert.Single(this.logger.Messages);

        Assert.DoesNotContain("RebuildStaleDerivedData", message, StringComparison.Ordinal);
    }

    /// <summary>The report decides nothing, so a figure it cannot read must not keep the deployment from starting.</summary>
    [Fact]
    public async Task StartAsync_ACountThatCouldNotBeRead_SaysSoAndLetsTheHostStart()
    {
        // Arrange
        this.store
            .CountEmailsWithStaleDerivedDataAsync(Arg.Any<CancellationToken>())
            .Returns<Task<int>>(_ => throw new InvalidOperationException("The database is not answering."));
        using var report = this.Report(rebuildStaleDerivedData: false);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var message = Assert.Single(this.logger.Messages);

        Assert.Contains("unavailable", message, StringComparison.Ordinal);
    }

    private void StoreCounts(int staleEmailCount) =>
        this.store
            .CountEmailsWithStaleDerivedDataAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(staleEmailCount));

    /// <summary>Builds a deployment that scans somebody's mail, which is the only state this report says anything in.</summary>
    /// <remarks>
    /// The redaction behind the posture is never exercised, because the report reads whether anything is scanned at all
    /// and nothing else — so the detector list is empty rather than one this test would then have to keep meaningful.
    /// </remarks>
    private ScannedDeployment Report(bool rebuildStaleDerivedData)
    {
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    SensitiveContentScannerKind.Secrets,
                    [SensitiveContentCategory.Create("ProviderToken")],
                    []),
            ]);
        var concurrency = new SensitiveContentScanConcurrency(plan.Bounds.MaximumConcurrentScans);
        var postures = FixedSensitiveContentPostures.ForEveryOwner(
            SensitiveContentPosture.Scanning(
                [SensitiveContentScannerKind.Secrets],
                new SensitiveContentRedactor(plan, [], this.timeProvider, concurrency),
                SensitiveContentScreeningPolicy.ScreeningNothing(),
                SensitiveContentDerivationStamp.Create(
                    new string('a', SensitiveContentDerivationStamp.Length))));
        var settings = new SensitiveContentOptions { RebuildStaleDerivedData = rebuildStaleDerivedData };
        var services = new ServiceCollection()
            .AddScoped(_ => this.store)
            .BuildServiceProvider();

        return new ScannedDeployment(
            concurrency,
            services,
            new StaleDerivedDataStartupReport(
                services.GetRequiredService<IServiceScopeFactory>(),
                new SensitiveContentDerivationGuard(
                    postures,
                    Substitute.For<ISensitiveContentDerivationTelemetry>(),
                    this.timeProvider),
                Options.Create(settings),
                this.logger));
    }

    /// <summary>Holds what one report is exercised against, so the scan permits and the scope factory are released.</summary>
    private sealed class ScannedDeployment(
        SensitiveContentScanConcurrency concurrency,
        ServiceProvider services,
        StaleDerivedDataStartupReport report) : IDisposable
    {
        public void Dispose()
        {
            concurrency.Dispose();
            services.Dispose();
        }

        public Task StartAsync(CancellationToken cancellationToken) => report.StartAsync(cancellationToken);
    }
}
