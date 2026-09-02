// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

public sealed class SenderAuthenticationReadingTests
{
    private const string TrustedIdentifier = "mx.example.test";

    private static readonly TrustedAuthenticationAuthority TrustedAuthority = CreateAuthority(TrustedIdentifier);

    /// <summary>A header an attacker wrote upstream must not decide the verdict, whatever it claims.</summary>
    [Fact]
    public void Read_ForgedHeaderBelowTheTrustedOne_ReadsTheTrustedOne()
    {
        // Arrange
        var headers = new[]
        {
            Header(TrustedIdentifier, Dkim("fail", "bank.test")),
            Header("attacker.test", Dkim("pass", "bank.test")),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Failed, authentication.Outcome);
        Assert.Null(authentication.AuthenticatedDomain);
    }

    /// <summary>Only the topmost header of the trusted server counts, because it may have stripped a forgery below it.</summary>
    [Fact]
    public void Read_TrustedServerWroteSeveralHeaders_ReadsTheTopmost()
    {
        // Arrange
        var headers = new[]
        {
            Header(TrustedIdentifier, Dkim("pass", "newsletter.test")),
            Header(TrustedIdentifier, Dkim("pass", "bank.test")),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "news@newsletter.test");

        // Assert
        Assert.Equal("NEWSLETTER.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
    }

    /// <summary>A header carrying no trusted identifier says nothing, which is not the same as saying the sender failed.</summary>
    [Fact]
    public void Read_NoHeaderFromTheTrustedServer_IsNotEstablishedRatherThanFailed()
    {
        // Arrange
        var headers = new[] { Header("someone.else.test", Dkim("pass", "bank.test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
        Assert.Null(authentication.AuthenticatedDomain);
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, authentication.AuthorAuthentication);
    }

    /// <summary>A message with no such header at all is the ordinary case on a provider that publishes nothing.</summary>
    [Fact]
    public void Read_MessageCarriedNoHeaders_IsNotEstablished()
    {
        // Act
        var authentication = SenderAuthenticationReading.Read([], TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
        Assert.Equal("BANK.TEST", authentication.FromDomain?.NormalizedValue);
    }

    /// <summary>An account that names no authority reads no header, rather than believing an arbitrary one.</summary>
    [Fact]
    public void Read_AccountTrustsNoServer_IsNotEstablishedAlthoughAHeaderPassed()
    {
        // Arrange
        var headers = new[] { Header(TrustedIdentifier, Dkim("pass", "bank.test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(
            headers,
            TrustedAuthenticationAuthority.None,
            "alerts@bank.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
        Assert.Null(authentication.AuthenticatedDomain);
    }

    /// <summary>A spoofed author is a transport identity that authenticated as somebody the message does not display.</summary>
    [Fact]
    public void Read_FromClaimsAnotherDomain_NamesTheAuthenticatedOneAndEstablishesNoAuthor()
    {
        // Arrange
        var headers = new[] { Header(TrustedIdentifier, Dkim("pass", "sender-relay.test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, authentication.Outcome);
        Assert.Equal("SENDER-RELAY.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
        Assert.Equal("BANK.TEST", authentication.FromDomain?.NormalizedValue);
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, authentication.AuthorAuthentication);
    }

    /// <summary>A signature over exactly the displayed domain establishes the author without DMARC being involved.</summary>
    [Fact]
    public void Read_DkimDomainIsTheDisplayedOne_EstablishesTheAuthor()
    {
        // Arrange
        var headers = new[] { Header(TrustedIdentifier, Dkim("pass", "Bank.Test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "Alerts@BANK.test");

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal("BANK.TEST", authentication.AuthenticatedAuthorDomain?.NormalizedValue);
    }

    /// <summary>An SPF envelope domain equal to the displayed one establishes the author the same way.</summary>
    [Fact]
    public void Read_SpfDomainIsTheDisplayedOne_EstablishesTheAuthor()
    {
        // Arrange
        var headers = new[] { Header(TrustedIdentifier, Spf("pass", "bounce@bank.test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal("BANK.TEST", authentication.AuthenticatedAuthorDomain?.NormalizedValue);
    }

    /// <summary>A second verified signature is read as well, so the first one cannot hide the author's own.</summary>
    /// <remarks>
    /// This is what a message sent through a delivery provider looks like, and the provider's signature is routinely the
    /// one the receiving server lists first. Reading only that one would leave the author unestablished on a message
    /// whose own domain signed it.
    /// </remarks>
    [Fact]
    public void Read_SeveralVerifiedSignatures_EstablishesTheAuthorFromTheMatchingOne()
    {
        // Arrange
        var headers = new[]
        {
            Header(TrustedIdentifier, Dkim("pass", "provider.test"), Dkim("pass", "bank.test")),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal("PROVIDER.TEST", authentication.DkimDomain?.NormalizedValue);
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal("BANK.TEST", authentication.AuthenticatedAuthorDomain?.NormalizedValue);
    }

    /// <summary>A DMARC failure beside a passing signature of somebody else's is an author that was refused.</summary>
    [Fact]
    public void Read_DmarcFailedOverAnUnrelatedPassingSignature_RefusesTheAuthor()
    {
        // Arrange
        var headers = new[]
        {
            Header(
                TrustedIdentifier,
                Dkim("pass", "attacker.test"),
                new ReportedAuthenticationMethod("dmarc", "fail", [Property("header", "from", "bank.test")])),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, authentication.Outcome);
        Assert.Equal("ATTACKER.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
        Assert.Equal(AuthorAuthenticationOutcome.Failed, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }

    /// <summary>A DMARC pass carries a signing subdomain over the exact comparison this reading would otherwise make.</summary>
    [Fact]
    public void Read_DmarcPassedOverASigningSubdomain_EstablishesTheDisplayedAuthor()
    {
        // Arrange
        var headers = new[]
        {
            Header(
                TrustedIdentifier,
                Dkim("pass", "mail.bank.test"),
                new ReportedAuthenticationMethod("dmarc", "pass", [Property("header", "from", "bank.test")])),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal("BANK.TEST", authentication.AuthenticatedAuthorDomain?.NormalizedValue);
        Assert.Equal("MAIL.BANK.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
    }

    /// <summary>A signing subdomain establishes the displayed author even where no DMARC result interprets it.</summary>
    /// <remarks>
    /// The trusted reading reaches this the same way the local one does, from the two names alone, so a deployment
    /// whose server reports no DMARC result concludes what one reporting <c>pass</c> concludes about the same message.
    /// The established domain stays the displayed one while the identity that authenticated stays the signer.
    /// </remarks>
    [Fact]
    public void Read_SigningSubdomainWithoutDmarc_EstablishesTheDisplayedAuthor()
    {
        // Arrange
        var headers = new[] { Header(TrustedIdentifier, Dkim("pass", "mail.bank.test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, authentication.AuthorAuthentication);
        Assert.Equal("BANK.TEST", authentication.AuthenticatedAuthorDomain?.NormalizedValue);
        Assert.Equal("MAIL.BANK.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
    }

    /// <summary>DKIM is the authoritative identity where both checks produced one, and both are still recorded.</summary>
    [Fact]
    public void Read_DkimAndSpfDisagree_NamesTheDkimDomainAndKeepsBoth()
    {
        // Arrange
        var headers = new[]
        {
            Header(TrustedIdentifier, Dkim("pass", "signer.test"), Spf("pass", "bounce@relay.test")),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "news@signer.test");

        // Assert
        Assert.Equal(SenderAuthenticationMethod.DomainKeysIdentifiedMail, authentication.AuthenticatedBy);
        Assert.Equal("SIGNER.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
        Assert.Equal("SIGNER.TEST", authentication.DkimDomain?.NormalizedValue);
        Assert.Equal("RELAY.TEST", authentication.SpfDomain?.NormalizedValue);
    }

    /// <summary>SPF alone still establishes an identity, and the verdict says which check reached it.</summary>
    [Fact]
    public void Read_OnlySpfPassed_NamesTheEnvelopeDomain()
    {
        // Arrange
        var headers = new[] { Header(TrustedIdentifier, Dkim("fail", "signer.test"), Spf("pass", "bounce@relay.test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "news@relay.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, authentication.Outcome);
        Assert.Equal(SenderAuthenticationMethod.SenderPolicyFramework, authentication.AuthenticatedBy);
        Assert.Equal("RELAY.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
        Assert.Null(authentication.DkimDomain);
    }

    /// <summary>An attempt that did not hold is a failure; a check with nothing to evaluate establishes nothing.</summary>
    [Theory]
    [InlineData("fail", SenderAuthenticationOutcome.Failed)]
    [InlineData("softfail", SenderAuthenticationOutcome.Failed)]
    [InlineData("neutral", SenderAuthenticationOutcome.Failed)]
    [InlineData("policy", SenderAuthenticationOutcome.Failed)]
    [InlineData("temperror", SenderAuthenticationOutcome.Failed)]
    [InlineData("permerror", SenderAuthenticationOutcome.Failed)]
    [InlineData("none", SenderAuthenticationOutcome.NotEstablished)]
    public void Read_TrustedHeaderStatesOneResult_SeparatesAFailureFromSilence(
        string result,
        SenderAuthenticationOutcome expectedOutcome)
    {
        // Arrange
        var headers = new[] { Header(TrustedIdentifier, Spf(result, "bounce@relay.test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "news@relay.test");

        // Assert
        Assert.Equal(expectedOutcome, authentication.Outcome);
    }

    /// <summary>A trusted header naming only methods this reading does not use establishes nothing.</summary>
    [Fact]
    public void Read_TrustedHeaderNamesNoUsableMethod_IsNotEstablished()
    {
        // Arrange
        var headers = new[]
        {
            Header(TrustedIdentifier, new ReportedAuthenticationMethod("iprev", "pass", [])),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "news@relay.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
    }

    /// <summary>A pass naming no usable domain has no identity to record, so nothing is invented for it.</summary>
    [Fact]
    public void Read_PassWithoutAUsableDomain_IsNotEstablished()
    {
        // Arrange
        var headers = new[]
        {
            Header(TrustedIdentifier, new ReportedAuthenticationMethod("dkim", "pass", [])),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "news@relay.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
        Assert.Null(authentication.AuthenticatedDomain);
    }

    /// <summary>The DMARC result the server reported is recorded whether or not an identity was established.</summary>
    [Theory]
    [InlineData("pass", DmarcOutcome.Pass)]
    [InlineData("fail", DmarcOutcome.Fail)]
    [InlineData("none", DmarcOutcome.NoPolicyPublished)]
    [InlineData("temperror", DmarcOutcome.TemporaryError)]
    [InlineData("permerror", DmarcOutcome.PermanentError)]
    [InlineData("something-else", DmarcOutcome.NotReported)]
    public void Read_TrustedHeaderReportsDmarc_RecordsWhatTheServerSaid(string result, DmarcOutcome expected)
    {
        // Arrange
        var headers = new[]
        {
            Header(
                TrustedIdentifier,
                Dkim("pass", "bank.test"),
                new ReportedAuthenticationMethod("dmarc", result, [Property("header", "from", "bank.test")])),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(expected, authentication.Dmarc);
    }

    /// <summary>A message whose sender-authentication failed still carries what DMARC said about the displayed domain.</summary>
    [Fact]
    public void Read_FailedIdentityWithADmarcResult_KeepsTheDmarcResult()
    {
        // Arrange
        var headers = new[]
        {
            Header(
                TrustedIdentifier,
                Dkim("fail", "bank.test"),
                new ReportedAuthenticationMethod("dmarc", "fail", [Property("header", "from", "bank.test")])),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Failed, authentication.Outcome);
        Assert.Equal(DmarcOutcome.Fail, authentication.Dmarc);
    }

    /// <summary>RFC 8601 makes every token case-insensitive, and servers write them both ways.</summary>
    [Fact]
    public void Read_TokensWrittenInAnyCase_AreRecognized()
    {
        // Arrange
        var headers = new[]
        {
            Header(
                "MX.Example.Test",
                new ReportedAuthenticationMethod("DKIM", "PASS", [Property("HEADER", "D", "Bank.Test")])),
        };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, "alerts@bank.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, authentication.Outcome);
        Assert.Equal("BANK.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
    }

    /// <summary>A message that displayed no usable sender has no author to establish, whatever authenticated.</summary>
    [Fact]
    public void Read_MessageDisplayedNoUsableSender_EstablishesNoAuthor()
    {
        // Arrange
        var headers = new[] { Header(TrustedIdentifier, Dkim("pass", "bank.test")) };

        // Act
        var authentication = SenderAuthenticationReading.Read(headers, TrustedAuthority, displayedSenderAddress: null);

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, authentication.Outcome);
        Assert.Null(authentication.FromDomain);
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, authentication.AuthorAuthentication);
        Assert.Null(authentication.AuthenticatedAuthorDomain);
    }

    private static TrustedAuthenticationAuthority CreateAuthority(string identifier)
    {
        TrustedAuthenticationAuthority.TryCreate(identifier, out var authority);

        return authority;
    }

    private static AuthenticationResultsHeader Header(string identifier, params ReportedAuthenticationMethod[] methods) =>
        new(identifier, methods);

    private static ReportedAuthenticationMethod Dkim(string result, string signingDomain) =>
        new("dkim", result, [Property("header", "d", signingDomain)]);

    private static ReportedAuthenticationMethod Spf(string result, string envelopeSender) =>
        new("spf", result, [Property("smtp", "mailfrom", envelopeSender)]);

    private static ReportedAuthenticationProperty Property(string type, string name, string value) =>
        new(type, name, value);
}
