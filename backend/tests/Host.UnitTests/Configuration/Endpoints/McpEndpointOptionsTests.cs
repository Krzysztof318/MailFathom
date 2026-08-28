// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Security.ClientCertificates;
using MailFathom.Mcp.Tools.Categories;
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
        Assert.Empty(options.Authentication);
        Assert.False(options.RequiresAuthentication);
        Assert.False(options.AllowsApiKey);
        Assert.Empty(options.OAuthMethods());
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
    public void FindConfigurationErrors_AnEntryAcceptingApiKeys_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(Accepting(OwnerCredentialMethod.ApiKey));

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
        Assert.True(options.AllowsApiKey);
    }

    /// <summary>
    /// An entry states which method is accepted rather than which credential is held, so a second entry accepting one
    /// the endpoint already accepts says nothing the first did not. The credentials themselves are rows, and a second
    /// one is provisioned rather than written here.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_TwoEntriesAcceptingOneMethod_IsRefused()
    {
        // Arrange
        var options = EnabledWith(
            Accepting(OwnerCredentialMethod.ApiKey),
            Accepting(OwnerCredentialMethod.ApiKey));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication:1:Method", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mapped subject resolves one owner, so several entries naming several authorization servers are what a
    /// deployment serving two of them writes — which is why that one method is the exception to the rule above.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_TwoEntriesAcceptingMappedSubjects_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(
            OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom")),
            OAuthMethod(AuthorizationServer("partners", "https://sso.partner.test/realms/mailfathom")));

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

    /// <summary>
    /// An entry naming no method is refused rather than skipped, because a list an operator wrote entries into reads as
    /// an authenticated deployment while an entry stating nothing registers no scheme and requires no credential.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("apikey")]
    public void FindConfigurationErrors_AnEntryNamingNoPublishedMethod_IsRefusedRatherThanIgnored(string? method)
    {
        // Arrange
        var options = EnabledWith(new OwnerFacingAuthenticationOptions { Method = method });

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication:0:Method", error, StringComparison.Ordinal);
    }

    /// <summary>A block belongs to the method it configures, so one written under another entry is a mistake to correct rather than a setting to ignore.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntryCarryingABlockAnotherMethodOwns_IsRefused()
    {
        // Arrange
        var entry = Accepting(OwnerCredentialMethod.ApiKey);
        entry.OAuth = OAuthWith(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));
        var options = EnabledWith(entry);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication:0:OAuth", error, StringComparison.Ordinal);
    }

    /// <summary>The methods identify different kinds of caller, so a deployment reaching both a person and a scheduled job accepts both.</summary>
    [Fact]
    public void FindConfigurationErrors_ApiKeyAndOAuthTogether_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(
            Accepting(OwnerCredentialMethod.ApiKey),
            OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom")));

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
        Assert.True(options.AllowsApiKey);
        Assert.True(options.AllowsOAuth);
    }

    /// <summary>A grant read from a token's scopes is meaningless where no token is presented, so it is refused where it could not be read.</summary>
    [Fact]
    public void FindConfigurationErrors_ScopesNarrowingAGrantOnAMethodCarryingNoToken_IsRefused()
    {
        // Arrange
        var entry = Accepting(OwnerCredentialMethod.ApiKey);
        entry.PermissionsFromTokenScopes = true;
        var options = EnabledWith(entry);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(
            "McpEndpoint:Authentication:0:PermissionsFromTokenScopes",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>An entry accepting mapped subjects and naming no authorization server says nothing about who may speak for the deployment.</summary>
    [Fact]
    public void FindConfigurationErrors_AnEntryAcceptingMappedSubjectsWithNoOAuthBlock_IsRefused()
    {
        // Arrange
        var options = EnabledWith(Accepting(OwnerCredentialMethod.OAuthSubject));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication:0:OAuth", error, StringComparison.Ordinal);
    }

    /// <summary>Each entry states what it asks of the servers it configures, which is what makes two of them independent.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoOAuthEntriesWithTheirOwnScopesAndServers_IsAccepted()
    {
        // Arrange
        var workforce = OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));
        workforce.OAuth!.RequiredScopes.Add("mailfathom.read");
        var partners = OAuthMethod(AuthorizationServer("partners", "https://sso.partner.test/realms/mailfathom"));
        var options = EnabledWith(workforce, partners);

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
        Assert.Equal(2, options.OAuthMethods().Count);
    }

    /// <summary>
    /// The endpoint publishes one protected resource metadata document, at an address derived from the resource, so two
    /// entries naming different resources would leave one of them described by a document its clients never see.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_TwoOAuthEntriesNamingDifferentResources_IsRefused()
    {
        // Arrange
        var workforce = OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));
        var partners = OAuthMethod(AuthorizationServer("partners", "https://sso.partner.test/realms/mailfathom"));
        partners.OAuth!.Resource = "https://other.example.test/mcp";
        var options = EnabledWith(workforce, partners);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication:1:OAuth:Resource", error, StringComparison.Ordinal);
    }

    /// <summary>The scheme a server's validator registers under is composed from its name, so two entries naming one server would collide.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoOAuthEntriesSharingAnAuthorizationServerName_IsRefused()
    {
        // Arrange
        var options = EnabledWith(
            OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom")),
            OAuthMethod(AuthorizationServer("workforce", "https://sso.partner.test/realms/mailfathom")));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(
            "McpEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Name",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>The same rule the entry applies within itself, reaching across two entries that were separately valid.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoOAuthEntriesSharingAnIssuer_IsRefused()
    {
        // Arrange
        var options = EnabledWith(
            OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom")),
            OAuthMethod(AuthorizationServer("partners", "https://sso.example.test/realms/mailfathom")));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(
            "McpEndpoint:Authentication:1:OAuth:AuthorizationServers:0:Issuer",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_OAuthWithNoAuthorizationServer_IsRefusedBecauseNoTokenCouldBeValidated()
    {
        // Arrange
        var options = EnabledWith(OAuthMethod());

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication:0:OAuth:AuthorizationServers", error, StringComparison.Ordinal);
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
        var oauth = OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));
        oauth.OAuth!.Resource = resource;
        var options = EnabledWith(oauth);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication:0:OAuth:Resource", error, StringComparison.Ordinal);
    }

    /// <summary>Two profiles claiming one issuer would leave the key set a token is trusted against decided by configuration order.</summary>
    [Fact]
    public void FindConfigurationErrors_TwoAuthorizationServersSharingAnIssuer_IsRefused()
    {
        // Arrange
        var options = EnabledWith(OAuthMethod(
            AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"),
            AuthorizationServer("partners", "https://sso.example.test/realms/mailfathom")));

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith(
            "McpEndpoint:Authentication:0:OAuth:AuthorizationServers:1:Issuer",
            error,
            StringComparison.Ordinal);
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
        var oauth = OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom"));
        oauth.OAuth!.RequiredScopes.Add(scope);
        var options = EnabledWith(oauth);

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:Authentication:0:OAuth:RequiredScopes:0", error, StringComparison.Ordinal);
    }

    /// <summary>Requiring no scope is the coarser boundary a deployment gets by default: any token this resource's servers issued is enough.</summary>
    [Fact]
    public void FindConfigurationErrors_OAuthWithNoRequiredScope_IsAccepted()
    {
        // Arrange
        var options = EnabledWith(OAuthMethod(AuthorizationServer("workforce", "https://sso.example.test/realms/mailfathom")));

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
    public void FindConfigurationErrors_AnUnusableCorsPolicy_IsReportedUnderTheSectionThatCarriesIt()
    {
        // Arrange
        var options = Enabled();
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
        var options = Enabled();
        options.RateLimiting.MaxConcurrentRequests = 0;

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:RateLimiting:MaxConcurrentRequests", error, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_AnUnusableRequestCeiling_IsReportedUnderTheSectionThatCarriesIt()
    {
        // Arrange
        var options = Enabled();
        options.RequestTimeout.Duration = TimeSpan.Zero;

        // Act
        var error = Assert.Single(options.FindConfigurationErrors());

        // Assert
        Assert.StartsWith("McpEndpoint:RequestTimeout:Duration", error, StringComparison.Ordinal);
    }

    [Fact]
    public void RequestTimeout_WithNothingConfigured_BoundsTheEndpoint()
    {
        // Act
        var options = new McpEndpointOptions();

        // Assert
        // Defaulted like the rate limits, so an endpoint somebody enabled cannot hold a permit indefinitely because
        // nobody wrote a number.
        Assert.True(options.RequestTimeout.Enabled);
        Assert.Equal(new TransportRequestTimeoutOptions().Duration, options.RequestTimeout.Duration);
    }

    /// <summary>
    /// The limits apply to an enabled endpoint whether or not an operator wrote any of them down, so a section nobody
    /// configured has to pass validation rather than demanding numbers the product already has.
    /// </summary>
    [Fact]
    public void FindConfigurationErrors_AnEndpointConfiguringNoLimits_ReportsNothing()
    {
        // Arrange
        var options = Enabled();

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
        var options = EnabledWith(new OwnerFacingAuthenticationOptions());
        options.Cors.AllowedOrigins.Add("not-an-origin");

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        Assert.Equal(2, errors.Count);
    }

    /// <summary>Mutual TLS is off unless a profile says otherwise, and it composes with the mode rather than replacing it.</summary>
    [Fact]
    public void ClientCertificateProfiles_UnconfiguredDeployment_IsEmptyRatherThanNull()
    {
        // Arrange, Act
        var options = Enabled();

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
        var options = Enabled();
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

    private static McpEndpointOptions Enabled() => new() { Enabled = true };

    private static McpEndpointOptions EnabledWith(params OwnerFacingAuthenticationOptions[] methods)
    {
        var options = Enabled();

        // A loop rather than a projection, because adding to a getter-only collection is a side effect and a pipeline
        // must never be the place one happens.
        foreach (var method in methods)
        {
            options.Authentication.Add(method);
        }

        return options;
    }

    private static OwnerFacingAuthenticationOptions Accepting(OwnerCredentialMethod method) =>
        new() { Method = method.Name };

    private static OwnerFacingAuthenticationOptions OAuthMethod(
        params AuthorizationServerOptions[] authorizationServers) =>
        new() { Method = OwnerCredentialMethod.OAuthSubject.Name, OAuth = OAuthWith(authorizationServers) };

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
        new() { Name = name, Issuer = issuer };

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

    /// <summary>The behaviour a deployment has without the setting, which is what keeps its arrival from changing anything.</summary>
    [Fact]
    public void ToPublishedToolCategories_ADeploymentNamingNoCategory_PublishesEveryOneOfThem()
    {
        // Arrange
        var options = new McpEndpointOptions { Enabled = true };

        // Act
        var published = options.ToPublishedToolCategories();

        // Assert
        Assert.Equal(McpToolCategory.All, published.Categories);
    }

    [Fact]
    public void ToPublishedToolCategories_TheCategoriesNamed_PublishesThoseAndNoOther()
    {
        // Arrange
        var options = new McpEndpointOptions { Enabled = true };
        options.PublishedToolCategories.Add("mailbox");
        options.PublishedToolCategories.Add("Contacts");

        // Act
        var published = options.ToPublishedToolCategories();

        // Assert
        Assert.Equal([McpToolCategory.Mailbox, McpToolCategory.Contacts], published.Categories);
        Assert.False(published.Publishes(McpToolCategory.Sending));
    }

    /// <summary>A misspelling would otherwise narrow the endpoint to a name nothing carries, which an operator would read as a listing that lost tools for no reason.</summary>
    [Fact]
    public void FindConfigurationErrors_ACategoryNoToolCarries_IsRefusedAndSaysWhatIsAccepted()
    {
        // Arrange
        var options = new McpEndpointOptions { Enabled = true };
        options.PublishedToolCategories.Add("mailboxes");

        // Act
        var errors = options.FindConfigurationErrors();

        // Assert
        var refusal = Assert.Single(errors);
        Assert.Contains("McpEndpoint:PublishedToolCategories:0", refusal, StringComparison.Ordinal);
        Assert.Contains("mailboxes", refusal, StringComparison.Ordinal);
        Assert.Contains(McpToolCategory.PublishedNames(), refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void FindConfigurationErrors_ThePublishedCategoryNames_AreAccepted()
    {
        // Arrange
        var options = new McpEndpointOptions { Enabled = true };

        foreach (var category in McpToolCategory.All)
        {
            options.PublishedToolCategories.Add(category.Name);
        }

        // Act, Assert
        Assert.Empty(options.FindConfigurationErrors());
    }

}
