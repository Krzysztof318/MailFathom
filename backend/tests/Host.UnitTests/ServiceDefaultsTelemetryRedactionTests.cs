// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Host.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using OpenTelemetry.Logs;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what this host removes from telemetry before it leaves the process.</summary>
/// <remarks>
/// An attachment download carries a signed capability in its path, and whoever holds it can fetch that file until it
/// expires. Two pipelines would record that path on their own: the tracing instrumentation writes it to
/// <c>url.path</c> verbatim, and the hosting middleware's log scope carries it on every record any request produces.
/// Without both of these a deployment exporting telemetry would be shipping short-lived bearer credentials over mail to
/// whatever stores them.
/// </remarks>
public sealed class ServiceDefaultsTelemetryRedactionTests
{
    /// <summary>The scope carrying the unredacted request path never reaches the exporter.</summary>
    /// <remarks>
    /// The value under test is the absence: the hosting scope's <c>RequestPath</c> is the raw path, so a record
    /// carrying scopes carries the capability of whatever download was in flight. Nothing else in this process opens a
    /// scope, and the request span still reports the path with the capability replaced, so the correlation this gives
    /// up is one the trace already holds.
    /// </remarks>
    [Fact]
    public void ConfigureExportedLogRecords_AnyDeployment_KeepsTheRequestPathScopeOffTheExporter()
    {
        // Arrange
        var logging = new OpenTelemetryLoggerOptions();

        // Act
        ServiceDefaultsExtensions.ConfigureExportedLogRecords(logging);

        // Assert
        Assert.False(logging.IncludeScopes);
        Assert.True(logging.IncludeFormattedMessage);
    }

    /// <summary>A deployment that named no collector exports nothing, however many publishers have been added.</summary>
    /// <remarks>
    /// The default this asserts is a privacy one rather than a convenience: every signal describes activity around
    /// personal mail, so a deployment sends it somewhere only by saying where. A blank value counts as unset on both
    /// sides, because templating a manifest routinely emits an empty string for a setting nobody chose.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExportsOverOtlp_NoEndpointNamed_KeepsEverySignalInTheProcess(string? configuredEndpoint)
    {
        // Arrange
        var configuration = ConfigurationWith(configuredEndpoint);

        // Act
        var exports = ServiceDefaultsExtensions.ExportsOverOtlp(configuration);

        // Assert
        Assert.False(exports);
    }

    /// <summary>Naming a collector is what attaches the exporter, which is the control for the absence above.</summary>
    [Fact]
    public void ExportsOverOtlp_AnEndpointNamed_ExportsToIt()
    {
        // Arrange
        var configuration = ConfigurationWith("http://localhost:4317");

        // Act
        var exports = ServiceDefaultsExtensions.ExportsOverOtlp(configuration);

        // Assert
        Assert.True(exports);
    }

    /// <summary>A probe is never traced, however many sibling changes have been made to the pipeline since.</summary>
    /// <remarks>
    /// Every probe path is driven rather than one, and the trailing-slash form with them, because routing serves
    /// <c>/health/</c> as readiness and a filter written for exact equality would trace the polling it was added to
    /// keep out.
    /// </remarks>
    [Theory]
    [InlineData("/started")]
    [InlineData("/health")]
    [InlineData("/health/")]
    [InlineData("/alive")]
    [InlineData("/ALIVE")]
    public void IsWorthTracing_AProbePath_KeepsThePollingOutOfTheTraceStore(string path)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        // Act
        var traced = ServiceDefaultsExtensions.IsWorthTracing(context);

        // Assert
        Assert.False(traced);
    }

    /// <summary>Everything a client actually calls is still traced, which is what makes the absence above readable.</summary>
    [Theory]
    [InlineData("/mcp")]
    [InlineData("/api/admin/embeddings/profiles")]
    [InlineData("/health/details")]
    [InlineData("/aliveness")]
    public void IsWorthTracing_AnyOtherPath_IsTraced(string path)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = path;

        // Act
        var traced = ServiceDefaultsExtensions.IsWorthTracing(context);

        // Assert
        Assert.True(traced);
    }

    /// <summary>The capability is replaced by the route it was served under, so the span still says what was fetched.</summary>
    [Fact]
    public void RedactAttachmentCapability_AttachmentDownload_ReplacesTheRecordedPathWithItsRouteTemplate()
    {
        // Arrange
        using var activity = new Activity("GET /attachments/{capability}");
        activity.SetTag("url.path", $"{EmailAttachmentDownloadEndpoint.RoutePrefix}/AQIDBAUGBwgJ.CgsMDQ4PEBES");
        var request = new DefaultHttpContext().Request;
        request.Path = $"{EmailAttachmentDownloadEndpoint.RoutePrefix}/AQIDBAUGBwgJ.CgsMDQ4PEBES";

        // Act
        ServiceDefaultsExtensions.RedactAttachmentCapability(activity, request);

        // Assert — read through TagObjects rather than Tags, which drops anything that is not a string.
        var recordedPath = Assert.Single(activity.TagObjects, tag => tag.Key == "url.path");
        Assert.Equal($"{EmailAttachmentDownloadEndpoint.RoutePrefix}/{{capability}}", recordedPath.Value);
    }

    /// <summary>Every other route keeps the path it was served under, because none of them carries a secret in it.</summary>
    [Theory]
    [InlineData("/mcp")]
    [InlineData("/api/admin/embeddings/profiles")]
    [InlineData("/attachmentsomething")]
    public void RedactAttachmentCapability_AnyOtherRoute_LeavesTheRecordedPathAlone(string path)
    {
        // Arrange
        using var activity = new Activity("request");
        activity.SetTag("url.path", path);
        var request = new DefaultHttpContext().Request;
        request.Path = path;

        // Act
        ServiceDefaultsExtensions.RedactAttachmentCapability(activity, request);

        // Assert
        var recordedPath = Assert.Single(activity.TagObjects, tag => tag.Key == "url.path");
        Assert.Equal(path, recordedPath.Value);
    }

    private static IConfiguration ConfigurationWith(string? configuredEndpoint) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    ServiceDefaultsExtensions.ExporterEndpointVariableName,
                    configuredEndpoint),
            ])
            .Build();
}
