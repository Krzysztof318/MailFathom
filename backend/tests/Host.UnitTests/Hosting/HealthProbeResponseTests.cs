// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Host.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting;

/// <summary>Covers what a probe response carries, which is the aggregate status and nothing else.</summary>
/// <remarks>
/// The endpoint is unauthenticated by design, so everything the body carries is disclosed to whoever can reach the
/// port. A check name says which dependencies exist, a description or an exception message says what went wrong with
/// one, and a duration says how a deployment is performing; none of them is something an orchestrator compares.
/// </remarks>
public sealed class HealthProbeResponseTests
{
    [Fact]
    public async Task WriteAggregateStatusAsync_AnUnhealthyReport_WritesTheStatusAndNothingAboutTheChecks()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal)
            {
                ["database"] = new(
                    HealthStatus.Unhealthy,
                    description: "Connection refused to postgres.internal:5432",
                    duration: TimeSpan.FromMilliseconds(1234),
                    exception: new InvalidOperationException("Npgsql could not open a connection"),
                    data: null),
            },
            TimeSpan.FromMilliseconds(1234));

        // Act
        await HealthProbeEndpoints.WriteAggregateStatusAsync(context, report);

        // Assert
        var written = Encoding.UTF8.GetString(body.ToArray());

        Assert.Equal("Unhealthy", written);
        Assert.Equal("text/plain", context.Response.ContentType);
    }

    [Fact]
    public async Task WriteAggregateStatusAsync_AHealthyReport_WritesTheAggregateStatus()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var body = new MemoryStream();
        context.Response.Body = body;

        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(StringComparer.Ordinal),
            TimeSpan.Zero);

        // Act
        await HealthProbeEndpoints.WriteAggregateStatusAsync(context, report);

        // Assert
        Assert.Equal("Healthy", Encoding.UTF8.GetString(body.ToArray()));
    }
}
