// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>Proves that a surface accepting a password over a clear-text hop starts, and that the one refusal of a password that remains is untouched.</summary>
/// <remarks>
/// <para>
/// A previous release refused exactly this arrangement, which is the shape a Compose or loopback deployment actually
/// runs in: plain HTTP on a published port, with nothing declared in front because nothing has to be. The process can
/// read the scheme of its own socket and nothing beyond it, so that refusal could not tell the deployment it existed to
/// allow from the one nobody meant to expose. It is now reported at startup instead —
/// <c>PasswordClearTextTransportWarning</c> covers the reporting, and this covers the starting.
/// </para>
/// <para>
/// It is a unit test rather than a started host because a refusal happens before a socket is bound: what a deployment
/// used to meet was a start that stopped, and what it meets now is nothing at all.
/// </para>
/// </remarks>
public sealed class ComposedSettingsPasswordTransportTests
{
    /// <summary>Both surfaces that may accept a password accept one on the transport a quick start prepares.</summary>
    [Theory]
    [InlineData(McpEndpointOptions.SectionName)]
    [InlineData(ClientEndpointOptions.SectionName)]
    public void FindSurfaceRefusals_ASurfaceAcceptingAPasswordOverAClearTextHop_IsAccepted(string sectionName)
    {
        // Arrange
        var configuration = Settings(
            new($"{sectionName}:Enabled", "true"),
            new($"{sectionName}:Authentication:0:Method", "password"),
            new($"{sectionName}:Authentication:0:Basic:AttemptsPerMinute", "10"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>Binding both sockets with no redirect answers the routes in the clear, which used to be the arrangement the refusal was sharpest about.</summary>
    [Fact]
    public void FindSurfaceRefusals_APasswordOnASurfaceAnsweringItsRoutesBesideItsOwnTls_IsAccepted()
    {
        // Arrange
        var configuration = Settings(
            new("ClientEndpoint:Enabled", "true"),
            new("ClientEndpoint:Transport", "HttpAndHttps"),
            new("ClientEndpoint:Https:Redirect:Enabled", "false"),
            new("ClientEndpoint:Https:Endpoints:0:Name", "client"),
            new("ClientEndpoint:Https:Endpoints:0:Domain", "mail.example.test"),
            new("ClientEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:Name", "client-certificate"),
            new("ClientEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:SecretReference", "file:/etc/mailfathom/mail.pfx"),
            new("ClientEndpoint:Authentication:0:Method", "password"),
            new("ClientEndpoint:Authentication:0:Basic:AttemptsPerMinute", "10"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>
    /// The administrative endpoint's refusal of a password is a different rule under a different reason — that surface
    /// answers for the deployment rather than for a person — and nothing about the transport withdrawal reaches it.
    /// </summary>
    [Fact]
    public void FindSurfaceRefusals_APasswordOnTheAdministrativeEndpoint_IsStillRefused()
    {
        // Arrange
        var configuration = Settings(
            new("AdminEndpoint:Enabled", "true"),
            new("AdminEndpoint:Authentication:0:Basic:AttemptsPerMinute", "10"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals, candidate => candidate.SectionName == AdminEndpointOptions.SectionName);
        Assert.Contains(refusal.Errors, error => error.Contains("Basic", StringComparison.Ordinal));
    }

    /// <summary>A section that is wrong in its own right still answers for itself, which is what the composition order was always for.</summary>
    [Fact]
    public void FindSurfaceRefusals_APasswordBesideAProxySectionThatIsItselfWrong_ReportsThatSectionsOwnRefusal()
    {
        // Arrange
        var configuration = Settings(
            new("ClientEndpoint:Enabled", "true"),
            new("ClientEndpoint:Authentication:0:Method", "password"),
            new("ClientEndpoint:Authentication:0:Basic:AttemptsPerMinute", "10"),
            new("ReverseProxy:TrustedProxies:0", "not-an-address"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals);
        Assert.Equal(ReverseProxyOptions.SectionName, refusal.SectionName);
    }

    private static IConfiguration Settings(params KeyValuePair<string, string?>[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
