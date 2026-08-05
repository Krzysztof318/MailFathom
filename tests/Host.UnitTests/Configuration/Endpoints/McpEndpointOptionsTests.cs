// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.ClientCertificates;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Endpoints;

/// <summary>Covers the decisions the MCP endpoint section carries and what an unusable combination of them does.</summary>
/// <remarks>
/// The path is a constant the protocol surface publishes and the transport is always stateless, so neither can be
/// misconfigured and neither needs validating. What remains is a set of security decisions, and each one is worth a
/// test for the same reason: the failure mode of getting one wrong is a mailbox served to somebody who should not have
/// reached it.
/// </remarks>
public sealed class McpEndpointOptionsTests
{
    [Fact]
    public void Enabled_UnconfiguredDeployment_ServesNoMcpEndpoint()
    {
        // Arrange, Act
        var options = new McpEndpointOptions();

        // Assert
        Assert.False(options.Enabled);
    }

    /// <summary>A disabled endpoint has nothing to guard, so half-written security settings beside it are not a reason to refuse to start.</summary>
    [Fact]
    public void FindConfigurationErrors_DisabledEndpoint_ReportsNothing()
    {
        // Arrange
        var options = new McpEndpointOptions();
        options.Cors.AllowedOrigins.Add("not-an-origin");

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    [Fact]
    public void Authentication_UnconfiguredDeployment_RequiresNoCredential()
    {
        // Arrange, Act
        var options = new McpEndpointOptions();

        // Assert
        Assert.Equal(TransportAuthenticationMethods.None, options.Authentication);
        Assert.False(options.RequiresAuthentication);
    }

    /// <summary>
    /// Authentication is turned on rather than chosen, so an enabled endpoint with none of it configured starts. What
    /// keeps that from being silent is the startup warning rather than a refusal, because refusing would make the
    /// loopback and reverse-proxy deployment the one needing extra settings.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_EnabledEndpointWithNoAuthenticationMethod_IsAccepted()
    {
        // Arrange
        var options = new McpEndpointOptions { Enabled = true };

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    [Fact]
    public void FindConfigurationErrors_ApiKeyAuthenticationWithAtLeastOneKey_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.ApiKey);
        options.ApiKeys.Add(Key("workstation"));

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    [Fact]
    public void FindConfigurationErrors_ApiKeyAuthenticationWithNoKey_IsRefusedBecauseNoClientCouldAuthenticate()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.ApiKey);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:ApiKeys", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The binder turns any number into an enum value, so a section carrying '4' would bind to a set no member declares.
    /// Every rule below asks whether a particular method is among them, and such a value answers no to all of them: no
    /// authentication is registered, no credential is required, and the unauthenticated warning stays silent because it
    /// is not <c>None</c> either. That combination opens the endpoint, which is why it is refused here.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AnAuthenticationValueNoMemberDeclares_IsRefusedRatherThanTreatedAsNeither()
    {
        // Arrange
        var options = EnabledWith((TransportAuthenticationMethods)4);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication", error, StringComparison.Ordinal);
    }

