// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Authoring;

/// <summary>Covers what an authoring attempt hands back: the answer and the account it is sent as, or the refusal alone.</summary>
public sealed class AuthoredResponseTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    /// <summary>An authored answer carries the message and the account it is sent as, and no refusal.</summary>
    [Fact]
    public void Authored_AnAnswerSomebodyWrote_CarriesItBesideTheAccountItIsSentAs()
    {
        // Act
        var response = AuthoredResponse.Authored(Account, Answer());

        // Assert
        Assert.True(response.IsAuthored);
        Assert.Equal(Account, response.AccountId);
        Assert.NotNull(response.Email);
        Assert.Null(response.Refusal);
    }

    /// <summary>A refusal carries no account, for the same reason it carries no address.</summary>
    [Fact]
    public void Refused_ARefusedAnswer_CarriesTheReasonAndNothingAboutTheMessage()
    {
        // Act
        var response = AuthoredResponse.Refused(AuthoredResponseRefusalReason.BoundExceeded, bound: 64);

        // Assert
        Assert.False(response.IsAuthored);
        Assert.Null(response.Email);
        Assert.Equal(AuthoredResponseRefusalReason.BoundExceeded, response.Refusal!.Reason);
        Assert.Equal(64, response.Refusal.Bound);
        Assert.Equal(default, response.AccountId);
    }

    /// <summary>An answer is a required value rather than something an authored result may be composed without.</summary>
    [Fact]
    public void Authored_NoAnswer_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => AuthoredResponse.Authored(Account, null!));
    }

    /// <summary>Every declared reason publishes an identity of its own, which is what an operator's runbook names.</summary>
    [Theory]
    [InlineData(AuthoredResponseRefusalReason.AnsweredEmailNotFound, 28006)]
    [InlineData(AuthoredResponseRefusalReason.AnsweredEmailContentUnavailable, 28007)]
    [InlineData(AuthoredResponseRefusalReason.SenderUnconfigured, 28001)]
    [InlineData(AuthoredResponseRefusalReason.BoundExceeded, 28005)]
    [InlineData(AuthoredResponseRefusalReason.RecipientContactUnknown, 28013)]
    [InlineData(AuthoredResponseRefusalReason.RecipientContactNameAmbiguous, 28014)]
    [InlineData(AuthoredResponseRefusalReason.RecipientContactAddressNotHeld, 28015)]
    public void Failure_DeclaredReason_PublishesItsOwnCode(
        AuthoredResponseRefusalReason reason,
        int expectedCode)
    {
        // Arrange
        var refusal = new AuthoredResponseRefusal(reason);

        // Act
        var failure = refusal.Failure;

        // Assert
        Assert.Equal(expectedCode, failure.Value);
        Assert.True(failure.IsSpecified);
    }

    /// <summary>
    /// A recipient that addressed nobody is translated into this result's own terms, so an author acts on one refusal
    /// shape whatever part of the send produced it — and the identity it publishes stays the resolution's own.
    /// </summary>
    [Theory]
    [InlineData(RecipientResolutionRefusalReason.ContactUnknown, AuthoredResponseRefusalReason.RecipientContactUnknown)]
    [InlineData(
        RecipientResolutionRefusalReason.ContactNameAmbiguous,
        AuthoredResponseRefusalReason.RecipientContactNameAmbiguous)]
    [InlineData(
        RecipientResolutionRefusalReason.ContactAddressNotHeld,
        AuthoredResponseRefusalReason.RecipientContactAddressNotHeld)]
    public void Refused_ARecipientThatAddressedNobody_TranslatesTheReasonAndKeepsTheIdentity(
        RecipientResolutionRefusalReason resolved,
        AuthoredResponseRefusalReason expected)
    {
        // Arrange
        var refusal = new RecipientResolutionRefusal(resolved, MatchedContactCount: 3);

        // Act
        var response = AuthoredResponse.Refused(refusal);

        // Assert
        Assert.False(response.IsAuthored);
        Assert.Equal(expected, response.Refusal!.Reason);
        Assert.Equal(3, response.Refusal.MatchedContactCount);
        Assert.Null(response.Refusal.Bound);
        Assert.Equal(refusal.Failure, response.Refusal.Failure);
    }

    /// <summary>A resolution reason cast from a number this system never declared cannot be translated either.</summary>
    [Fact]
    public void Refused_AResolutionReasonThisSystemDoesNotDeclare_IsRefused()
    {
        // Arrange
        var refusal = new RecipientResolutionRefusal((RecipientResolutionRefusalReason)99);

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => AuthoredResponse.Refused(refusal));
    }

    /// <summary>A reason cast from a number this system never declared has no identity to publish.</summary>
    [Fact]
    public void Failure_ReasonThisSystemDoesNotDeclare_IsRefused()
    {
        // Arrange
        var refusal = new AuthoredResponseRefusal((AuthoredResponseRefusalReason)99);

        // Act and assert
        Assert.Throws<InvalidOperationException>(() => refusal.Failure);
    }

    private static AuthoredEmail Answer() => new()
    {
        Recipients = [new AuthoredEmailRecipient(OutgoingRecipientRole.To, "author@example.test")],
        Subject = "Re: Quarterly report",
        PlainTextBody = "Thank you.",
    };
}
