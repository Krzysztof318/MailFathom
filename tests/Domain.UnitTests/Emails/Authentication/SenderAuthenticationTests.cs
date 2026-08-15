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
}
