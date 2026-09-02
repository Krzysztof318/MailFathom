// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Observability;
using MailFathom.Versioning;
using OpenTelemetry.Resources;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability;

/// <summary>Covers the two attributes this process puts on the resource of everything it exports.</summary>
/// <remarks>
/// The failure being guarded against is silent at the far end: records keep arriving and nothing on them says which
/// build produced them, which is discovered when a rollout has to be attributed and the history no longer can be.
/// Asserting the values against the assembly's own stamp rather than against literals is what keeps a reporting path
/// that froze on one build from staying plausible after the declared version moved.
/// </remarks>
public sealed class StampedBuildResourceExtensionsTests
{
    [Fact]
    public void AddStampedBuildIdentity_Always_ReportsTheVersionStampedIntoTheHostAssembly()
    {
        // Arrange
        var stamped = StampedAssemblyVersion.ReadFrom(typeof(BootstrapLoggingSettings).Assembly);

        // Act
        var resource = ResourceBuilder.CreateEmpty().AddStampedBuildIdentity().Build();

        // Assert
        Assert.Equal(stamped.Version, ReadServiceVersion(resource));
        Assert.NotEmpty(ReadServiceVersion(resource));
        Assert.DoesNotContain("+", ReadServiceVersion(resource), StringComparison.Ordinal);
    }

    /// <summary>
    /// The revision is the half of the stamp the version deliberately drops, so it reaches a record through this
    /// attribute or through nothing at all.
    /// </summary>
    [Fact]
    public void AddStampedBuildIdentity_Always_ReportsTheRevisionStampedIntoTheHostAssembly()
    {
        // Arrange
        var stamped = StampedAssemblyVersion.ReadFrom(typeof(BootstrapLoggingSettings).Assembly);

        // Act
        var resource = ResourceBuilder.CreateEmpty().AddStampedBuildIdentity().Build();

        // Assert
        Assert.Equal(stamped.Revision, ReadSourceRevision(resource));
        Assert.NotEmpty(ReadSourceRevision(resource));
    }

    /// <summary>
    /// The startup records report both parts as properties of their own, so the two readings are the place this process
    /// could contradict itself about which build is running.
    /// </summary>
    [Fact]
    public void AddStampedBuildIdentity_Always_ReportsTheBuildTheStartupRecordsReport()
    {
        // Arrange
        var settings = BootstrapLoggingSettings.FromEnvironment();

        // Act
        var resource = ResourceBuilder.CreateEmpty().AddStampedBuildIdentity().Build();

        // Assert
        Assert.Equal(settings.ServiceVersion, ReadServiceVersion(resource));
        Assert.Equal(settings.ServiceRevision, ReadSourceRevision(resource));
    }

    /// <summary>
    /// A resource already carrying either attribute is what <c>OTEL_RESOURCE_ATTRIBUTES</c> produces, and the stamped
    /// values have to win over it: the build is a fact about the running process rather than a deployment's claim.
    /// </summary>
    [Fact]
    public void AddStampedBuildIdentity_OverABuildTheResourceAlreadyCarried_ReportsTheStampedOne()
    {
        // Arrange
        var supplied = ResourceBuilder.CreateEmpty()
            .AddAttributes(
            [
                KeyValuePair.Create(
                    StampedBuildResourceExtensions.ServiceVersionAttributeName,
                    (object)"99.0.0-supplied"),
                KeyValuePair.Create(
                    StampedBuildResourceExtensions.SourceRevisionAttributeName,
                    (object)"0000000000000000000000000000000000000000"),
            ]);

        // Act
        var resource = supplied.AddStampedBuildIdentity().Build();

        // Assert
        Assert.Equal(StampedBuildResourceExtensions.StampedServiceVersion, ReadServiceVersion(resource));
        Assert.Equal(StampedBuildResourceExtensions.StampedSourceRevision, ReadSourceRevision(resource));
    }

    /// <summary>
    /// The service identity stays the SDK's to resolve, so this adds the build and nothing beside it. An
    /// <c>AddService</c> call here would name the service a second time and disagree with the rest of the process
    /// whenever <c>OTEL_SERVICE_NAME</c> is unset.
    /// </summary>
    [Fact]
    public void AddStampedBuildIdentity_Always_AddsTheBuildAndNoOtherAttribute()
    {
        // Arrange
        string[] expected =
        [
            StampedBuildResourceExtensions.ServiceVersionAttributeName,
            StampedBuildResourceExtensions.SourceRevisionAttributeName,
        ];

        // Act
        var resource = ResourceBuilder.CreateEmpty().AddStampedBuildIdentity().Build();

        // Assert
        string[] reported = [.. resource.Attributes.Select(attribute => attribute.Key).Order()];
        Assert.Equal(expected, reported);
    }

    [Fact]
    public void AddStampedBuildIdentity_OnTheDefaultResource_KeepsTheServiceNameTheSdkResolved()
    {
        // Arrange
        var sdkResolved = ResourceBuilder.CreateDefault().Build();

        // Act
        var resource = ResourceBuilder.CreateDefault().AddStampedBuildIdentity().Build();

        // Assert
        Assert.Equal(ReadServiceName(sdkResolved), ReadServiceName(resource));
        Assert.Equal(StampedBuildResourceExtensions.StampedServiceVersion, ReadServiceVersion(resource));
        Assert.Equal(StampedBuildResourceExtensions.StampedSourceRevision, ReadSourceRevision(resource));
    }

    [Fact]
    public void AddStampedBuildIdentity_WithNoResourceBuilder_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(
            () => StampedBuildResourceExtensions.AddStampedBuildIdentity(null!));
    }

    private static string ReadServiceVersion(Resource resource) =>
        ReadAttribute(resource, StampedBuildResourceExtensions.ServiceVersionAttributeName);

    private static string ReadSourceRevision(Resource resource) =>
        ReadAttribute(resource, StampedBuildResourceExtensions.SourceRevisionAttributeName);

    private static string ReadServiceName(Resource resource) => ReadAttribute(resource, "service.name");

    private static string ReadAttribute(Resource resource, string attributeName)
    {
        var attribute = Assert.Single(resource.Attributes, candidate => candidate.Key == attributeName);

        return Assert.IsType<string>(attribute.Value);
    }
}
