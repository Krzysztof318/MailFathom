// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Transport;
using MailMcp.Host.Configuration;
using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailMcp.Host.UnitTests;

public sealed class ValidatedSettingsSnapshotTests
{
    [Fact]
    public async Task Current_BeforeAnyReload_IsTheSnapshotBoundAtStartup()
    {
        // Arrange
        var startupSettings = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        await using var harness = CreateHarness(startupSettings);

        // Act
        var current = harness.Settings.Current;

        // Assert
        Assert.Same(startupSettings, current);
    }

    [Fact]
    public async Task PublishWhenUsableAsync_ResolvableCandidate_PublishesItForNewOperations()
    {
        // Arrange
        await using var harness = CreateHarness(ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")));
        var rotatedReference = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:rotated-password"));

        // Act
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(1, rotatedReference),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(rotatedReference, harness.Settings.Current);
    }

    [Fact]
    public async Task PublishWhenUsableAsync_CandidateWithAnUnresolvableReference_KeepsThePreviousSnapshotActive()
    {
        // Arrange
        var startupSettings = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        await using var harness = CreateHarness(startupSettings);
        var brokenCandidate = ConfiguredAccounts.WithPasswordReferences(("primary", "file:/run/secrets/absent"));

        // Act
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(1, brokenCandidate),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(startupSettings, harness.Settings.Current);
    }

    [Fact]
    public async Task PublishWhenUsableAsync_RejectedCandidate_LogsThePathAndFailureAndNoMaterial()
    {
        // Arrange
        await using var harness = CreateHarness(ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")));
        var brokenCandidate = ConfiguredAccounts.WithPasswordReferences(("primary", "file:/run/secrets/imap-primary-password"));

        // Act
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(1, brokenCandidate),
            TestContext.Current.CancellationToken);

        // Assert
        var rejection = Assert.Single(harness.SettingsLogger.Messages);
        Assert.Contains("MailSynchronization:Accounts:0:Secrets:Password", rejection, StringComparison.Ordinal);
        Assert.Contains(nameof(SecretResolutionFailure.MaterialNotFound), rejection, StringComparison.Ordinal);
        Assert.DoesNotContain("/run/secrets", rejection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishWhenUsableAsync_TrustAnchorThatNoLongerLoads_KeepsThePreviousSnapshotActive()
    {
        // Arrange
        var startupSettings = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        await using var harness = CreateHarness(startupSettings);
        var brokenCandidate = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        brokenCandidate.Accounts[0].TransportSecurity.CertificateTrust = MailServerCertificateTrust.AdditionalTrustedAuthority;
        brokenCandidate.Accounts[0].TransportSecurity.TrustedCertificateAuthority =
            new ConfiguredSecret { SecretReference = "plaintext:not-a-certificate" };

        // Act
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(1, brokenCandidate),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(startupSettings, harness.Settings.Current);
    }

    /// <summary>A slow validation of an older snapshot must not overwrite a newer one that already published.</summary>
    [Fact]
    public async Task PublishWhenUsableAsync_OlderCandidateAfterANewerOne_LeavesTheNewerOnePublished()
    {
        // Arrange
        await using var harness = CreateHarness(ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")));
        var newerCandidate = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:newer-password"));
        var olderCandidate = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:older-password"));

        // Act
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(2, newerCandidate),
            TestContext.Current.CancellationToken);
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(1, olderCandidate),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(newerCandidate, harness.Settings.Current);
    }

    /// <summary>A candidate superseded while it was being validated must not reach operations even briefly.</summary>
    [Fact]
    public async Task PublishWhenUsableAsync_CandidateSupersededWhileValidating_IsNotPublished()
    {
        // Arrange
        var startupSettings = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        await using var harness = CreateHarness(startupSettings);
        await harness.Settings.StartingAsync(TestContext.Current.CancellationToken);
        var supersededCandidate = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:superseded-password"));

        // Act
        // Two reports move the observed count past the candidate below, whether or not the reader has reached either
        // of them yet, which is what makes this independent of the background loop's timing.
        harness.OptionsMonitor.ReportReload(ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:newer-password")));
        harness.OptionsMonitor.ReportReload(ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:newest-password")));
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(1, supersededCandidate),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotSame(supersededCandidate, harness.Settings.Current);
    }

    /// <summary>Hosted services are constructed before the startup gate runs, so a reload can land before any listener exists.</summary>
    [Fact]
    public async Task StartingAsync_ReloadLandedBeforeSubscribing_AdoptsItInsteadOfHoldingTheCapturedSnapshot()
    {
        // Arrange
        var startupSettings = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        await using var harness = CreateHarness(startupSettings);
        var reloadedDuringTheGap = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:rotated-password"));
        harness.OptionsMonitor.ReportReload(reloadedDuringTheGap);

        // Act
        await harness.Settings.StartingAsync(TestContext.Current.CancellationToken);
        await harness.Settings.StoppedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(reloadedDuringTheGap, harness.Settings.Current);
    }

    /// <summary>A reload that fails unexpectedly must leave a running deployment alone rather than end the process.</summary>
    [Fact]
    public async Task PublishWhenUsableAsync_ValidationThrows_KeepsThePreviousSnapshotAndReportsTheFailure()
    {
        // Arrange
        var startupSettings = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        await using var harness = CreateHarness(startupSettings, new ThrowingSecretReferenceResolver());
        var candidate = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:rotated-password"));

        // Act
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(1, candidate),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(startupSettings, harness.Settings.Current);
        var rejection = Assert.Single(harness.SettingsLogger.Messages);
        Assert.Contains(nameof(IOException), rejection, StringComparison.Ordinal);
        Assert.DoesNotContain("unreachable", rejection, StringComparison.Ordinal);
    }

    /// <summary>Resolution can reach a file or a managed store, so it must never run on the thread reporting the reload.</summary>
    [Fact]
    public async Task OnChange_ReportedReload_ReturnsWithoutValidatingOnTheReportingThread()
    {
        // Arrange
        var startupSettings = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        await using var harness = CreateHarness(startupSettings, new BlockingSecretReferenceResolver());
        await harness.Settings.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        harness.OptionsMonitor.ReportReload(ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:rotated-password")));
        var settingsAfterTheReportReturned = harness.Settings.Current;

        // Assert
        Assert.Same(startupSettings, settingsAfterTheReportReturned);
    }

    [Fact]
    public async Task StoppedAsync_ReloadReportedBeforeShutdown_StillDecidesTheWaitingCandidate()
    {
        // Arrange
        await using var harness = CreateHarness(ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")));
        await harness.Settings.StartingAsync(TestContext.Current.CancellationToken);
        var rotatedReference = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:rotated-password"));

        // Act
        harness.OptionsMonitor.ReportReload(rotatedReference);
        await harness.Settings.StoppedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(rotatedReference, harness.Settings.Current);
    }

    /// <summary>This settings group has no named variants, so a named reload describes a section nothing reads.</summary>
    [Fact]
    public async Task OnChange_NamedOptionsReload_IsIgnored()
    {
        // Arrange
        var startupSettings = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        await using var harness = CreateHarness(startupSettings);
        await harness.Settings.StartingAsync(TestContext.Current.CancellationToken);

        // Act
        harness.OptionsMonitor.ReportReload(
            ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:rotated-password")),
            name: "secondary");
        await harness.Settings.StoppedAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(startupSettings, harness.Settings.Current);
    }

    /// <summary>A work unit reads its transport policy from the snapshot it captured, which the publisher supplies.</summary>
    [Fact]
    public async Task Current_AfterAPublishedReload_SuppliesTheAdoptedTransportSecurityPolicy()
    {
        // Arrange
        await using var harness = CreateHarness(ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password")));
        var candidate = ConfiguredAccounts.WithPasswordReferences(("primary", "plaintext:dev-password"));
        candidate.Accounts[0].TransportSecurity.ConnectionSecurity = MailConnectionSecurity.StartTlsRequired;

        // Act
        await harness.Settings.PublishWhenUsableAsync(
            new ValidatedSettingsSnapshot<MailSynchronizationOptions>.ReloadCandidate(1, candidate),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            MailConnectionSecurity.StartTlsRequired,
            harness.Settings.Current.GetPolicy(MailAccountId.Create("primary")).ConnectionSecurity);
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The harness owns the settings and every test disposes the harness.")]
    private static SettingsHarness CreateHarness(
        MailSynchronizationOptions startupSettings,
        ISecretReferenceResolver? resolver = null)
    {
        var secretReferenceResolver = resolver ?? new PlaintextOnlySecretReferenceResolver();
        var optionsMonitor = new TestOptionsMonitor<MailSynchronizationOptions>(startupSettings);
        var settingsLogger = new RecordingLogger<ValidatedSettingsSnapshot<MailSynchronizationOptions>>();

        var validator = new SecretConfigurationValidator(
            secretReferenceResolver,
            new TrustAnchorLoader(secretReferenceResolver),
            new DatabaseConnectionSettingsMapper(new ConfigurationBuilder().Build()),
            new StubDatabaseConnectionSettingsValidator(),
            PostgresTextSearchConfiguration.Default,
            new RecordingLogger<SecretConfigurationValidator>());

        var settings = new ValidatedSettingsSnapshot<MailSynchronizationOptions>(
            optionsMonitor,
            validator.FindMailConfigurationErrorsAsync,
            "MailSynchronization",
            settingsLogger);

        return new SettingsHarness(settings, optionsMonitor, settingsLogger);
    }

    private sealed record SettingsHarness(
        ValidatedSettingsSnapshot<MailSynchronizationOptions> Settings,
        TestOptionsMonitor<MailSynchronizationOptions> OptionsMonitor,
        RecordingLogger<ValidatedSettingsSnapshot<MailSynchronizationOptions>> SettingsLogger) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => this.Settings.DisposeAsync();
    }

    /// <summary>Stands in for a credential source that has become unreachable in a way no failure identity covers.</summary>
    private sealed class ThrowingSecretReferenceResolver : ISecretReferenceResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken) =>
            throw new IOException("the credential source is unreachable");
    }

    /// <summary>Never completes, so a validation that ran inline would deadlock the reporting thread instead of returning.</summary>
    private sealed class BlockingSecretReferenceResolver : ISecretReferenceResolver
    {
        public Task<SecretResolutionResult> ResolveAsync(string? configuredValue, CancellationToken cancellationToken) =>
            new TaskCompletionSource<SecretResolutionResult>().Task.WaitAsync(cancellationToken);
    }
}
