// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers the only record an operator has of a boundary a refused caller is told nothing about.</summary>
/// <remarks>
/// The publisher is built per test, because each one asserts over a logger of its own and a shared instance could
/// carry only one. What that costs is an instrument per test on the application's one meter, which is affordable here
/// and would not be for an observable one: a counter measures when something calls it, so an instance nothing calls
/// again reports nothing, while a gauge would answer the meter for the rest of the run. What keeps the measurements
/// apart is not the instance but the operation — every test names one of its own, so a listener watching a shared
/// counter name reads back only what that test published.
/// </remarks>
public sealed class AuthorizationRefusalTelemetryTests : IDisposable
{
    private const string RefusalsInstrumentName = "mailfathom.authorization.refusals";

    private const string CallerIdentity = "an-api-key";

    private readonly RecordingLoggerProvider logs = new();
    private readonly ILoggerFactory loggerFactory;
    private readonly AuthorizationRefusalTelemetry telemetry;

    public AuthorizationRefusalTelemetryTests()
    {
        this.loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(this.logs));
        this.telemetry = new AuthorizationRefusalTelemetry(
            this.loggerFactory.CreateLogger<AuthorizationRefusalTelemetry>());
    }

    public void Dispose()
    {
        this.loggerFactory.Dispose();
        this.logs.Dispose();
    }

    /// <summary>The three dimensions are what an alert partitions by: which surface, what was reached, and what was missing.</summary>
    [Theory]
    [InlineData(ProtectedSurface.Mail, "mail")]
    [InlineData(ProtectedSurface.Administration, "administration")]
    public void RecordRefusal_ARefusalOnASurface_CountsItUnderThatSurfaceOperationAndPermission(
        ProtectedSurface surface,
        string expectedSurfaceName)
    {
        // Arrange
        var operation = $"dimensions-{expectedSurfaceName}";
        using var measurements = new RecordedMailFathomMeasurements(RefusalsInstrumentName);

        // Act
        this.telemetry.RecordRefusal(surface, operation, MailFathomPermission.MailRead, CallerIdentity);

        // Assert
        var measurement = Assert.Single(RefusalsOf(measurements, operation));
        Assert.Equal(1, measurement.Value);
        Assert.Equal(expectedSurfaceName, measurement.Tags[AuthorizationRefusalTelemetry.SurfaceTagName]);
        Assert.Equal(
            MailFathomPermission.MailRead.Name,
            measurement.Tags[AuthorizationRefusalTelemetry.PermissionTagName]);
    }

    /// <summary>A refusal no grant would have satisfied is still counted, under the one value that says so.</summary>
    [Fact]
    public void RecordRefusal_ARefusalNamingNoPermission_CountsItUnderTheUnnamedPermission()
    {
        // Arrange
        const string Operation = "no-permission-would-have-helped";
        using var measurements = new RecordedMailFathomMeasurements(RefusalsInstrumentName);

        // Act
        this.telemetry.RecordRefusal(ProtectedSurface.Administration, Operation, default, CallerIdentity);

        // Assert
        var measurement = Assert.Single(RefusalsOf(measurements, Operation));
        Assert.Equal(
            AuthorizationRefusalTelemetry.UnnamedPermissionValue,
            measurement.Tags[AuthorizationRefusalTelemetry.PermissionTagName]);
    }

    /// <summary>The credential is what an operator repairs the grant of, and the counter cannot say which one it was.</summary>
    [Fact]
    public void RecordRefusal_ARefusalOfACaller_LogsTheCredentialAndThePermissionItLacked()
    {
        // Arrange
        const string Operation = "logs-the-credential";

        // Act
        this.telemetry.RecordRefusal(
            ProtectedSurface.Mail,
            Operation,
            MailFathomPermission.MailContactsWrite,
            CallerIdentity);

        // Assert
        var record = Assert.Single(this.logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal(CallerIdentity, record.Properties["RefusedIdentity"]);
        Assert.Equal(Operation, record.Properties["Operation"]);
        Assert.Equal(MailFathomPermission.MailContactsWrite.Name, record.Properties["RequiredPermission"]);
    }

    /// <summary>A log line telling an operator to grant a permission that would not have helped is worse than none.</summary>
    [Fact]
    public void RecordRefusal_ARefusalNamingNoPermission_LogsWithoutNamingOne()
    {
        // Arrange

        // Act
        this.telemetry.RecordRefusal(ProtectedSurface.Administration, "logs-no-permission", default, CallerIdentity);

        // Assert
        var record = Assert.Single(this.logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.DoesNotContain("RequiredPermission", record.Properties.Keys, StringComparer.Ordinal);
    }

    /// <summary>Work reached under no principal has nothing to name, and the record still says a refusal happened.</summary>
    [Fact]
    public void RecordRefusal_ARefusalUnderNoPrincipal_LogsTheFixedUnidentifiedValue()
    {
        // Arrange

        // Act
        this.telemetry.RecordRefusal(
            ProtectedSurface.Mail,
            "under-no-principal",
            MailFathomPermission.MailRead,
            refusedIdentity: null);

        // Assert
        Assert.Equal(
            AuthorizationRefusalTelemetry.UnidentifiedCallerValue,
            Assert.Single(this.logs.Records).Properties["RefusedIdentity"]);
    }

    /// <summary>Nothing about the caller reaches the counter, where a value the operator's credentials decide would be a series apiece.</summary>
    [Fact]
    public void RecordRefusal_ARefusalOfACaller_PutsNothingAboutTheCallerOnTheCounter()
    {
        // Arrange
        const string Operation = "identity-stays-off-the-counter";
        using var measurements = new RecordedMailFathomMeasurements(RefusalsInstrumentName);

        // Act
        this.telemetry.RecordRefusal(ProtectedSurface.Mail, Operation, MailFathomPermission.MailAsk, CallerIdentity);

        // Assert
        Assert.DoesNotContain(
            Assert.Single(RefusalsOf(measurements, Operation)).Tags,
            tag => tag.Value is string value && value.Contains(CallerIdentity, StringComparison.Ordinal));
    }

    /// <summary>A refusal with nothing to partition by is a defect in the boundary that recorded it, not a series to open.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RecordRefusal_AnOperationThatNamesNothing_IsRefused(string operation)
    {
        // Arrange

        // Act, Assert
        Assert.Throws<ArgumentException>(() => this.telemetry.RecordRefusal(
            ProtectedSurface.Mail,
            operation,
            MailFathomPermission.MailRead,
            CallerIdentity));
    }

    /// <summary>A surface added without a published value has to fail rather than silently rename a series.</summary>
    [Fact]
    public void RecordRefusal_ASurfaceWithNoPublishedValue_IsRefused()
    {
        // Arrange

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => this.telemetry.RecordRefusal(
            (ProtectedSurface)int.MaxValue,
            "an-unpublished-surface",
            MailFathomPermission.MailRead,
            CallerIdentity));
    }

    /// <summary>Reads the refusals one test published, which is what keeps the classes running in parallel apart.</summary>
    private static IReadOnlyList<RecordedMeasurement> RefusalsOf(
        RecordedMailFathomMeasurements measurements,
        string operation) =>
        [
            .. measurements.Read(RefusalsInstrumentName).Where(measurement => StringComparer.Ordinal.Equals(
                measurement.Tags.GetValueOrDefault(AuthorizationRefusalTelemetry.OperationTagName) as string,
                operation)),
        ];
}
