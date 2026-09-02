// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.MailKit.Delivery;
using MailKit.Net.Smtp;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Delivery;

/// <summary>Covers what a submission server's refusal is taken to mean, and what is kept from it.</summary>
public sealed class SmtpReplyClassifierTests
{
    /// <summary>RFC 5321 gives the 4yz class to a temporary rejection, and everything else states a settled answer.</summary>
    [Theory]
    [InlineData(421, false)]
    [InlineData(450, false)]
    [InlineData(452, false)]
    [InlineData(500, true)]
    [InlineData(535, true)]
    [InlineData(550, true)]
    [InlineData(554, true)]
    public void Classify_ReplyCodeAlone_FollowsTheReplyClass(int replyCode, bool expectedPermanent)
    {
        // Act
        var classification = SmtpReplyClassifier.Classify(replyCode, "the server said something");

        // Assert
        Assert.Equal(replyCode, classification.ReplyCode);
        Assert.Null(classification.EnhancedStatusCode);
        Assert.Equal(
            expectedPermanent ? SmtpRejectionDisposition.Permanent : SmtpRejectionDisposition.Transient,
            classification.Disposition);
    }

    /// <summary>
    /// A reply nobody recognizes is settled rather than repeated. A submission repeated against a server whose answer
    /// was not understood is how a second copy reaches a recipient.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    [InlineData(354)]
    [InlineData(999)]
    public void Classify_ReplyThatStatesNoTemporaryRejection_IsPermanent(int replyCode)
    {
        // Act
        var classification = SmtpReplyClassifier.Classify(replyCode, replyText: null);

        // Assert
        Assert.Equal(SmtpRejectionDisposition.Permanent, classification.Disposition);
    }

    /// <summary>The enhanced status code a reply opens with is kept as the three numbers RFC 3463 makes it of.</summary>
    [Theory]
    [InlineData("4.7.1 Greylisted, try again later", 4, 7, 1)]
    [InlineData("5.1.1 <someone@example.test>: recipient rejected", 5, 1, 1)]
    [InlineData("2.0.0 Ok", 2, 0, 0)]
    [InlineData("5.7.999 policy", 5, 7, 999)]
    [InlineData("  4.4.2 timed out\r\nmore text", 4, 4, 2)]
    public void Classify_ReplyOpeningWithAnEnhancedStatusCode_KeepsItsThreeParts(
        string replyText,
        int expectedClass,
        int expectedSubject,
        int expectedDetail)
    {
        // Act
        var classification = SmtpReplyClassifier.Classify(450, replyText);

        // Assert
        var enhancedStatusCode = Assert.IsType<SmtpEnhancedStatusCode>(classification.EnhancedStatusCode);
        Assert.Equal(expectedClass, enhancedStatusCode.Class);
        Assert.Equal(expectedSubject, enhancedStatusCode.Subject);
        Assert.Equal(expectedDetail, enhancedStatusCode.Detail);
    }

    /// <summary>A reply that opens with anything else carries no enhanced status code, which is an ordinary answer.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mailbox unavailable")]
    [InlineData("4.7")]
    [InlineData("4.7.1.2")]
    [InlineData("3.1.1 no such class")]
    [InlineData("5.7.1234 out of range")]
    [InlineData("5..1 empty part")]
    [InlineData("5.x.1 not a number")]
    public void Classify_ReplyWithoutAWellFormedEnhancedStatusCode_ReportsNone(string? replyText)
    {
        // Act
        var classification = SmtpReplyClassifier.Classify(450, replyText);

        // Assert
        Assert.Null(classification.EnhancedStatusCode);
        Assert.Equal(SmtpRejectionDisposition.Transient, classification.Disposition);
    }

    /// <summary>
    /// Where the two statements disagree the permanent reading wins, because being wrong about a permanent failure
    /// costs a delivery that had already failed while being wrong about a transient one costs a message received twice.
    /// </summary>
    [Theory]
    [InlineData(450, "5.7.1 not authorized", true)]
    [InlineData(421, "5.3.2 system not accepting messages", true)]
    [InlineData(550, "4.2.2 mailbox full", true)]
    [InlineData(450, "4.2.1 mailbox disabled", false)]
    [InlineData(450, "2.0.0 nonsense in a refusal", false)]
    public void Classify_EnhancedStatusCodeBesideTheReplyCode_RefinesOnlyTowardsPermanent(
        int replyCode,
        string replyText,
        bool expectedPermanent)
    {
        // Act
        var classification = SmtpReplyClassifier.Classify(replyCode, replyText);

        // Assert
        Assert.Equal(
            expectedPermanent ? SmtpRejectionDisposition.Permanent : SmtpRejectionDisposition.Transient,
            classification.Disposition);
    }

    /// <summary>The refusal the mail library raises is classified from the same two statements a reply carries.</summary>
    [Fact]
    public void Classify_MailLibraryRefusal_ReadsItsReplyCodeAndEnhancedStatusCode()
    {
        // Arrange
        var refusal = new SmtpCommandException(
            SmtpErrorCode.MessageNotAccepted,
            SmtpStatusCode.MailboxBusy,
            "4.3.1 insufficient system storage");

        // Act
        var classification = SmtpReplyClassifier.Classify(refusal);

        // Assert
        Assert.Equal(450, classification.ReplyCode);
        Assert.Equal("4.3.1", classification.EnhancedStatusCode?.ToString());
        Assert.Equal(SmtpRejectionDisposition.Transient, classification.Disposition);
    }
}
