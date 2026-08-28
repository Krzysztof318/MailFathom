// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers which arrangements may carry a password, and which are refused before the process serves one.</summary>
/// <remarks>
/// The refusal exists because a password is the one credential a person types and reuses, so the clear-text hop the
/// other methods are merely warned about is unreachable for this one. What the cases below pin is that the two
/// arrangements a deployment actually runs satisfy it, that neither an unserved endpoint nor one configuring no password
/// is refused, and that the trust-everything range is not a way of writing the promise the refusal asks for.
/// </remarks>
public sealed class PasswordTransportConfidentialityTests
{
    private const string SectionName = "ClientEndpoint";

    /// <summary>This process holding the certificate is the arrangement that needs nothing else stated.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEndpointTerminatingTlsItself_ReportsNothing()
    {
        // Act
        var errors = PasswordTransportConfidentiality.FindConfigurationErrors(
            SectionName,
            enabled: true,
            allowsBasic: true,
            servesClearText: false,
            new ReverseProxyOptions());

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Naming what stands in front is the existing contract by which a forwarded scheme is believed at all, so it is what says the hop is encrypted.</summary>
    [Theory]
    [InlineData("10.0.0.5")]
    [InlineData("10.0.0.0/24")]
    [InlineData("2001:db8::1")]
    public void FindConfigurationErrors_AProxyNamedInFront_ReportsNothing(string trustedProxy)
    {
        // Arrange
        var reverseProxy = new ReverseProxyOptions();
        reverseProxy.TrustedProxies.Add(trustedProxy);

        // Act
        var errors = PasswordTransportConfidentiality.FindConfigurationErrors(
            SectionName,
            enabled: true,
            allowsBasic: true,
            servesClearText: true,
            reverseProxy);

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void FindConfigurationErrors_APasswordOverAClearTextHop_IsRefusedWithBothRemedies()
    {
        // Act
        var errors = PasswordTransportConfidentiality.FindConfigurationErrors(
            SectionName,
            enabled: true,
            allowsBasic: true,
            servesClearText: true,
            new ReverseProxyOptions());

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SectionName}:Authentication", reported, StringComparison.Ordinal);
        Assert.Contains($"{SectionName}:Https:Endpoints", reported, StringComparison.Ordinal);
        Assert.Contains($"{ReverseProxyOptions.SectionName}:{nameof(ReverseProxyOptions.TrustedProxies)}", reported, StringComparison.Ordinal);
    }

    /// <summary>A range covering every address is the posture a section stating nothing already has, so reading it as a proxy would make the refusal satisfiable by writing it down.</summary>
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void FindConfigurationErrors_ARangeCoveringEveryAddress_IsRefusedLikeNamingNoProxyAtAll(string trustedProxy)
    {
        // Arrange
        var reverseProxy = new ReverseProxyOptions();
        reverseProxy.TrustedProxies.Add(trustedProxy);

        // Act
        var errors = PasswordTransportConfidentiality.FindConfigurationErrors(
            SectionName,
            enabled: true,
            allowsBasic: true,
            servesClearText: true,
            reverseProxy);

        // Assert
        Assert.Single(errors);
    }

    /// <summary>A range covering every address beside a real one still names a proxy, because the deployment stated where the traffic comes from as well as giving up.</summary>
    [Fact]
    public void FindConfigurationErrors_ARangeCoveringEveryAddressBesideANamedProxy_IsStillRefused()
    {
        // Arrange
        var reverseProxy = new ReverseProxyOptions();
        reverseProxy.TrustedProxies.Add("10.0.0.5");
        reverseProxy.TrustedProxies.Add("0.0.0.0/0");

        // Act
        var errors = PasswordTransportConfidentiality.FindConfigurationErrors(
            SectionName,
            enabled: true,
            allowsBasic: true,
            servesClearText: true,
            reverseProxy);

        // Assert
        Assert.Single(errors);
    }

    /// <summary>Nothing is refused about a surface nobody can reach, or one carrying no password to protect.</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void FindConfigurationErrors_ASurfaceServingNoPassword_ReportsNothing(bool enabled, bool allowsBasic)
    {
        // Act
        var errors = PasswordTransportConfidentiality.FindConfigurationErrors(
            SectionName,
            enabled,
            allowsBasic,
            servesClearText: true,
            new ReverseProxyOptions());

        // Assert
        Assert.Empty(errors);
    }
}
