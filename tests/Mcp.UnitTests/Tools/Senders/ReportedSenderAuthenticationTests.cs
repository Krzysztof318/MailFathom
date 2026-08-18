// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Mcp.Tools.Senders;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools.Senders;

/// <summary>What the boundary publishes for the evidence an email's author conclusion was reached from.</summary>
public sealed class ReportedSenderAuthenticationTests
{
    /// <summary>An email whose authenticated domain differs from the displayed one publishes both, side by side.</summary>
    /// <remarks>
    /// Both domains are published in the comparison form the row holds, and neither is dropped for differing from the
    /// other. Nothing here restates the difference as an alignment flag, deliberately: the authenticated domain is
    /// whichever identity authenticated the transport, so mail a provider relayed and signed as itself differs here
    /// while being authenticated exactly as it appears, and the verdict is what answers that question.
    /// </remarks>
    [Fact]
    public void From_AuthenticatedDomainDifferingFromTheDisplayedOne_PublishesBoth()
    {
        // Arrange
        var evidence = new SenderAuthenticationEvidence
        {
            AuthenticatedDomain = DomainOf("delivery.example.test"),
            DisplayedAuthorDomain = DomainOf("bank.example.test"),
            AuthenticatedBy = SenderAuthenticationMethod.DomainKeysIdentifiedMail,
            Dmarc = DmarcOutcome.NotReported,
            Source = SenderAuthenticationSource.ReceivingServer,
        };

        // Act
        var published = ReportedSenderAuthentication.From(evidence);

        // Assert
        Assert.Equal("DELIVERY.EXAMPLE.TEST", published.AuthenticatedDomain);
        Assert.Equal("BANK.EXAMPLE.TEST", published.DisplayedAuthorDomain);
    }

    /// <summary>An email nothing authenticated names no authenticated domain, which is an outcome rather than a gap.</summary>
    [Fact]
    public void From_EvidenceNothingWasReadFor_PublishesAbsentDomainsAndNoCheck()
    {
        // Act
        var published = ReportedSenderAuthentication.From(SenderAuthenticationEvidence.None);

        // Assert
        Assert.Null(published.AuthenticatedDomain);
        Assert.Null(published.DisplayedAuthorDomain);
        Assert.Equal("None", published.AuthenticatedBy.ToString());
        Assert.Equal("NotReported", published.Dmarc.ToString());
    }

    /// <summary>Each check is published under the name a reader of a mail header already knows it by.</summary>
    [Theory]
    [InlineData(SenderAuthenticationMethod.None, "None")]
    [InlineData(SenderAuthenticationMethod.DomainKeysIdentifiedMail, "Dkim")]
    [InlineData(SenderAuthenticationMethod.SenderPolicyFramework, "Spf")]
    public void From_StoredCheck_PublishesTheProtocolName(SenderAuthenticationMethod method, string expected)
    {
        // Arrange
        var evidence = SenderAuthenticationEvidence.None with { AuthenticatedBy = method };

        // Act
        var published = ReportedSenderAuthentication.From(evidence);

        // Assert
        Assert.Equal(expected, published.AuthenticatedBy.ToString());
    }

    /// <summary>Every DMARC result the trusted header can report has a published value of its own.</summary>
    /// <remarks>
    /// A server that evaluated DMARC and found no published policy is deliberately not the same answer as a server
    /// that reported no DMARC result at all, so both are published rather than folded into one absence.
    /// </remarks>
    [Theory]
    [InlineData(DmarcOutcome.NotReported, "NotReported")]
    [InlineData(DmarcOutcome.Pass, "Pass")]
    [InlineData(DmarcOutcome.Fail, "Fail")]
    [InlineData(DmarcOutcome.NoPolicyPublished, "NoPolicyPublished")]
    [InlineData(DmarcOutcome.TemporaryError, "TemporaryError")]
    [InlineData(DmarcOutcome.PermanentError, "PermanentError")]
    public void From_StoredDmarcOutcome_PublishesTheServersOwnResult(DmarcOutcome outcome, string expected)
    {
        // Arrange
        var evidence = SenderAuthenticationEvidence.None with { Dmarc = outcome };

        // Act
        var published = ReportedSenderAuthentication.From(evidence);

        // Assert
        Assert.Equal(expected, published.Dmarc.ToString());
    }

    /// <summary>Who reached the verdict is published, because it is how a reader weighs everything beside it.</summary>
    [Theory]
    [InlineData(SenderAuthenticationSource.ReceivingServer, "ReceivingServer")]
    [InlineData(SenderAuthenticationSource.LocalVerification, "LocalVerification")]
    public void From_StoredVerdictSource_PublishesWhoReachedIt(SenderAuthenticationSource source, string expected)
    {
        // Arrange
        var evidence = SenderAuthenticationEvidence.None with { Source = source };

        // Act
        var published = ReportedSenderAuthentication.From(evidence);

        // Assert
        Assert.Equal(expected, published.VerdictSource.ToString());
    }

    /// <summary>A stored verdict source nobody decided how to publish is refused rather than guessed at.</summary>
    [Fact]
    public void From_VerdictSourceWithNoPublishedValue_IsRefused()
    {
        // Arrange
        var evidence = SenderAuthenticationEvidence.None with { Source = (SenderAuthenticationSource)99 };

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportedSenderAuthentication.From(evidence));
    }

    /// <summary>A stored check nobody decided how to publish is refused rather than guessed at.</summary>
    [Fact]
    public void From_CheckWithNoPublishedValue_IsRefused()
    {
        // Arrange
        var evidence = SenderAuthenticationEvidence.None with
        {
            AuthenticatedBy = (SenderAuthenticationMethod)99,
        };

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportedSenderAuthentication.From(evidence));
    }

    /// <summary>A stored DMARC result nobody decided how to publish is refused rather than guessed at.</summary>
    [Fact]
    public void From_DmarcOutcomeWithNoPublishedValue_IsRefused()
    {
        // Arrange
        var evidence = SenderAuthenticationEvidence.None with { Dmarc = (DmarcOutcome)99 };

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportedSenderAuthentication.From(evidence));
    }

    /// <summary>Nothing is published for evidence that was never handed over.</summary>
    [Fact]
    public void From_WithoutEvidence_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ReportedSenderAuthentication.From(null!));
    }

    private static SenderDomain DomainOf(string value)
    {
        Assert.True(SenderDomain.TryCreate(value, out var domain));

        return domain;
    }
}
