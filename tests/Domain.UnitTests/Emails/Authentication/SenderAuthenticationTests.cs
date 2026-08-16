// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

public sealed class SenderAuthenticationTests
{
    /// <summary>An authenticated verdict names the domain that authenticated, so one without a domain is not a verdict.</summary>
    [Fact]
    public void Authenticated_NeitherCheckProducedADomain_IsRefused()
    {
        // Act
        var creating = () => SenderAuthentication.Authenticated(
            dkimDomain: null,
            spfDomain: null,
            fromDomain: null,
            DmarcOutcome.NotReported);

        // Assert
        Assert.Throws<ArgumentException>(creating);
    }

    /// <summary>Not established is a verdict of its own, and it carries neither an identity nor an alignment.</summary>
    [Fact]
    public void NotEstablished_WithoutAnything_CarriesNoIdentity()
    {
        // Act
        var authentication = SenderAuthentication.NotEstablished();

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
        Assert.Equal(SenderAuthenticationMethod.None, authentication.AuthenticatedBy);
        Assert.Equal(DmarcOutcome.NotReported, authentication.Dmarc);
        Assert.Equal(SenderDomainAlignment.NotAssessed, authentication.Alignment);
        Assert.Null(authentication.AuthenticatedDomain);
        Assert.Null(authentication.DkimDomain);
        Assert.Null(authentication.SpfDomain);
        Assert.Null(authentication.FromDomain);
    }

    /// <summary>A failure names no identity either, which is what keeps it readable as a check that did not hold.</summary>
    [Fact]
    public void Failed_WithADisplayedSender_NamesNoAuthenticatedDomain()
    {
        // Arrange
        SenderDomain.TryCreate("bank.test", out var fromDomain);

        // Act
        var authentication = SenderAuthentication.Failed(fromDomain, DmarcOutcome.Fail);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Failed, authentication.Outcome);
        Assert.Null(authentication.AuthenticatedDomain);
        Assert.Equal("BANK.TEST", authentication.FromDomain?.NormalizedValue);
        Assert.Equal(DmarcOutcome.Fail, authentication.Dmarc);
    }

    /// <summary>A trusted DMARC pass is the receiving server's own statement about the displayed author.</summary>
    [Fact]
    public void AuthenticatedAuthorDomain_DmarcPassOverAnUnrelatedSignature_IsTheDisplayedDomain()
    {
        // Arrange
        SenderDomain.TryCreate("provider.test", out var signer);
        SenderDomain.TryCreate("partner.test", out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            signer,
            spfDomain: null,
            displayed,
            DmarcOutcome.Pass);

        // Assert
        Assert.Equal(displayed, authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>Without DMARC, an identity that authenticated as the displayed domain establishes the author.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AuthenticatedAuthorDomain_IdentityEqualToTheDisplayedDomain_IsThatDomain(bool throughDkim)
    {
        // Arrange
        SenderDomain.TryCreate("partner.test", out var displayed);
        SenderDomain.TryCreate("relay.test", out var relay);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            throughDkim ? displayed : relay,
            throughDkim ? null : displayed,
            displayed,
            DmarcOutcome.NotReported);

        // Assert
        Assert.Equal(displayed, authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>An identity belonging to somebody other than the displayed author establishes no author at all.</summary>
    [Theory]
    [InlineData(DmarcOutcome.Fail)]
    [InlineData(DmarcOutcome.NotReported)]
    public void AuthenticatedAuthorDomain_IdentityUnrelatedToTheDisplayedDomain_IsAbsent(DmarcOutcome dmarc)
    {
        // Arrange
        SenderDomain.TryCreate("relay.test", out var relay);
        SenderDomain.TryCreate("bank.test", out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated(relay, spfDomain: null, displayed, dmarc);

        // Assert
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>A verdict that establishes nothing, and one that refused an identity, establish no author either.</summary>
    [Fact]
    public void AuthenticatedAuthorDomain_NothingEstablishedOrRefused_IsAbsent()
    {
        // Arrange
        SenderDomain.TryCreate("bank.test", out var displayed);

        // Act
        var unestablished = SenderAuthentication.NotEstablished(displayed);
        var refused = SenderAuthentication.Failed(displayed, DmarcOutcome.Fail);

        // Assert
        Assert.Null(unestablished.AuthenticatedAuthorDomain);
        Assert.Null(refused.AuthenticatedAuthorDomain);
    }

    /// <summary>A message displaying no usable domain has no author to establish, whatever authenticated.</summary>
    [Fact]
    public void AuthenticatedAuthorDomain_NoDisplayedDomain_IsAbsent()
    {
        // Arrange
        SenderDomain.TryCreate("partner.test", out var signer);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            signer,
            spfDomain: null,
            fromDomain: null,
            DmarcOutcome.Pass);

        // Assert
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }
}
