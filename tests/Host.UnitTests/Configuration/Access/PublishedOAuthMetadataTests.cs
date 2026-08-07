// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Access;

/// <summary>Covers what a surface's configured OAuth entries publish about themselves between them.</summary>
/// <remarks>
/// Both protected surfaces publish an RFC 9728 document — the MCP endpoint through the protocol SDK's type, the
/// administrative endpoint through a record of this repository's own — and this is the one place that decides what goes
/// in either. Covering it here is what makes both publishers correct by the same test, rather than one of them being a
/// second implementation nothing reaches.
/// </remarks>
public sealed class PublishedOAuthMetadataTests
{
    private const string Resource = "https://mail.example.test/mcp";

    private const string WorkforceIssuer = "https://sso.example.test/realms/mailfathom";

    private const string PartnerIssuer = "https://sso.partner.test/realms/mailfathom";

    [Fact]
    public void For_OneEntry_PublishesThatEntrysResourceIssuerAndScopes()
    {
        // Arrange
        var workforce = EntryFor(WorkforceIssuer, "workforce", "mailfathom.read");

        // Act
        var published = PublishedOAuthMetadata.For([workforce]);

        // Assert
        Assert.Equal(Resource, published.Resource);
        Assert.Equal([WorkforceIssuer], published.AuthorizationServers);
        Assert.Equal(["mailfathom.read"], published.ScopesSupported);
    }

    /// <summary>
    /// A client reads this to find out where to authorize and what to ask for, so a second entry's authorization server
    /// has to appear or its clients discover nothing about it. Reading only the first entry is the regression this pins.
    /// </summary>
    [Fact]
    public void For_SeveralEntries_PublishesEveryIssuerBetweenThem()
    {
        // Arrange
        var workforce = EntryFor(WorkforceIssuer, "workforce", "mailfathom.read");
        var partners = EntryFor(PartnerIssuer, "partners", "partners.read");

        // Act
        var published = PublishedOAuthMetadata.For([workforce, partners]);

        // Assert
        Assert.Equal([WorkforceIssuer, PartnerIssuer], published.AuthorizationServers);
    }

    /// <summary>The document lists what this resource supports, so a scope two entries both ask for is named once rather than twice.</summary>
    [Fact]
    public void For_SeveralEntriesSharingAScope_PublishesThatScopeOnce()
    {
        // Arrange
        var workforce = EntryFor(WorkforceIssuer, "workforce", "mailfathom.read");
        var partners = EntryFor(PartnerIssuer, "partners", "mailfathom.read", "partners.read");

        // Act
        var published = PublishedOAuthMetadata.For([workforce, partners]);

        // Assert
        Assert.Equal(["mailfathom.read", "partners.read"], published.ScopesSupported);
    }

    /// <summary>An entry asking for no scope contributes none, rather than contributing an empty one a client would ask for.</summary>
    [Fact]
    public void For_AnEntryRequiringNoScope_ContributesNoScope()
    {
        // Arrange
        var workforce = EntryFor(WorkforceIssuer, "workforce", "mailfathom.read");
        var partners = EntryFor(PartnerIssuer, "partners");

        // Act
        var published = PublishedOAuthMetadata.For([workforce, partners]);

        // Assert
        Assert.Equal(["mailfathom.read"], published.ScopesSupported);
        Assert.Equal([WorkforceIssuer, PartnerIssuer], published.AuthorizationServers);
    }

    /// <summary>A surface accepting no token publishes no document, so composing one from nothing is a fault rather than an empty answer.</summary>
    [Fact]
    public void For_NoEntryAtAll_IsRefusedRatherThanPublishingAnEmptyDocument() =>
        Assert.Throws<ArgumentException>(() => PublishedOAuthMetadata.For([]));

    private static OAuthValidationOptions EntryFor(string issuer, string name, params string[] requiredScopes)
    {
        var oauth = new OAuthValidationOptions { Resource = Resource };

        // A loop rather than a projection, because adding to a getter-only collection is a side effect and a pipeline
        // must never be the place one happens.
        foreach (var requiredScope in requiredScopes)
        {
            oauth.RequiredScopes.Add(requiredScope);
        }

        oauth.AuthorizationServers.Add(new AuthorizationServerOptions { Name = name, Issuer = issuer });

        return oauth;
    }
}
