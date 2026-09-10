// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what holding work exclusively publishes, and what it must never publish with it.</summary>
/// <remarks>
/// One telemetry instance serves the whole class, because it is a singleton in the process it belongs to and its gauge
/// is created on the application's one meter — an instance per test would leave a gauge per test observing the meter
/// for the rest of the run. Each test therefore names a scope of its own, since the held set the gauge reads is state
/// that outlives the test that wrote it.
/// </remarks>
public sealed class WorkLeaseTelemetryTests
{
    private const string ClaimsInstrumentName = "mailfathom.work_leases.claims";
    private const string RenewalsInstrumentName = "mailfathom.work_leases.renewals";
    private const string HeldInstrumentName = "mailfathom.work_leases.held";
    private const string ScopeTagName = "mailfathom.work.scope";
    private const string OutcomeTagName = "mailfathom.work.lease.outcome";

    private static readonly WorkLeaseTelemetry LeaseTelemetry = new();

    /// <summary>A granted claim is by construction a change of holder, because a live lease is refused whoever asks.</summary>
    [Fact]
    public void RecordClaim_AClaimThatTookTheScope_CountsItAsGranted()
    {
        // Arrange
        var scope = WorkScope.Create("claim-granted");
        using var measurements = new RecordedMailFathomMeasurements(ClaimsInstrumentName);

        // Act
        LeaseTelemetry.RecordClaim(scope, granted: true);

        // Assert
        var claim = Assert.Single(PublishedFor(measurements, scope, ClaimsInstrumentName));
        Assert.Equal(1, claim.Value);
        Assert.Equal("granted", claim.Tags[OutcomeTagName]);
    }

    /// <summary>
    /// A refused claim is an ordinary answer rather than a failure, and it is counted so that a replica waiting on
    /// work somebody else holds is visible as waiting rather than as idle.
    /// </summary>
    [Fact]
    public void RecordClaim_AClaimAnotherHolderRefused_CountsItAndPublishesNoHold()
    {
        // Arrange
        var scope = WorkScope.Create("claim-refused");
        using var measurements = new RecordedMailFathomMeasurements(ClaimsInstrumentName, HeldInstrumentName);

        // Act
        LeaseTelemetry.RecordClaim(scope, granted: false);

        // Assert
        measurements.ObserveGauges();
        Assert.Equal("refused", Assert.Single(PublishedFor(measurements, scope, ClaimsInstrumentName)).Tags[OutcomeTagName]);
        Assert.Empty(PublishedFor(measurements, scope, HeldInstrumentName));
    }

    /// <summary>The gauge is how "who holds what" is read: one series per scope, from the replica holding it.</summary>
    [Fact]
    public void RecordClaim_AScopeThisReplicaTook_PublishesItAsHeld()
    {
        // Arrange
        var scope = WorkScope.Create("held-scope");
        using var measurements = new RecordedMailFathomMeasurements(HeldInstrumentName);
        LeaseTelemetry.RecordClaim(scope, granted: true);

        // Act
        measurements.ObserveGauges();

        // Assert
        Assert.Equal(1, Assert.Single(PublishedFor(measurements, scope, HeldInstrumentName)).Value);
    }

    /// <summary>
    /// A refused renewal is a replica losing a scope it thought it had, which is the one thing that stops work. It
    /// stops publishing the hold here rather than waiting for the holder to act, so the two replicas involved never
    /// both report it.
    /// </summary>
    [Fact]
    public void RecordRenewal_ARenewalTheHolderLost_CountsItAndStopsPublishingTheHold()
    {
        // Arrange
        var scope = WorkScope.Create("renewal-lost");
        using var measurements = new RecordedMailFathomMeasurements(RenewalsInstrumentName, HeldInstrumentName);
        LeaseTelemetry.RecordClaim(scope, granted: true);

        // Act
        LeaseTelemetry.RecordRenewal(scope, granted: false);

        // Assert
        measurements.ObserveGauges();
        Assert.Equal(
            "refused",
            Assert.Single(PublishedFor(measurements, scope, RenewalsInstrumentName)).Tags[OutcomeTagName]);
        Assert.Empty(PublishedFor(measurements, scope, HeldInstrumentName));
    }

    [Fact]
    public void RecordRenewal_ARenewalTheHolderKept_CountsItAndGoesOnPublishingTheHold()
    {
        // Arrange
        var scope = WorkScope.Create("renewal-kept");
        using var measurements = new RecordedMailFathomMeasurements(RenewalsInstrumentName, HeldInstrumentName);
        LeaseTelemetry.RecordClaim(scope, granted: true);

        // Act
        LeaseTelemetry.RecordRenewal(scope, granted: true);

        // Assert
        measurements.ObserveGauges();
        Assert.Equal(
            "granted",
            Assert.Single(PublishedFor(measurements, scope, RenewalsInstrumentName)).Tags[OutcomeTagName]);
        Assert.Equal(1, Assert.Single(PublishedFor(measurements, scope, HeldInstrumentName)).Value);
    }

    [Fact]
    public void RecordRelease_AScopeGivenBack_StopsPublishingItAsHeld()
    {
        // Arrange
        var scope = WorkScope.Create("released-scope");
        using var measurements = new RecordedMailFathomMeasurements(HeldInstrumentName);
        LeaseTelemetry.RecordClaim(scope, granted: true);

        // Act
        LeaseTelemetry.RecordRelease(scope);

        // Assert
        measurements.ObserveGauges();
        Assert.Empty(PublishedFor(measurements, scope, HeldInstrumentName));
    }

    /// <summary>
    /// The hold is a fresh identity per lease, so publishing it would make a new series of every takeover. An operator
    /// who needs it reads the lease table, which is where it is kept.
    /// </summary>
    [Fact]
    public void RecordClaim_AClaim_PublishesNoHolderIdentityOnAnyMeasurement()
    {
        // Arrange
        var scope = WorkScope.Create("holder-off-the-series");
        using var measurements = new RecordedMailFathomMeasurements(ClaimsInstrumentName, HeldInstrumentName);

        // Act
        LeaseTelemetry.RecordClaim(scope, granted: true);

        // Assert
        measurements.ObserveGauges();
        Assert.All(
            measurements.Recorded.SelectMany(measurement => measurement.Tags.Keys),
            tag => Assert.True(tag is ScopeTagName or OutcomeTagName, tag));
    }

    private static IReadOnlyList<RecordedMeasurement> PublishedFor(
        RecordedMailFathomMeasurements measurements,
        WorkScope scope,
        string instrumentName) =>
    [
        .. measurements.Read(instrumentName)
            .Where(measurement => Equals(measurement.Tags.GetValueOrDefault(ScopeTagName), scope.Value)),
    ];
}
