// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Host.Api;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers the one request attribute this host removes before a span leaves the process.</summary>
/// <remarks>
/// An attachment download carries a signed capability in its path, and whoever holds it can fetch that file until it
/// expires. The instrumentation records the path verbatim, so without this a deployment exporting traces would be
/// shipping short-lived bearer credentials over mail to whatever stores them.
/// </remarks>
public sealed class ServiceDefaultsTelemetryRedactionTests
{
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
}
