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
            dkimDomains: [],
            spfDomains: [],
            fromDomain: null,
            DmarcOutcome.NotReported);

        // Assert
        Assert.Throws<ArgumentException>(creating);
    }

    /// <summary>Not established is a verdict of its own, and it carries neither an identity nor an author.</summary>
    [Fact]
    public void NotEstablished_WithoutAnything_CarriesNoIdentity()
    {
        // Act
        var authentication = SenderAuthentication.NotEstablished();

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
        Assert.Equal(SenderAuthenticationMethod.None, authentication.AuthenticatedBy);
        Assert.Equal(DmarcOutcome.NotReported, authentication.Dmarc);
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedDomain);
        Assert.Null(authentication.DkimDomain);
        Assert.Null(authentication.SpfDomain);
        Assert.Null(authentication.FromDomain);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
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
    /// <remarks>
    /// The signature belongs to somebody else and a subdomain of the displayed author is not the displayed author, so
    /// neither of them would establish anything on its own. What establishes the author here is the receiving server
    /// having evaluated the sender's published policy, which is a thing MailFathom never does for itself.
    /// </remarks>
    [Theory]
    [InlineData("provider.test")]
    [InlineData("mail.partner.test")]
    public void AuthorAuthentication_DmarcPassOverAnIdentityThatIsNotTheDisplayedDomain_IsAuthenticated(string signer)
    {
        // Arrange
        SenderDomain.TryCreate(signer, out var signingDomain);
        SenderDomain.TryCreate("partner.test", out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            [signingDomain],
            spfDomains: [],
            displayed,
            DmarcOutcome.Pass);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal(displayed, authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>Where DMARC decided nothing, an identity that authenticated as the displayed domain establishes the author.</summary>
    /// <remarks>
    /// Both results that decided nothing are covered, because they are different facts about the receiving server: one
    /// evaluation never ran and the other ran and found no policy to apply. Neither is a statement about the author, so
    /// neither may close the route that most mail actually arrives by.
    /// </remarks>
    [Theory]
    [InlineData(true, DmarcOutcome.NotReported)]
    [InlineData(false, DmarcOutcome.NotReported)]
    [InlineData(true, DmarcOutcome.NoPolicyPublished)]
    [InlineData(false, DmarcOutcome.NoPolicyPublished)]
    public void AuthorAuthentication_IdentityEqualToTheDisplayedDomain_IsAuthenticated(
        bool throughDkim,
        DmarcOutcome dmarc)
    {
        // Arrange
        SenderDomain.TryCreate("partner.test", out var displayed);
        SenderDomain.TryCreate("relay.test", out var relay);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            throughDkim ? [displayed] : [relay],
            throughDkim ? [] : [displayed],
            displayed,
            dmarc);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal(displayed, authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>One unrelated passing signature must not hide another that authenticated the displayed author.</summary>
    /// <remarks>
    /// A message sent through a delivery provider carries the provider's signature beside its author's, and which of
    /// them a receiving server lists first is the server's own ordering. Reading only the first would leave ordinary
    /// mail with no established author, while establishing nothing an attacker could not arrange for themselves.
    /// </remarks>
    [Fact]
    public void AuthorAuthentication_SeveralSignaturesWhereOneIsTheDisplayedDomain_IsAuthenticated()
    {
        // Arrange
        SenderDomain.TryCreate("provider.test", out var provider);
        SenderDomain.TryCreate("partner.test", out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            [provider, displayed],
            spfDomains: [],
            displayed,
            DmarcOutcome.NotReported);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal(displayed, authentication.AuthenticatedAuthorDomain);
        Assert.Equal(provider, authentication.DkimDomain);
        Assert.Equal(provider, authentication.AuthenticatedDomain);
    }

    /// <summary>A DMARC failure ends the question, so an identity equal to the displayed domain does not reopen it.</summary>
    /// <remarks>
    /// The receiving server reached that result with the displayed domain's own published policy in hand, which is more
    /// than an identity comparison made here has. Treating the comparison as the stronger of the two would let a message
    /// the author's own domain disowned establish that author.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AuthorAuthentication_IdentityEqualToTheDisplayedDomainButDmarcFailed_IsFailed(bool throughDkim)
    {
        // Arrange
        SenderDomain.TryCreate("partner.test", out var displayed);
        SenderDomain.TryCreate("relay.test", out var relay);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            throughDkim ? [displayed] : [relay],
            throughDkim ? [] : [displayed],
            displayed,
            DmarcOutcome.Fail);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Failed, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>A message whose transport identity authenticated as somebody else has a spoofed author, not a failed one.</summary>
    /// <remarks>
    /// The two DMARC results that decided nothing are what separate this from a refusal. Without the sender's published
    /// policy, an identity belonging to somebody else says only that this reading cannot establish the author — calling
    /// it a failure would report a refusal the receiving server never made, and the two are acted on differently.
    /// </remarks>
    [Theory]
    [InlineData(DmarcOutcome.NotReported)]
    [InlineData(DmarcOutcome.NoPolicyPublished)]
    [InlineData(DmarcOutcome.TemporaryError)]
    [InlineData(DmarcOutcome.PermanentError)]
    public void AuthorAuthentication_IdentityUnrelatedToTheDisplayedDomain_IsNotEstablished(DmarcOutcome dmarc)
    {
        // Arrange
        SenderDomain.TryCreate("attacker.test", out var attacker);
        SenderDomain.TryCreate("bank.test", out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated([attacker], [attacker], displayed, dmarc);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, authentication.Outcome);
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
        Assert.Equal(attacker, authentication.AuthenticatedDomain);
    }

    /// <summary>Without a usable DMARC result, an identity one within the other establishes the displayed author.</summary>
    /// <remarks>
    /// Both directions are the same fact about the naming tree: a zone was delegated, downwards, by whoever holds the
    /// name above it. What is established stays the displayed domain rather than the signing one, because the
    /// trusted-sender list is held against what the reader is shown.
    /// </remarks>
    [Theory]
    [InlineData("mail.partner.test", "partner.test")]
    [InlineData("p1.mail.partner.test", "partner.test")]
    [InlineData("partner.test", "notice.partner.test")]
    public void AuthorAuthentication_IdentityWithinTheDisplayedDomainsBranch_IsAuthenticated(
        string signingDomain,
        string displayedDomain)
    {
        // Arrange
        SenderDomain.TryCreate(signingDomain, out var signer);
        SenderDomain.TryCreate(displayedDomain, out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            [signer],
            spfDomains: [],
            displayed,
            DmarcOutcome.NotReported);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal(displayed, authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>Two names under one parent are unrelated, whichever of them signed the message.</summary>
    /// <remarks>
    /// This is the line between a delegation and DMARC's relaxed alignment. Calling siblings aligned needs the
    /// organizational domain they share, which needs a public suffix list — neither of which exists here, and a shared
    /// parent is no evidence that either name was ever permitted to speak for the other.
    /// </remarks>
    [Fact]
    public void AuthorAuthentication_SiblingOfTheDisplayedDomain_IsNotEstablished()
    {
        // Arrange
        SenderDomain.TryCreate("a.partner.test", out var signer);
        SenderDomain.TryCreate("b.partner.test", out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            [signer],
            spfDomains: [],
            displayed,
            DmarcOutcome.NotReported);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>A name that merely ends in the displayed one as text is a different domain and establishes nothing.</summary>
    /// <remarks>
    /// The widened comparison is on whole labels, so <c>notpartner.test</c> is somebody else's registration rather than
    /// a zone <c>partner.test</c> delegated. Anything less would hand every displayed domain to whoever registers a
    /// name ending in its characters.
    /// </remarks>
    [Fact]
    public void AuthorAuthentication_IdentitySharingOnlyASuffixOfCharacters_IsNotEstablished()
    {
        // Arrange
        SenderDomain.TryCreate("notpartner.test", out var signer);
        SenderDomain.TryCreate("partner.test", out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            [signer],
            spfDomains: [],
            displayed,
            DmarcOutcome.NotReported);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>A DMARC failure ends the question before the widened comparison is reached.</summary>
    /// <remarks>
    /// The receiving server refused the displayed domain under that domain's own published policy, which outranks a
    /// delegation this reading inferred from two names. Widening the comparison must not reopen a question DMARC closed.
    /// </remarks>
    [Fact]
    public void AuthorAuthentication_IdentityWithinTheDisplayedDomainsBranchButDmarcFailed_IsFailed()
    {
        // Arrange
        SenderDomain.TryCreate("mail.partner.test", out var signer);
        SenderDomain.TryCreate("partner.test", out var displayed);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            [signer],
            spfDomains: [],
            displayed,
            DmarcOutcome.Fail);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Failed, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>A verdict that establishes nothing establishes no author either, unless DMARC spoke for itself.</summary>
    [Fact]
    public void AuthorAuthentication_NothingEstablished_FollowsWhatDmarcSaid()
    {
        // Arrange
        SenderDomain.TryCreate("bank.test", out var displayed);

        // Act
        var silent = SenderAuthentication.NotEstablished(displayed);
        var refused = SenderAuthentication.Failed(displayed, DmarcOutcome.Fail);
        var vouched = SenderAuthentication.NotEstablished(displayed, DmarcOutcome.Pass);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, silent.AuthorAuthentication);
        Assert.Equal(AuthorAuthenticationOutcome.Failed, refused.AuthorAuthentication);
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, vouched.AuthorAuthentication);
        Assert.Equal(displayed, vouched.AuthenticatedAuthorDomain);
    }

    /// <summary>A message displaying no usable domain has no author to authenticate, whatever else passed.</summary>
    [Fact]
    public void AuthorAuthentication_NoDisplayedDomain_IsNotEstablished()
    {
        // Arrange
        SenderDomain.TryCreate("partner.test", out var signer);

        // Act
        var authentication = SenderAuthentication.Authenticated(
            [signer],
            spfDomains: [],
            fromDomain: null,
            DmarcOutcome.Pass);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>A DMARC failure is the server's own refusal and stands whether or not a displayed domain could be read.</summary>
    [Fact]
    public void AuthorAuthentication_NoDisplayedDomainAndDmarcFailed_IsFailed()
    {
        // Act
        var authentication = SenderAuthentication.Failed(fromDomain: null, DmarcOutcome.Fail);

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Failed, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }
}
