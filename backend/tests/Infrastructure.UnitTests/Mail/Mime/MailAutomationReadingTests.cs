// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.Mime;
using MimeKit;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers what a message said about having been composed by a program rather than written to one person.</summary>
public sealed class MailAutomationReadingTests
{
    /// <summary>A list posting carries the poster's real address, so only the distributor's own header can name it.</summary>
    [Theory]
    [InlineData("List-Id: MailFathom discussion <mailfathom.example.test>")]
    [InlineData("List-Post: <mailto:mailfathom@example.test>")]
    [InlineData("List-Unsubscribe: <mailto:mailfathom-unsubscribe@example.test>")]
    public void Read_AMessageADistributorStamped_ReadsItAsAMailingList(string header)
    {
        // Arrange
        var message = MessageWith("From: anna@example.test", header);

        // Act
        var automation = MailAutomationReading.Read(message);

        // Assert
        Assert.Equal(EmailAutomation.MailingList, automation);
    }

    /// <summary>RFC 3834 names one value for a message a person wrote and leaves every other keyword to mean a program did.</summary>
    [Theory]
    [InlineData("auto-generated", EmailAutomation.AutomaticallySubmitted)]
    [InlineData("auto-replied", EmailAutomation.AutomaticallySubmitted)]
    [InlineData("auto-notified; owner=postmaster@example.test", EmailAutomation.AutomaticallySubmitted)]
    [InlineData("a-keyword-registered-later", EmailAutomation.AutomaticallySubmitted)]
    [InlineData("no", EmailAutomation.None)]
    [InlineData("NO", EmailAutomation.None)]
    public void Read_AMessageStatingHowItWasSubmitted_ReadsTheKeywordItNamed(string value, EmailAutomation expected)
    {
        // Arrange
        var message = MessageWith("From: anna@example.test", $"Auto-Submitted: {value}");

        // Act
        var automation = MailAutomationReading.Read(message);

        // Assert
        Assert.Equal(expected, automation);
    }

    /// <summary>Precedence is the oldest of the three signals, so only the values that ever meant bulk distribution count.</summary>
    [Theory]
    [InlineData("bulk", EmailAutomation.BulkPrecedence)]
    [InlineData("List", EmailAutomation.BulkPrecedence)]
    [InlineData("JUNK", EmailAutomation.BulkPrecedence)]
    [InlineData("first-class", EmailAutomation.None)]
    [InlineData("urgent", EmailAutomation.None)]
    public void Read_AMessageStatingAPrecedence_ReadsOnlyTheValuesThatMeanManyRecipients(
        string value,
        EmailAutomation expected)
    {
        // Arrange
        var message = MessageWith("From: anna@example.test", $"Precedence: {value}");

        // Act
        var automation = MailAutomationReading.Read(message);

        // Assert
        Assert.Equal(expected, automation);
    }

    /// <summary>The three are asked in the order of how much they establish, so a list posting stays a list posting.</summary>
    [Fact]
    public void Read_AMessageCarryingEverySignal_ReadsTheOneThatEstablishesMost()
    {
        // Arrange
        var message = MessageWith(
            "From: anna@example.test",
            "List-Id: MailFathom discussion <mailfathom.example.test>",
            "Auto-Submitted: auto-generated",
            "Precedence: bulk");

        // Act
        var automation = MailAutomationReading.Read(message);

        // Assert
        Assert.Equal(EmailAutomation.MailingList, automation);
    }

    /// <summary>A header written unusably establishes nothing, and the message stays held against every other bound.</summary>
    [Theory]
    [InlineData("List-Id:  ")]
    [InlineData("Auto-Submitted:  ")]
    [InlineData("Precedence:  ")]
    public void Read_AHeaderTheSenderLeftBlank_ReadsNothingFromIt(string header)
    {
        // Arrange
        var message = MessageWith("From: anna@example.test", header);

        // Act
        var automation = MailAutomationReading.Read(message);

        // Assert
        Assert.Equal(EmailAutomation.None, automation);
    }

    /// <summary>Ordinary correspondence claims nothing, which is the answer collection acts on.</summary>
    [Fact]
    public void Read_AMessageOnePersonWroteToAnother_ReadsNoClaim()
    {
        // Arrange
        var message = MessageWith("From: anna@example.test", "To: marek@example.test", "Subject: Lunch");

        // Act
        var automation = MailAutomationReading.Read(message);

        // Assert
        Assert.Equal(EmailAutomation.None, automation);
    }

    private static MimeMessage MessageWith(params string[] headers)
    {
        var content = string.Join("\r\n", [.. headers, string.Empty, "Body", string.Empty]);
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));

        return MimeMessage.Load(stream);
    }
}
