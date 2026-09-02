// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Governance;

public sealed class OutgoingRecipientPolicyTests
{
    /// <summary>A deployment that named nobody writes to anybody, which is the posture an operator gets by writing nothing.</summary>
    [Fact]
    public void Judge_PolicyNamingNobody_AdmitsEveryRecipient()
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([], []);

        // Act
        var refusal = policy.Judge(Address("anna@example.test"));

        // Assert
        Assert.Null(refusal);
        Assert.False(policy.RestrictsRecipients);
    }

    /// <summary>An allowed list is a statement about everybody: whoever is not on it is refused as being outside it.</summary>
    [Theory]
    [InlineData("anna@example.test", null)]
    [InlineData("anna@team.example.test", null)]
    [InlineData("anna@notexample.test", OutgoingRecipientRefusalReason.OutsideAllowedRecipients)]
    [InlineData("anna@example.test.evil.test", OutgoingRecipientRefusalReason.OutsideAllowedRecipients)]
    public void Judge_AllowedDomain_AdmitsThatOrganizationAndRefusesEverybodyElse(
        string recipient,
        OutgoingRecipientRefusalReason? expected)
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([DomainRule("example.test")], []);

        // Act
        var refusal = policy.Judge(Address(recipient));

        // Assert
        Assert.Equal(expected, refusal);
    }

    /// <summary>An address entry names one mailbox and nobody else at the same provider.</summary>
    [Theory]
    [InlineData("anna@example.test", null)]
    [InlineData("ANNA@EXAMPLE.TEST", null)]
    [InlineData("bruno@example.test", OutgoingRecipientRefusalReason.OutsideAllowedRecipients)]
    public void Judge_AllowedAddress_AdmitsThatMailboxAlone(
        string recipient,
        OutgoingRecipientRefusalReason? expected)
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([AddressRule("anna@example.test")], []);

        // Act
        var refusal = policy.Judge(Address(recipient));

        // Assert
        Assert.Equal(expected, refusal);
    }

    /// <summary>Denial is read first, so a recipient an operator wrote on both lists is refused rather than admitted.</summary>
    [Fact]
    public void Judge_RecipientOnBothLists_IsRefusedByTheDenial()
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create(
            [DomainRule("example.test")],
            [AddressRule("anna@example.test")]);

        // Act
        var refusal = policy.Judge(Address("anna@example.test"));

        // Assert
        Assert.Equal(OutgoingRecipientRefusalReason.DeniedByPolicy, refusal);
    }

    /// <summary>A denied organization reaches the names beneath it, which is what keeps a subdomain from being the way around it.</summary>
    [Theory]
    [InlineData("bruno@rival.test")]
    [InlineData("bruno@mail.rival.test")]
    public void Judge_DeniedDomain_RefusesThatOrganizationAndWhatSitsBeneathIt(string recipient)
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([], [DomainRule("rival.test")]);

        // Act
        var refusal = policy.Judge(Address(recipient));

        // Assert
        Assert.Equal(OutgoingRecipientRefusalReason.DeniedByPolicy, refusal);
    }

    /// <summary>A denied list alone restricts only whom it names, so everybody else is still written to.</summary>
    [Fact]
    public void Judge_DeniedListAlone_AdmitsEverybodyItDoesNotName()
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([], [DomainRule("rival.test")]);

        // Act
        var refusal = policy.Judge(Address("anna@example.test"));

        // Assert
        Assert.Null(refusal);
        Assert.True(policy.RestrictsRecipients);
    }

    /// <summary>An entry written in either encoding of an internationalized name names the same organization.</summary>
    [Fact]
    public void Judge_AllowedDomainWrittenAsAUnicodeName_AdmitsTheAddressThatCarriesItsAsciiForm()
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([DomainRule("bücher.test")], []);

        // Act
        var refusal = policy.Judge(Address("anna@xn--bcher-kva.test"));

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>A message everybody on it is admitted for is admitted whole, whichever header each recipient is named in.</summary>
    [Fact]
    public void FindFirstRefusal_EveryRecipientAdmitted_ReportsNoRefusal()
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([DomainRule("example.test")], []);

        // Act
        var refusal = policy.FindFirstRefusal(
        [
            Recipient("anna@example.test", OutgoingRecipientRole.To),
            Recipient("bruno@team.example.test", OutgoingRecipientRole.Cc),
        ]);

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>The message is judged whole, so a recipient in any header refuses it and the reason is that recipient's own.</summary>
    [Fact]
    public void FindFirstRefusal_RecipientRefusedInALaterHeader_ReportsThatRecipientsReason()
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([], [DomainRule("rival.test")]);

        // Act
        var refusal = policy.FindFirstRefusal(
        [
            Recipient("anna@example.test", OutgoingRecipientRole.To),
            Recipient("bruno@rival.test", OutgoingRecipientRole.Bcc),
        ]);

        // Assert
        Assert.Equal(OutgoingRecipientRefusalReason.DeniedByPolicy, refusal);
    }

    /// <summary>The reading stops at the first refusal, so a denial ahead of an outsider is the reason reported.</summary>
    [Fact]
    public void FindFirstRefusal_SeveralRecipientsRefused_ReportsTheFirstOfThem()
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([DomainRule("example.test")], [DomainRule("rival.test")]);

        // Act
        var refusal = policy.FindFirstRefusal(
        [
            Recipient("anna@rival.test", OutgoingRecipientRole.To),
            Recipient("bruno@elsewhere.test", OutgoingRecipientRole.To),
        ]);

        // Assert
        Assert.Equal(OutgoingRecipientRefusalReason.DeniedByPolicy, refusal);
    }

    /// <summary>A message addressed to nobody names nobody the policy could refuse.</summary>
    [Fact]
    public void FindFirstRefusal_NoRecipients_ReportsNoRefusal()
    {
        // Arrange
        var policy = OutgoingRecipientPolicy.Create([DomainRule("example.test")], []);

        // Act
        var refusal = policy.FindFirstRefusal([]);

        // Assert
        Assert.Null(refusal);
    }

    /// <summary>Text that names no organization or mailbox is no entry at all, which is what a configuration refuses at startup.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not an address")]
    public void TryCreateForAddress_TextNamingNoMailbox_IsRefused(string? candidate)
    {
        // Act
        var created = OutgoingRecipientRule.TryCreateForAddress(candidate, out var rule);

        // Assert
        Assert.False(created);
        Assert.Null(rule);
    }

    /// <summary>An entry is built from the text an operator wrote and never from a repair of it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a domain..test")]
    public void TryCreateForDomain_TextNamingNoOrganization_IsRefused(string? candidate)
    {
        // Act
        var created = OutgoingRecipientRule.TryCreateForDomain(candidate, out var rule);

        // Assert
        Assert.False(created);
        Assert.Null(rule);
    }

    /// <summary>Every list is copied on the way in, so a caller mutating what it passed cannot widen a policy afterwards.</summary>
    [Fact]
    public void Create_ListMutatedAfterwards_LeavesThePolicyAsItWasBuilt()
    {
        // Arrange
        var allowed = new List<OutgoingRecipientRule> { DomainRule("example.test") };
        var policy = OutgoingRecipientPolicy.Create(allowed, []);

        // Act
        allowed.Add(DomainRule("rival.test"));

        // Assert
        Assert.Equal(OutgoingRecipientRefusalReason.OutsideAllowedRecipients, policy.Judge(Address("bruno@rival.test")));
    }

    private static OutgoingRecipientRule DomainRule(string domain)
    {
        Assert.True(OutgoingRecipientRule.TryCreateForDomain(domain, out var rule));

        return rule;
    }

    private static OutgoingRecipientRule AddressRule(string address)
    {
        Assert.True(OutgoingRecipientRule.TryCreateForAddress(address, out var rule));

        return rule;
    }

    private static OutgoingRecipient Recipient(string address, OutgoingRecipientRole role) =>
        OutgoingRecipient.Create(Address(address), role);

    private static EmailAddress Address(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var mailbox));

        return mailbox;
    }
}
