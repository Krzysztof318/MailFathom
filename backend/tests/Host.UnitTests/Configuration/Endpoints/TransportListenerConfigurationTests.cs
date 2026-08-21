// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers the listener rules the MCP and administrative endpoints hold in common.</summary>
/// <remarks>
/// These are asserted once, against the shared rules, rather than twice against each surface that uses them. What the
/// surfaces' own suites then cover is what only they can: which section a message names, which ports each claims, and
/// the settings neither shares with the other.
/// </remarks>
public sealed class TransportListenerConfigurationTests
{
    private const string SectionName = "McpEndpoint";

    [Fact]
    public void TerminatesTls_EachTransport_AnswersWhetherProfilesAreServed()
    {
        Assert.False(TransportListenerConfiguration.TerminatesTls(EndpointTransport.Http));
        Assert.True(TransportListenerConfiguration.TerminatesTls(EndpointTransport.HttpAndHttps));
        Assert.True(TransportListenerConfiguration.TerminatesTls(EndpointTransport.HttpsOnly));
    }

    [Fact]
    public void OpensClearTextListener_EachTransport_AnswersWhetherASocketIsBound()
    {
        Assert.True(TransportListenerConfiguration.OpensClearTextListener(EndpointTransport.Http));
        Assert.True(TransportListenerConfiguration.OpensClearTextListener(EndpointTransport.HttpAndHttps));
        Assert.False(TransportListenerConfiguration.OpensClearTextListener(EndpointTransport.HttpsOnly));
    }

    /// <summary>A redirect needs both a clear-text socket to bind and profiles to send a client to, which is one mode.</summary>
    [Fact]
    public void RedirectsClearText_EachTransportAndSetting_OnlyRedirectsWhereBothHold()
    {
        Assert.False(Redirects(EndpointTransport.Http, redirectEnabled: true));
        Assert.False(Redirects(EndpointTransport.HttpsOnly, redirectEnabled: true));
        Assert.True(Redirects(EndpointTransport.HttpAndHttps, redirectEnabled: true));
        Assert.False(Redirects(EndpointTransport.HttpAndHttps, redirectEnabled: false));
    }

    /// <summary>The clear-text socket is bound under HttpAndHttps whether it redirects or serves, so it is claimed either way.</summary>
    [Fact]
    public void ListenerPorts_EachTransport_ClaimsExactlyTheSocketsItBinds()
    {
        Assert.Equal([8080], ClaimedPorts(EndpointTransport.Http));
        Assert.Equal([8080, 8443], ClaimedPorts(EndpointTransport.HttpAndHttps));
        Assert.Equal([8443], ClaimedPorts(EndpointTransport.HttpsOnly));
    }

    [Fact]
    public void FindConfigurationErrors_AWellFormedTlsSurface_ReportsNothing() =>
        Assert.Empty(FindErrors(EndpointTransport.HttpsOnly, WithProfile()));

    [Fact]
    public void FindConfigurationErrors_AWellFormedClearTextSurface_ReportsNothing() =>
        Assert.Empty(FindErrors(EndpointTransport.Http, new TransportHttpsOptions()));

