// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Composition;

/// <summary>
/// Covers the answer a composition hands back: that exactly one of its two halves is present, that a refusal publishes
/// a stable identity for what stopped it, and that a sending identity cannot name a mailbox that is not one.
/// </summary>
public sealed class AuthoredEmailCompositionTests
{
    /// <summary>A composed result carries the message and no refusal, which is what a caller reads it by.</summary>
    [Fact]
    public void Composed_ComposedMessage_CarriesItAndNoRefusal()
    {
        // Arrange
        var email = new ComposedOutgoingEmail(
            OutgoingEmailRequest.Create(MailAccountId.Create("primary"), Requester(), [Recipient()]),
            InternetMessageId.Mint("example.test"),
            new byte[] { 1, 2, 3 });

        // Act
        var composition = AuthoredEmailComposition.Composed(email);

        // Assert
        Assert.True(composition.IsComposed);
        Assert.Same(email, composition.Email);
        Assert.Null(composition.Refusal);
    }

    /// <summary>A refused result carries the refusal and no message, so nothing downstream can transmit half an answer.</summary>
    [Fact]
    public void Refused_Refusal_CarriesItAndNoMessage()
    {
        // Act
        var composition = AuthoredEmailComposition.Refused(
            AuthoredEmailRefusalReason.BoundExceeded,
            AuthoredEmailField.Message,
            bound: 1024);

        // Assert
        Assert.False(composition.IsComposed);
        Assert.Null(composition.Email);
        Assert.Equal(AuthoredEmailField.Message, composition.Refusal!.Field);
        Assert.Equal(1024, composition.Refusal.Bound);
    }

    /// <summary>Neither half of a composition is optional, so neither factory accepts nothing.</summary>
    [Fact]
    public void Factories_NothingToCarry_AreRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => AuthoredEmailComposition.Composed(null!));
        Assert.Throws<ArgumentNullException>(() => AuthoredEmailComposition.Refused(null!));
    }

    /// <summary>Every declared reason publishes an identity of its own, which is what an operator's runbook names.</summary>
    [Theory]
    [InlineData(AuthoredEmailRefusalReason.SenderUnconfigured, 28001)]
    [InlineData(AuthoredEmailRefusalReason.HeaderInjected, 28002)]
    [InlineData(AuthoredEmailRefusalReason.FieldUnusable, 28003)]
    [InlineData(AuthoredEmailRefusalReason.InternationalizationUnsupported, 28004)]
    [InlineData(AuthoredEmailRefusalReason.BoundExceeded, 28005)]
    public void Failure_DeclaredReason_PublishesItsOwnCode(AuthoredEmailRefusalReason reason, int expectedCode)
    {
        // Arrange
        var refusal = new AuthoredEmailRefusal(reason, AuthoredEmailField.Message);

        // Act
        var failure = refusal.Failure;

        // Assert
        Assert.Equal(expectedCode, failure.Value);
        Assert.True(failure.IsSpecified);
    }

    /// <summary>A reason cast from a number this system never declared has no identity to publish.</summary>
    [Fact]
    public void Failure_ReasonThisSystemDoesNotDeclare_IsRefused()
    {
        // Arrange
        var refusal = new AuthoredEmailRefusal((AuthoredEmailRefusalReason)99, AuthoredEmailField.Message);

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => refusal.Failure);
    }

    /// <summary>A sending identity that named no mailbox would compose a message written from nobody.</summary>
    [Fact]
    public void Create_AddressNamingNoMailbox_IsRefused()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(
            () => OutgoingSenderIdentity.Create(MailAccountId.Create("primary"), default));
    }

    /// <summary>The domain is the half a minted message identity is unique within, and is read from the address itself.</summary>
    [Fact]
    public void Create_SendingAddress_ReadsItsDomain()
    {
        // Arrange
        Assert.True(EmailAddress.TryCreate("MailFathom", "mailfathom@example.test", out var address));

        // Act
        var identity = OutgoingSenderIdentity.Create(MailAccountId.Create("primary"), address);

        // Assert
        Assert.Equal("example.test", identity.Domain);
        Assert.Equal("MailFathom", identity.Address.DisplayName);
    }

    private static OutgoingRecipient Recipient()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return OutgoingRecipient.Create(address, OutgoingRecipientRole.To);
    }

    private static OutgoingEmailRequester Requester() => OutgoingEmailRequester.Command("send-quarterly-report");
}
