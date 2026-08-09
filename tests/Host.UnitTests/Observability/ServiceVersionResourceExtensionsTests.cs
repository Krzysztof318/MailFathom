// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Observability;
using MailFathom.Versioning;
using OpenTelemetry.Resources;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability;

/// <summary>Covers the one attribute this process puts on the resource of everything it exports.</summary>
/// <remarks>
/// The failure being guarded against is silent at the far end: records keep arriving and nothing on them says which
/// build produced them, which is discovered when a rollout has to be attributed and the history no longer can be.
/// Asserting the value against the assembly's own stamp rather than against a literal is what keeps a reporting path
/// that froze on one version from staying plausible after the declared version moved.
/// </remarks>
public sealed class ServiceVersionResourceExtensionsTests
{
    [Fact]
    public void AddStampedServiceVersion_Always_ReportsTheVersionStampedIntoTheHostAssembly()
    {
        // Arrange
        var stamped = StampedAssemblyVersion.ReadFrom(typeof(BootstrapLoggingSettings).Assembly);

        // Act
        var resource = ResourceBuilder.CreateEmpty().AddStampedServiceVersion().Build();

        // Assert
        Assert.Equal(stamped.Version, ReadServiceVersion(resource));
        Assert.NotEmpty(ReadServiceVersion(resource));
        Assert.DoesNotContain("+", ReadServiceVersion(resource), StringComparison.Ordinal);
    }

    /// <summary>
    /// The startup records report the version as a property of their own, so the two readings are the place this
    /// process could contradict itself about which build is running.
    /// </summary>
    [Fact]
    public void AddStampedServiceVersion_Always_ReportsTheVersionTheStartupRecordsReport()
    {
        // Arrange
        var settings = BootstrapLoggingSettings.FromEnvironment();

        // Act
        var resource = ResourceBuilder.CreateEmpty().AddStampedServiceVersion().Build();

        // Assert
        Assert.Equal(settings.ServiceVersion, ReadServiceVersion(resource));
    }

    /// <summary>
    /// A resource already carrying the attribute is what <c>OTEL_RESOURCE_ATTRIBUTES</c> produces, and the stamped
    /// value has to win over it: the build is a fact about the running process rather than a deployment's claim.
    /// </summary>
    [Fact]
    public void AddStampedServiceVersion_OverAVersionTheResourceAlreadyCarried_ReportsTheStampedOne()
    {
        // Arrange
        var supplied = ResourceBuilder.CreateEmpty()
            .AddAttributes([KeyValuePair.Create(
                ServiceVersionResourceExtensions.ServiceVersionAttributeName,
                (object)"99.0.0-supplied")]);

        // Act
        var resource = supplied.AddStampedServiceVersion().Build();

        // Assert
        Assert.Equal(ServiceVersionResourceExtensions.StampedServiceVersion, ReadServiceVersion(resource));
    }

    /// <summary>
    /// The service identity stays the SDK's to resolve, so this adds the version and nothing beside it. An
    /// <c>AddService</c> call here would name the service a second time and disagree with the rest of the process
    /// whenever <c>OTEL_SERVICE_NAME</c> is unset.
    /// </summary>
    [Fact]
    public void AddStampedServiceVersion_Always_AddsTheVersionAndNoOtherAttribute()
    {
        // Act
        var resource = ResourceBuilder.CreateEmpty().AddStampedServiceVersion().Build();

        // Assert
        var attribute = Assert.Single(resource.Attributes);
        Assert.Equal(ServiceVersionResourceExtensions.ServiceVersionAttributeName, attribute.Key);
    }

    [Fact]
    public void AddStampedServiceVersion_OnTheDefaultResource_KeepsTheServiceNameTheSdkResolved()
    {
        // Arrange
        var sdkResolved = ResourceBuilder.CreateDefault().Build();

        // Act
        var resource = ResourceBuilder.CreateDefault().AddStampedServiceVersion().Build();

        // Assert
        Assert.Equal(ReadServiceName(sdkResolved), ReadServiceName(resource));
        Assert.Equal(ServiceVersionResourceExtensions.StampedServiceVersion, ReadServiceVersion(resource));
    }

    [Fact]
    public void AddStampedServiceVersion_WithNoResourceBuilder_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(
            () => ServiceVersionResourceExtensions.AddStampedServiceVersion(null!));
    }

    private static string ReadServiceVersion(Resource resource) =>
        ReadAttribute(resource, ServiceVersionResourceExtensions.ServiceVersionAttributeName);

    private static string ReadServiceName(Resource resource) => ReadAttribute(resource, "service.name");

    private static string ReadAttribute(Resource resource, string attributeName)
    {
        var attribute = Assert.Single(resource.Attributes, candidate => candidate.Key == attributeName);

        return Assert.IsType<string>(attribute.Value);
    }
}
