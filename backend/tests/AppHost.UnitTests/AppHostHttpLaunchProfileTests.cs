// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using Xunit;

namespace MailFathom.AppHost.UnitTests;

/// <summary>Covers the launch profile the AppHost actually starts under locally.</summary>
public sealed class AppHostHttpLaunchProfileTests
{
    /// <summary>
    /// The documentation says there is no local TLS listener. Aspire still allocates an HTTPS dashboard unless the
    /// profile that is named <c>http</c> says it may run unsecured, and a machine whose development certificate is
    /// present but untrusted then fails while allocating <c>aspire-dashboard-https</c>.
    /// </summary>
    [Fact]
    public void HttpProfile_AllowsUnsecuredTransportAndNamesNoHttpsDashboard()
    {
        // Arrange
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Build", "launchSettings.json")));
        var http = document.RootElement.GetProperty("profiles").GetProperty("http");
        var environment = http.GetProperty("environmentVariables");

        // Act
        var allowsUnsecured = environment.GetProperty("ASPIRE_ALLOW_UNSECURED_TRANSPORT").GetString();
        var applicationUrl = http.TryGetProperty("applicationUrl", out var url) ? url.GetString() : null;

        // Assert
        Assert.Equal("true", allowsUnsecured, StringComparer.Ordinal);
        Assert.Equal("Development", environment.GetProperty("DOTNET_ENVIRONMENT").GetString(), StringComparer.Ordinal);
        if (applicationUrl is not null)
        {
            Assert.DoesNotContain("https://", applicationUrl, StringComparison.OrdinalIgnoreCase);
        }
    }
}
