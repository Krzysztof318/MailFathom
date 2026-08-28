// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>Proves that a surface accepting a password is refused at startup unless the hop it crosses is encrypted.</summary>
/// <remarks>
/// <para>
/// The rule itself is <see cref="PasswordTransportConfidentiality" />'s and is covered there. What is asserted
/// here is the part only this composition can settle: that the rule is reached at all, that it is reached for both of
/// the surfaces that may accept a password, and that it is read after each section has answered for itself — because it
/// reads a proxy section that raises rather than answering when it has not yet been validated.
/// </para>
/// <para>
/// It is a unit test rather than a started host because the refusal happens before a socket is bound: what a deployment
/// meets is a start that stopped, and what an operator reads is this sentence.
/// </para>
/// </remarks>
public sealed class ComposedSettingsPasswordConfidentialityTests
{
    [Theory]
    [InlineData(McpEndpointOptions.SectionName)]
    [InlineData(ClientEndpointOptions.SectionName)]
    public void FindSurfaceRefusals_ASurfaceAcceptingAPasswordOverAClearTextHop_IsRefused(string sectionName)
    {
        // Arrange
        var configuration = Settings(
            new($"{sectionName}:Enabled", "true"),
            new($"{sectionName}:Authentication:0:Basic:AttemptsPerMinute", "10"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals, candidate => candidate.SectionName == sectionName);
        Assert.Contains(refusal.Errors, error => error.Contains("Basic", StringComparison.Ordinal));
    }

    /// <summary>The endpoint holding the certificate is the arrangement that needs nothing else stated, so it starts.</summary>
    [Fact]
    public void FindSurfaceRefusals_ASurfaceTerminatingTlsItself_IsAccepted()
    {
        // Arrange
        var configuration = Settings(
            new("ClientEndpoint:Enabled", "true"),
            new("ClientEndpoint:Transport", "HttpsOnly"),
            new("ClientEndpoint:Https:Endpoints:0:Name", "client"),
            new("ClientEndpoint:Https:Endpoints:0:Domain", "mail.example.test"),
            new("ClientEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:Name", "client-certificate"),
            new("ClientEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:SecretReference", "file:/etc/mailfathom/mail.pfx"),
            new("ClientEndpoint:Authentication:0:Basic:AttemptsPerMinute", "10"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>Binding both sockets terminates TLS and still answers the routes in the clear, so the redirect is what decides whether the password can cross an unencrypted hop.</summary>
    [Theory]
    [InlineData("true", 0)]
    [InlineData("false", 1)]
    public void FindSurfaceRefusals_APasswordOnASurfaceBindingBothSockets_IsDecidedByTheRedirect(
        string redirectEnabled,
        int expectedRefusals)
    {
        // Arrange
        var configuration = Settings(
            new("ClientEndpoint:Enabled", "true"),
            new("ClientEndpoint:Transport", "HttpAndHttps"),
            new("ClientEndpoint:Https:Redirect:Enabled", redirectEnabled),
            new("ClientEndpoint:Https:Endpoints:0:Name", "client"),
            new("ClientEndpoint:Https:Endpoints:0:Domain", "mail.example.test"),
            new("ClientEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:Name", "client-certificate"),
            new("ClientEndpoint:Https:Endpoints:0:ServerCertificate:Bundle:SecretReference", "file:/etc/mailfathom/mail.pfx"),
            new("ClientEndpoint:Authentication:0:Basic:AttemptsPerMinute", "10"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        Assert.Equal(expectedRefusals, refusals.Count);
    }

    /// <summary>Naming the proxy that terminates TLS is the existing contract by which a forwarded scheme is believed at all.</summary>
    [Fact]
    public void FindSurfaceRefusals_APasswordBehindANamedProxy_IsAccepted()
    {
        // Arrange
        var configuration = Settings(
            new("ClientEndpoint:Enabled", "true"),
            new("ClientEndpoint:Authentication:0:Basic:AttemptsPerMinute", "10"),
            new("ReverseProxy:TrustedProxies:0", "10.0.0.5"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>
    /// The proxy section raises rather than answering until it has been validated, so a shape that is wrong in both
    /// places has to report the proxy's own refusal instead of failing while reading it.
    /// </summary>
    [Fact]
    public void FindSurfaceRefusals_APasswordBesideAProxySectionThatIsItselfWrong_ReportsThatSectionsOwnRefusal()
    {
        // Arrange
        var configuration = Settings(
            new("ClientEndpoint:Enabled", "true"),
            new("ClientEndpoint:Authentication:0:Basic:AttemptsPerMinute", "10"),
            new("ReverseProxy:TrustedProxies:0", "not-an-address"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals);
        Assert.Equal(ReverseProxyOptions.SectionName, refusal.SectionName);
    }

    /// <summary>Nothing is refused about a surface accepting no password, whatever its transport is.</summary>
    [Fact]
    public void FindSurfaceRefusals_ASurfaceAcceptingOnlyAKeyOverAClearTextHop_IsAccepted()
    {
        // Arrange
        var configuration = Settings(
            new("ClientEndpoint:Enabled", "true"),
            new("ClientEndpoint:Authentication:0:ApiKey:Name", "desktop"),
            new("ClientEndpoint:Authentication:0:ApiKey:SecretReference", "plaintext:not-a-real-key"));

        // Act
        var refusals = ComposedSettings.FindSurfaceRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    private static IConfiguration Settings(params KeyValuePair<string, string?>[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
