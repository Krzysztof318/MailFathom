// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

public sealed class LocalSenderAuthenticationReadingTests
{
    /// <summary>A signature that verified names the domain it was made for, as a cryptographic identity.</summary>
    [Fact]
    public void Read_AVerifiedSignature_AuthenticatesItsSigningDomain()
    {
        // Arrange
        var signingDomain = DomainOf("signer.example.test");

        // Act
        var verdict = LocalSenderAuthenticationReading.Read([signingDomain], anySignatureRejected: false, "anna@signer.example.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, verdict.Outcome);
        Assert.Equal(SenderAuthenticationMethod.DomainKeysIdentifiedMail, verdict.AuthenticatedBy);
        Assert.Equal(signingDomain, verdict.AuthenticatedDomain);
        Assert.Equal(signingDomain, verdict.DkimDomain);
    }

    /// <summary>Every locally reached verdict says so, whatever it establishes, because that is what a reader weighs it by.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Read_AnyOutcome_NamesLocalVerificationAsTheSource(bool anySignatureRejected)
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read([], anySignatureRejected, "anna@example.test");

        // Assert
        Assert.Equal(SenderAuthenticationSource.LocalVerification, verdict.Source);
    }

    /// <summary>Neither half of what an envelope carried survives delivery, so a local verdict names neither.</summary>
    [Fact]
    public void Read_AVerifiedSignature_NamesNoSpfIdentityAndReportsNoDmarcResult()
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read(
            [DomainOf("signer.example.test")],
            anySignatureRejected: false,
            "anna@signer.example.test");

        // Assert
        Assert.Null(verdict.SpfDomain);
        Assert.Equal(DmarcOutcome.NotReported, verdict.Dmarc);
    }

    /// <summary>The author is established where a verified signature's domain is exactly the displayed one.</summary>
    [Fact]
    public void Read_ASignatureFromTheDisplayedDomain_EstablishesTheAuthor()
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read(
            [DomainOf("signer.example.test")],
            anySignatureRejected: false,
            "anna@signer.example.test");

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, verdict.AuthorAuthentication);
        Assert.Equal("SIGNER.EXAMPLE.TEST", verdict.AuthenticatedAuthorDomain?.NormalizedValue);
    }

    /// <summary>A signing subdomain establishes nothing about the author, because no DMARC result says whether it may.</summary>
    [Fact]
    public void Read_ASignatureFromASubdomainOfTheDisplayedOne_LeavesTheAuthorUnestablished()
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read(
            [DomainOf("mail.signer.example.test")],
            anySignatureRejected: false,
            "anna@signer.example.test");

        // Assert
        Assert.Equal(AuthorAuthenticationOutcome.NotEstablished, verdict.AuthorAuthentication);
        Assert.Null(verdict.AuthenticatedAuthorDomain);
    }

    /// <summary>A provider's signature listed first never hides the author's own, so every verified domain is considered.</summary>
    [Fact]
    public void Read_SeveralVerifiedSignatures_EstablishesTheAuthorFromAnyOfThem()
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read(
            [DomainOf("relay.example.test"), DomainOf("bank.example.test")],
            anySignatureRejected: false,
            "anna@bank.example.test");

        // Assert
        Assert.Equal("RELAY.EXAMPLE.TEST", verdict.AuthenticatedDomain?.NormalizedValue);
        Assert.Equal(AuthorAuthenticationOutcome.Authenticated, verdict.AuthorAuthentication);
        Assert.Equal("BANK.EXAMPLE.TEST", verdict.AuthenticatedAuthorDomain?.NormalizedValue);
    }

    /// <summary>A signature checked against a resolved key and rejected is a failure, which is more than silence.</summary>
    [Fact]
    public void Read_ASignatureCheckedAndRejected_RecordsAFailure()
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read([], anySignatureRejected: true, "anna@signer.example.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.Failed, verdict.Outcome);
        Assert.Null(verdict.AuthenticatedDomain);
    }

    /// <summary>Nothing checked is not a failure: an unreachable resolver says nothing whatever about the sender.</summary>
    [Fact]
    public void Read_NothingCheckedAtAll_LeavesTheVerdictNotEstablished()
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read([], anySignatureRejected: false, "anna@signer.example.test");

        // Assert
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, verdict.Outcome);
        Assert.Equal(SenderAuthenticationMethod.None, verdict.AuthenticatedBy);
    }

    /// <summary>The displayed domain is recorded whether or not anything held, in the one form everything compares on.</summary>
    [Fact]
    public void Read_AMessageDisplayingAnAuthor_RecordsTheDisplayedDomain()
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read([], anySignatureRejected: true, "anna@Bank.Example.Test");

        // Assert
        Assert.Equal("BANK.EXAMPLE.TEST", verdict.FromDomain?.NormalizedValue);
    }

    /// <summary>A message writing no usable author records none rather than one invented from anything else.</summary>
    [Fact]
    public void Read_AMessageWritingNoUsableAuthor_RecordsNoDisplayedDomain()
    {
        // Act
        var verdict = LocalSenderAuthenticationReading.Read([], anySignatureRejected: false, displayedSenderAddress: null);

        // Assert
        Assert.Null(verdict.FromDomain);
    }

    /// <summary>The reading refuses a null collection rather than treating it as nothing verified.</summary>
    [Fact]
    public void Read_NullVerifiedDomains_Throws()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            LocalSenderAuthenticationReading.Read(null!, anySignatureRejected: false, "anna@example.test"));
    }

    private static SenderDomain DomainOf(string value)
    {
        Assert.True(SenderDomain.TryCreate(value, out var domain));

        return domain;
    }
}
