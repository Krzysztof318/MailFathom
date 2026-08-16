// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

/// <summary>Covers which authors one account recognizes, which half recognized them, and what the answer is stored with.</summary>
public sealed class SenderTrustPolicyTests
{
    /// <summary>Mail one of this deployment's own accounts wrote is recognized wherever it was delivered.</summary>
    [Fact]
    public void Evaluate_AuthorAtAnotherConfiguredAccountsDomain_IsRecognizedAsOwn()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            [DomainOf("work.example"), DomainOf("personal.example")],
            configuredTrustedSenders: [],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(WrittenBy("work.example"), AddressOf("owner@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(SenderTrustSource.OwnAccountDomain, trust.GrantedBy);
    }

    /// <summary>A deployment whose accounts share a provider with everybody says so by supplying no own domains.</summary>
    [Fact]
    public void Evaluate_OwnDomainsWithheld_LeavesTheSameAuthorUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountDomains: [],
            configuredTrustedSenders: [],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(WrittenBy("work.example"), AddressOf("owner@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>An own domain is the account's own name and never the names beneath it.</summary>
    [Fact]
    public void Evaluate_SubdomainOfAnOwnDomain_IsNotRecognizedAsOwn()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([DomainOf("work.example")], [], []);

        // Act
        var trust = policy.Evaluate(WrittenBy("mail.work.example"), displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>An entry in either half recognizes, and the half that did is what the verdict reports.</summary>
    [Theory]
    [InlineData(true, false, SenderTrustSource.ConfiguredTrustedSender)]
    [InlineData(false, true, SenderTrustSource.StoredTrustedSender)]
    [InlineData(true, true, SenderTrustSource.ConfiguredTrustedSender)]
    public void Evaluate_EntryInEitherHalf_RecognizesAndNamesTheHalf(
        bool configured,
        bool stored,
        SenderTrustSource expected)
    {
        // Arrange
        var entry = DomainEntry("partner.example", includeSubdomains: false);
        var policy = SenderTrustPolicy.Create(
            ownAccountDomains: [],
            configured ? [entry] : [],
            stored ? [entry] : []);

        // Act
        var trust = policy.Evaluate(WrittenBy("partner.example"), displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
        Assert.Equal(expected, trust.GrantedBy);
    }

    /// <summary>A displayed author nothing established is never held against the list, however the list names them.</summary>
    /// <remarks>
    /// This is the forgery the whole design is arranged against: anybody can write a trusted correspondent's address
    /// into <c>From</c>, so an entry naming that correspondent must recognize nothing until something other than the
    /// header says the message really came from them.
    /// </remarks>
    [Theory]
    [InlineData(DmarcOutcome.Fail)]
    [InlineData(DmarcOutcome.NotReported)]
    public void Evaluate_TrustedAuthorDisplayedButAuthenticationFailed_LeavesTheMessageUnknown(DmarcOutcome dmarc)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountDomains: [],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Failed(DomainOf("partner.example"), dmarc),
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>A recognized signer cannot vouch for an author it is not, which is what an allow-listed relay would be.</summary>
    /// <remarks>
    /// The message really was signed by a domain the account named, and the domain it displays as its author is not
    /// that one. Anything reading the authenticated identity here would recognize every message the signer ever relays,
    /// whoever it says wrote them.
    /// </remarks>
    [Theory]
    [InlineData(DmarcOutcome.Fail)]
    [InlineData(DmarcOutcome.NotReported)]
    public void Evaluate_TrustedThirdPartySignatureOverAnotherAuthor_LeavesTheMessageUnknown(DmarcOutcome dmarc)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountDomains: [],
            [DomainEntry("relay.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Authenticated(
                DomainOf("relay.example"),
                spfDomain: null,
                DomainOf("bank.example"),
                dmarc),
            AddressOf("security@bank.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>The receiving server's own DMARC result establishes the displayed author whatever signed the message.</summary>
    [Fact]
    public void Evaluate_TrustedAuthorEstablishedByDmarcAlone_RecognizesTheAuthor()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountDomains: [],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Authenticated(
                DomainOf("provider.example"),
                spfDomain: null,
                DomainOf("partner.example"),
                DmarcOutcome.Pass),
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
    }

    /// <summary>Without DMARC, an identity that authenticated as the displayed author establishes them all the same.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Evaluate_IdentityMatchingTheDisplayedAuthorWithoutDmarc_RecognizesTheAuthor(bool throughDkim)
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountDomains: [],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Authenticated(
                throughDkim ? DomainOf("partner.example") : DomainOf("relay.example"),
                throughDkim ? null : DomainOf("partner.example"),
                DomainOf("partner.example"),
                DmarcOutcome.NotReported),
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
    }

    /// <summary>A message whose author was authenticated and whom nobody named is the ordinary answer.</summary>
    [Fact]
    public void Evaluate_AuthenticatedAuthorNobodyNamed_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            [DomainOf("work.example")],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(WrittenBy("stranger.example"), AddressOf("someone@stranger.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>A message nothing was established about is judged against no list at all.</summary>
    [Fact]
    public void Evaluate_NothingEstablished_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([DomainOf("work.example")], [], []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.NotEstablished(DomainOf("work.example")),
            AddressOf("owner@work.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.Equal(SenderTrustSource.None, trust.GrantedBy);
    }

    /// <summary>A message displaying no usable author has nobody to recognize, whatever else authenticated.</summary>
    [Fact]
    public void Evaluate_NoDisplayedAuthorDomain_LeavesTheMessageUnknown()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountDomains: [],
            [DomainEntry("partner.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(
            SenderAuthentication.Authenticated(
                DomainOf("partner.example"),
                spfDomain: null,
                fromDomain: null,
                DmarcOutcome.Pass),
            displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
    }

    /// <summary>An entry written in one encoding of an internationalized name recognizes the other one a message carried.</summary>
    [Fact]
    public void Evaluate_InternationalizedEntryAndAsciiVerdict_RecognizesOneName()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create(
            ownAccountDomains: [],
            [DomainEntry("bücher.example", includeSubdomains: false)],
            storedTrustedSenders: []);

        // Act
        var trust = policy.Evaluate(WrittenBy("xn--bcher-kva.example"), displayedSender: null);

        // Assert
        Assert.Equal(SenderTrustLevel.Trusted, trust.Level);
    }

    /// <summary>Every verdict carries the list it was reached under, so a later change to that list is legible.</summary>
    [Fact]
    public void Evaluate_AnyMessage_CarriesTheRevisionOfThePolicyThatJudgedIt()
    {
        // Arrange
        var policy = SenderTrustPolicy.Create([DomainOf("work.example")], [], []);

        // Act
        var recognized = policy.Evaluate(WrittenBy("work.example"), displayedSender: null);
        var unrecognized = policy.Evaluate(WrittenBy("stranger.example"), displayedSender: null);

        // Assert
        Assert.True(policy.Revision.NamesAPolicy);
        Assert.Equal(policy.Revision, recognized.PolicyRevision);
        Assert.Equal(policy.Revision, unrecognized.PolicyRevision);
    }

    /// <summary>Reordering a list is not a change to it, and adding to either half is.</summary>
    [Fact]
    public void Revision_ListsThatSayTheSameThing_AreOneRevision()
    {
        // Arrange
        var first = DomainEntry("a.example", includeSubdomains: false);
        var second = DomainEntry("b.example", includeSubdomains: true);

        // Act
        var written = SenderTrustPolicy.Create([], [first, second], []).Revision;
        var reordered = SenderTrustPolicy.Create([], [second, first], []).Revision;
        var moved = SenderTrustPolicy.Create([], [first], [second]).Revision;
        var extended = SenderTrustPolicy.Create([], [first, second], [DomainEntry("c.example", false)]).Revision;

        // Assert
        Assert.Equal(written, reordered);
        Assert.NotEqual(written, moved);
        Assert.NotEqual(written, extended);
    }

    /// <summary>A deployment that recognizes nobody names no policy, so its verdicts read as judged by nothing.</summary>
    [Fact]
    public void Revision_PolicyWithNothingToSay_NamesNoPolicy()
    {
        // Act
        var revision = SenderTrustPolicy.RecognizingNobody.Revision;

        // Assert
        Assert.False(revision.NamesAPolicy);
        Assert.Equal(SenderTrustPolicyRevision.None, revision);
    }

    /// <summary>An account this deployment no longer serves recognizes nobody rather than failing.</summary>
    [Fact]
    public void Evaluate_PolicyRecognizingNobody_LeavesEveryAuthorUnknown()
    {
        // Act
        var trust = SenderTrustPolicy.RecognizingNobody.Evaluate(
            WrittenBy("partner.example"),
            AddressOf("alice@partner.example"));

        // Assert
        Assert.Equal(SenderTrustLevel.Unknown, trust.Level);
        Assert.False(trust.PolicyRevision.NamesAPolicy);
    }

    /// <summary>Builds the verdict of a message whose displayed author the receiving server established.</summary>
    private static SenderAuthentication WrittenBy(string domain) =>
        SenderAuthentication.Authenticated(
            DomainOf(domain),
            spfDomain: null,
            DomainOf(domain),
            DmarcOutcome.Pass);

    private static TrustedSenderEntry DomainEntry(string domain, bool includeSubdomains)
    {
        Assert.True(TrustedSenderEntry.TryCreateForDomain(domain, includeSubdomains, out var entry));
        Assert.NotNull(entry);

        return entry;
    }

    private static SenderDomain DomainOf(string written)
    {
        Assert.True(SenderDomain.TryCreate(written, out var domain));

        return domain;
    }

    private static EmailAddress AddressOf(string written)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, written, out var address));

        return address;
    }
}