    /// <summary>The binder accepts any number for an enum, and a value naming no transport would open no listener while reporting nothing.</summary>
    [Fact]
    public void FindConfigurationErrors_ATransportNoMemberDeclares_IsRefused()
    {
        // Act
        var error = Assert.Single(FindErrors((EndpointTransport)7, new TransportHttpsOptions()));

        // Assert
        Assert.StartsWith($"{SectionName}:Transport", error, StringComparison.Ordinal);
        Assert.Contains("names no transport", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ATlsTransportWithoutAnyProfile_IsRefused() =>
        Assert.Contains(
            FindErrors(EndpointTransport.HttpsOnly, new TransportHttpsOptions()),
            error => error.Contains("no HTTPS profile is configured", StringComparison.Ordinal));

    /// <summary>Profiles nothing serves are a deployment believing its endpoint carries TLS, which is worse than one that knows it does not.</summary>
    [Fact]
    public void FindConfigurationErrors_ProfilesUnderAClearTextTransport_IsRefused() =>
        Assert.Contains(
            FindErrors(EndpointTransport.Http, WithProfile()),
            error => error.Contains("none of them is served", StringComparison.Ordinal));

    [Fact]
    public void FindConfigurationErrors_ARedirectStatedUnderATransportThatCannotServeOne_IsRefused()
    {
        Assert.Contains(
            FindErrors(EndpointTransport.Http, StatedRedirect(new TransportHttpsOptions())),
            error => error.Contains("a clear-text redirect is configured", StringComparison.Ordinal));

        Assert.Contains(
            FindErrors(EndpointTransport.HttpsOnly, StatedRedirect(WithProfile())),
            error => error.Contains("a clear-text redirect is configured", StringComparison.Ordinal));
    }

    /// <summary>Left at its default the redirect says nothing, so a surface that cannot serve one is not reported for a decision nobody took.</summary>
    [Fact]
    public void FindConfigurationErrors_ARedirectLeftAtItsDefault_IsNotReported()
    {
        Assert.Empty(FindErrors(EndpointTransport.Http, new TransportHttpsOptions()));
        Assert.Empty(FindErrors(EndpointTransport.HttpsOnly, WithProfile()));
    }

    [Fact]
    public void FindConfigurationErrors_ARedirectStatedUnderBothSchemes_IsAccepted()
    {
        // Arrange
        var httpsSettings = WithProfile();
        httpsSettings.Redirect.MarkStated();

        // Act, Assert
        Assert.Empty(FindErrors(EndpointTransport.HttpAndHttps, httpsSettings));
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    public void FindConfigurationErrors_ABindAddressThatIsNotAnAddress_IsRefused(string bindAddress)
    {
        // Act
        var error = Assert.Single(FindErrors(
            EndpointTransport.Http,
            new TransportHttpsOptions(),
            bindAddress: bindAddress));

        // Assert
        Assert.StartsWith($"{SectionName}:BindAddress", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void FindConfigurationErrors_APortOutsideTheTcpRange_IsRefused(int port)
    {
        // Act
        var error = Assert.Single(FindErrors(
            EndpointTransport.Http,
            new TransportHttpsOptions(),
            port: port));

        // Assert
        Assert.StartsWith($"{SectionName}:Port", error, StringComparison.Ordinal);
    }

    /// <summary>A transport opening no clear-text socket binds neither the address nor the port, so neither describes anything to refuse.</summary>
    [Fact]
    public void FindConfigurationErrors_AnUnusableClearTextSocketUnderTlsOnly_IsNotReported() =>
        Assert.Empty(FindErrors(
            EndpointTransport.HttpsOnly,
            WithProfile(),
            bindAddress: "not-an-address",
            port: 0));

    /// <summary>One socket cannot serve both schemes, and the operating system would report it as an address already in use.</summary>
    [Fact]
    public void FindConfigurationErrors_AClearTextPortAProfileAlreadyBinds_IsRefused() =>
        Assert.Contains(
            FindErrors(EndpointTransport.HttpAndHttps, WithProfile(), port: 8443),
            error => error.Contains("one socket cannot serve both schemes", StringComparison.Ordinal));

    /// <summary>Two specific addresses on one port are two sockets the operating system grants independently.</summary>
    [Fact]
    public void FindConfigurationErrors_AClearTextSocketOnAnotherAddressSharingAProfilePort_IsAccepted() =>
        Assert.Empty(FindErrors(
            EndpointTransport.HttpAndHttps,
            WithProfile(bindAddress: "10.0.0.1"),
            bindAddress: "10.0.0.2",
            port: 8443));

    private static bool Redirects(EndpointTransport transport, bool redirectEnabled) =>
        TransportListenerConfiguration.RedirectsClearText(
            transport,
            new TransportClearTextRedirectOptions { Enabled = redirectEnabled });

    private static IEnumerable<int> ClaimedPorts(EndpointTransport transport) =>
        TransportListenerConfiguration.ListenerPorts(transport, 8080, WithProfile()).Order();

    private static TransportHttpsOptions StatedRedirect(TransportHttpsOptions httpsSettings)
    {
        httpsSettings.Redirect.MarkStated();

        return httpsSettings;
    }

    private static IReadOnlyList<string> FindErrors(
        EndpointTransport transport,
        TransportHttpsOptions httpsSettings,
        string bindAddress = "0.0.0.0",
        int port = 8080) =>
        TransportListenerConfiguration.FindConfigurationErrors(
            SectionName,
            bindAddress,
            port,
            transport,
            httpsSettings,
            http3Supported: true);

    private static TransportHttpsOptions WithProfile(string bindAddress = "0.0.0.0")
    {
        var settings = new TransportHttpsOptions();

        settings.Endpoints.Add(new TransportHttpsEndpointOptions
        {
            Name = "public",
            Domain = "mail.example.test",
            BindAddress = bindAddress,
            Port = 8443,
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret
                {
                    Name = "bundle",
                    SecretReference = "file:/etc/mailfathom/tls/mail.pfx",
                },
            },
        });

        return settings;
    }
}
