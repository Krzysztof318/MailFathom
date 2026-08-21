// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers which entry's scopes a configured issuer is held to.</summary>
/// <remarks>
/// This is where the entries' independence becomes a runtime lookup: the access policy and the insufficient-scope
/// challenge both read the map this composes, keyed by the issuer a validated token carries. A regression attributing
/// every server to the first entry's scopes would judge one tenant's callers against another tenant's requirements
/// without failing anything that hand-builds the resulting dictionary instead of deriving it.
/// </remarks>
public sealed class TransportAuthenticationConfigurationTests
{
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