    /// <summary>An unknown bit is refused even beside a method that is real, because the request it would authorize is the same either way.</summary>
    [Fact]
    public void FindConfigurationErrors_AnUnknownMethodBesideAKnownOne_IsRefused()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.ApiKey | (TransportAuthenticationMethods)8);
        options.ApiKeys.Add(Key("workstation"));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication", error, StringComparison.Ordinal);
    }

    /// <summary>The two methods identify different kinds of caller, so a deployment reaching both a person and a scheduled job turns on both.</summary>
    [Fact]
    public void FindConfigurationErrors_ApiKeyAndOAuthTogether_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.ApiKey | TransportAuthenticationMethods.OAuth);
        options.ApiKeys.Add(Key("nightly-digest"));
        options.OAuth = OAuthWith(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
        Assert.True(options.AllowsApiKey);
        Assert.True(options.AllowsOAuth);
    }

    /// <summary>The same reasoning as an unchecked key: an authorization server nothing validates against is a trust relationship an operator believes they configured.</summary>
    [Fact]
    public void FindConfigurationErrors_AuthorizationServersConfiguredWhileOAuthIsNotAMethod_IsRefused()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.None);
        options.OAuth = OAuthWith(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:OAuth", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_OAuthWithNoAuthorizationServer_IsRefusedBecauseNoTokenCouldBeValidated()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.OAuth);
        options.OAuth = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" };

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:OAuth:AuthorizationServers", error, StringComparison.Ordinal);
    }

    /// <summary>The resource is what a token's audience is compared against, so a deployment without one accepts a token issued for any service the same server serves.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("mail.example.test/mcp")]
    [InlineData("http://mail.example.test/mcp")]
    [InlineData("https://mail.example.test/mcp#fragment")]
    [InlineData("https://mail.example.test/mcp?tenant=1")]
    public void FindConfigurationErrors_AResourceThatIsNotACanonicalUrl_IsRefused(string? resource)
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.OAuth);
        options.OAuth = OAuthWith(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));
        options.OAuth.Resource = resource;

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:OAuth:Resource", error, StringComparison.Ordinal);
    }

    /// <summary>Two profiles claiming one issuer would leave the key set a token is trusted against decided by configuration order.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoAuthorizationServersSharingAnIssuer_IsRefused()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.OAuth);
        options.OAuth = OAuthWith(
            AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"),
            AuthorizationServer("partners", "https://sso.example.test/realms/mailfathom"));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:OAuth:AuthorizationServers:1:Issuer", error, StringComparison.Ordinal);
    }

    /// <summary>A scope reaches a client inside a space-separated header parameter, so one carrying a space or a quotation mark would rewrite the challenge around it.</summary>
    [Theory]
    [InlineData("mail read")]
    [InlineData("mail\"read")]
    [InlineData("mail\\read")]
    [InlineData("")]
    public void FindConfigurationErrors_AScopeThatCouldRewriteTheChallenge_IsRefused(string scope)
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.OAuth);
        options.OAuth = OAuthWith(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));
        options.OAuth.RequiredScopes.Add(scope);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:OAuth:RequiredScopes:0", error, StringComparison.Ordinal);
    }

    /// <summary>Requiring no scope is the coarser boundary a deployment gets by default: any token this resource's servers issued is enough.</summary>
    [Fact]
    public void FindConfigurationErrors_OAuthWithNoRequiredScope_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.OAuth);
        options.OAuth = OAuthWith(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>
    /// Serving every browser origin with no credential required is the combination that makes DNS rebinding work, and
    /// it is deliberately not a configuration error: it is what a deployment behind a reverse proxy or on a trusted
    /// network runs, and refusing it would make the simple case the one that needs extra settings. The operator is told
    /// instead, by <c>McpTransportAuthenticationWarning</c>, which is where that judgement belongs.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_ExplicitlyUnauthenticated_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.None);

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>Keys nothing checks are a deployment believing it is protected, which is worse than one knowing it is not.</summary>
    [Fact]
    public void FindConfigurationErrors_KeysConfiguredWhileApiKeyIsNotAMethod_IsRefused()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.None);
        options.ApiKeys.Add(Key("workstation"));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:ApiKeys", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AnUnusableCorsPolicy_IsReportedUnderTheSectionThatCarriesIt()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.None);
        options.Cors.ServeEveryBrowserOrigin();
        options.Cors.AllowedOrigins.Add("https://client.example.test");

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Cors:AllowedOrigins", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AnUnusableRateLimit_IsReportedUnderTheSectionThatCarriesIt()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.None);
        options.RateLimiting.MaxConcurrentRequests = 0;

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:RateLimiting:MaxConcurrentRequests", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The limits apply to an enabled endpoint whether or not an operator wrote any of them down, so a section nobody
    /// configured has to pass validation rather than demanding numbers the product already has.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AnEndpointConfiguringNoLimits_ReportsNothing()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.None);

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>Every fault is reported together, so an operator fixing a section reads all of it rather than one restart at a time.</summary>
    [Fact]
    public void FindConfigurationErrors_SeveralFaults_ReportsThemAllAtOnce()
    {
        // Arrange
        var options = EnabledWith(TransportAuthenticationMethods.ApiKey);
        options.Cors.AllowedOrigins.Add("not-an-origin");

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void ApiKeys_UnconfiguredDeployment_IsEmptyRatherThanNull()
    {
        // Arrange, Act
        var options = new McpEndpointOptions();

        // Assert
        Assert.Empty(options.ApiKeys);
    }

    /// <summary>Mutual TLS is off unless a profile says otherwise, and it composes with the mode rather than replacing it.</summary>
    [Fact]
    public void ClientCertificateProfiles_UnconfiguredDeployment_IsEmptyRatherThanNull()
    {
        // Arrange, Act
        var options = EnabledWith(TransportAuthenticationMethods.None);

        // Assert
        Assert.Empty(options.ClientCertificateProfiles);
        Assert.Empty(options.FindConfigurationErrors());
        Assert.Empty(options.ToClientCertificateTrustProfiles());
    }

    [Fact]
    public void FindConfigurationErrors_AnUnusableClientCertificateProfile_IsReportedUnderItsPosition()
    {
        // Arrange
        var options = MutualTlsEndpoint();
        var profile = ConnectorProfile();
        profile.TrustAnchors.Clear();
        options.ClientCertificateProfiles.Add(profile);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:ClientCertificateProfiles:0:TrustAnchors", error, StringComparison.Ordinal);
    }

    /// <summary>A refusal in the log and an audit record are read by the profile's name, so two profiles answering to one name make both ambiguous.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoProfilesSharingAName_IsRefused()
    {
        // Arrange
        var options = MutualTlsEndpoint();
        options.ClientCertificateProfiles.Add(ConnectorProfile());
        options.ClientCertificateProfiles.Add(ConnectorProfile());

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:ClientCertificateProfiles:1:Name", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ToClientCertificateTrustProfiles_SeveralProfiles_MapsThemInConfigurationOrder()
    {
        // Arrange
        var options = MutualTlsEndpoint();
        options.ClientCertificateProfiles.Add(ConnectorProfile());
        var reportingProfile = ConnectorProfile();
        reportingProfile.Name = "reporting-service";
        options.ClientCertificateProfiles.Add(reportingProfile);

        // Act
        var trustProfiles = options.ToClientCertificateTrustProfiles();

        // Assert
        Assert.Equal(
            ["chatgpt-connector", "reporting-service"],
            trustProfiles.Select(profile => profile.Name));
    }

    /// <summary>An endpoint terminating TLS, which is what a client certificate needs before it can be presented at all.</summary>
    private static McpEndpointOptions MutualTlsEndpoint()
    {
        var options = EnabledWith(TransportAuthenticationMethods.None);
        options.Transport = EndpointTransport.HttpsOnly;
        options.Https.Endpoints.Add(new TransportHttpsEndpointOptions
        {
            Name = "public",
            Domain = "mail.example.test",
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/mail.pfx" },
            },
        });

        return options;
    }

    private static McpEndpointOptions EnabledWith(TransportAuthenticationMethods authentication) =>
        new() { Enabled = true, Authentication = authentication };

    private static OAuthValidationOptions OAuthWith(params AuthorizationServerOptions[] authorizationServers)
    {
        var oauth = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" };

        // A loop rather than a projection, because adding to a getter-only collection is a side effect and a pipeline
        // must never be the place one happens.
        foreach (var authorizationServer in authorizationServers)
        {
            oauth.AuthorizationServers.Add(authorizationServer);
        }

        return oauth;
    }

    private static AuthorizationServerOptions AuthorizationServer(string name, string issuer) =>
        new() { Name = name, Issuer = issuer, AuthorizedSubjects = { "9f2c" } };

    private static McpClientCertificateProfileOptions ConnectorProfile()
    {
        var profile = new McpClientCertificateProfileOptions
        {
            Name = "chatgpt-connector",
            Requirement = McpClientCertificateRequirement.Optional,
        };

        profile.TrustAnchors.Add(new ConfiguredSecret
        {
            Name = "openai-connectors-ca",
            SecretReference = "file:/run/secrets/openai-connectors-ca.pem",
        });
        profile.SubjectAlternativeNames.Add("mtls.prod.connectors.openai.com");

        return profile;
    }

    private static ConfiguredSecret Key(string name) => new()
    {
        Name = name,
        SecretReference = "systemd-credential:mailfathom-mcp-workstation-key",
    };
}
