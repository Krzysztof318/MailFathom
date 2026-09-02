// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Infrastructure.Secrets.Discovery;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers what the section reads across every entry at once: which issuer is held to which scopes, and which entry accepts a password.</summary>
/// <remarks>
/// This is where the entries' independence becomes a runtime lookup: the access policy and the insufficient-scope
/// challenge both read the map this composes, keyed by the issuer a validated token carries. A regression attributing
/// every server to the first entry's scopes would judge one tenant's callers against another tenant's requirements
/// without failing anything that hand-builds the resulting dictionary instead of deriving it.
/// </remarks>
public sealed class TransportAuthenticationConfigurationTests
{
    private const string SectionName = "McpEndpoint";

    private const string Resource = "https://mail.example.test/mcp";

    private const string WorkforceIssuer = "https://sso.example.test/realms/mailfathom";

    private const string PartnerIssuer = "https://sso.partner.test/realms/mailfathom";

    [Fact]
    public void RequiredScopesByIssuer_TwoEntriesAskingForDifferentScopes_HoldsEachIssuerToTheScopesOfItsOwnEntry()
    {
        // Arrange
        var workforce = EntryRequiring(["mailfathom.read"], (WorkforceIssuer, "workforce"));
        var partners = EntryRequiring(["partners.read"], (PartnerIssuer, "partners"));

        // Act
        var requiredScopes = TransportAuthenticationConfiguration.RequiredScopesByIssuer([workforce, partners]);

        // Assert
        Assert.Equal(["mailfathom.read"], requiredScopes[WorkforceIssuer]);
        Assert.Equal(["partners.read"], requiredScopes[PartnerIssuer]);
    }

    /// <summary>
    /// An entry states its scopes once for every server it trusts, so both issuers carry all of them. Pairing the two
    /// lists off against each other instead would leave the second server asking for nothing.
    /// </summary>
    [Fact]
    public void RequiredScopesByIssuer_OneEntryTrustingTwoServers_HoldsBothIssuersToTheScopesOfThatEntry()
    {
        // Arrange
        var federated = EntryRequiring(
            ["mailfathom.read", "mailfathom.send"],
            (WorkforceIssuer, "workforce"),
            (PartnerIssuer, "partners"));

        // Act
        var requiredScopes = TransportAuthenticationConfiguration.RequiredScopesByIssuer([federated]);

        // Assert
        Assert.Equal(["mailfathom.read", "mailfathom.send"], requiredScopes[WorkforceIssuer]);
        Assert.Equal(["mailfathom.read", "mailfathom.send"], requiredScopes[PartnerIssuer]);
    }

    /// <summary>
    /// An entry asking for no scope is present with nothing required rather than absent, because absence is what the
    /// challenge reads as an issuer this deployment configures no entry for.
    /// </summary>
    [Fact]
    public void RequiredScopesByIssuer_AnEntryAskingForNoScope_KeepsItsIssuerWithNothingRequired()
    {
        // Arrange
        var anySignedInUser = EntryRequiring([], (WorkforceIssuer, "workforce"));

        // Act
        var requiredScopes = TransportAuthenticationConfiguration.RequiredScopesByIssuer([anySignedInUser]);

        // Assert
        Assert.Empty(requiredScopes[WorkforceIssuer]);
    }

    /// <summary>The lookup happens against a token's own <c>iss</c>, so the key is the form the identity records rather than what the operator typed around it.</summary>
    [Fact]
    public void RequiredScopesByIssuer_AnIssuerWrittenWithSurroundingSpace_IsKeyedByTheFormATokenIsComparedAgainst()
    {
        // Arrange
        var workforce = EntryRequiring(["mailfathom.read"], ($"  {WorkforceIssuer}  ", "workforce"));

        // Act
        var requiredScopes = TransportAuthenticationConfiguration.RequiredScopesByIssuer([workforce]);

        // Assert
        Assert.Equal([WorkforceIssuer], requiredScopes.Keys);
    }

    [Fact]
    public void BasicMethodIn_NoEntryCarryingAPasswordBlock_ReportsNone()
    {
        // Arrange
        TransportAuthenticationOptions[] methods = [new() { ApiKey = AnApiKey() }];

        // Act, Assert
        Assert.Null(TransportAuthenticationConfiguration.BasicMethodIn(methods));
    }

    /// <summary>The scheme is registered from the entry that carries the block, so it is the entry the read has to hand back rather than the block alone.</summary>
    [Fact]
    public void BasicMethodIn_AnEntryCarryingAPasswordBlock_ReportsThatEntry()
    {
        // Arrange
        var basic = new TransportAuthenticationOptions { Basic = new BasicAuthenticationOptions() };
        TransportAuthenticationOptions[] methods = [new() { ApiKey = AnApiKey() }, basic];

        // Act, Assert
        Assert.Same(basic, TransportAuthenticationConfiguration.BasicMethodIn(methods));
    }

    [Fact]
    public void FindConfigurationErrors_OneEntryCarryingAPasswordBlock_ReportsNothing()
    {
        // Arrange
        TransportAuthenticationOptions[] methods =
        [
            new() { ApiKey = AnApiKey() },
            new() { Basic = new BasicAuthenticationOptions() },
        ];

        // Act, Assert
        Assert.Empty(TransportAuthenticationConfiguration.FindConfigurationErrors(SectionName, methods, ProtectedSurface.Mail));
    }

    /// <summary>A password names a credential the deployment provisioned rather than an entry, so a second block would leave the grant decided by configuration order.</summary>
    [Fact]
    public void FindConfigurationErrors_ASecondEntryCarryingAPasswordBlock_IsRefusedNamingTheLaterEntry()
    {
        // Arrange
        TransportAuthenticationOptions[] methods =
        [
            new() { Basic = new BasicAuthenticationOptions() },
            new() { Basic = new BasicAuthenticationOptions() },
        ];

        // Act
        var errors = TransportAuthenticationConfiguration.FindConfigurationErrors(SectionName, methods, ProtectedSurface.Mail);

        // Assert
        var reported = Assert.Single(errors);
        Assert.Contains($"{SectionName}:Authentication:1:{nameof(TransportAuthenticationOptions.Basic)}", reported, StringComparison.Ordinal);
    }

    private static ConfiguredSecret AnApiKey(string name = "workstation") =>
        new() { Name = name, SecretReference = "plaintext:a-key" };

    private static OAuthValidationOptions EntryRequiring(
        IReadOnlyList<string> requiredScopes,
        params (string Issuer, string Name)[] authorizationServers)
    {
        var oauth = new OAuthValidationOptions { Resource = Resource };

        // Loops rather than projections, because adding to a getter-only collection is a side effect and a pipeline
        // must never be the place one happens.
        foreach (var requiredScope in requiredScopes)
        {
            oauth.RequiredScopes.Add(requiredScope);
        }

        foreach (var (issuer, name) in authorizationServers)
        {
            oauth.AuthorizationServers.Add(new AuthorizationServerOptions { Name = name, Issuer = issuer });
        }

        return oauth;
    }
}
