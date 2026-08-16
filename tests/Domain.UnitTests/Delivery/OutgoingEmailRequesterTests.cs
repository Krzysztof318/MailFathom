// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

public sealed class OutgoingEmailRequesterTests
{
    private static readonly StoredEmailId AnsweredEmail =
        StoredEmailId.Create(Guid.Parse("0198f0a0-0000-7000-8000-000000000001"));

    /// <summary>A retried command carries the key the first call carried, so it is one request and one delivery.</summary>
    [Fact]
    public void Command_SameKeyTwice_IsOneRequester()
    {
        // Arrange
        var first = OutgoingEmailRequester.Command("send-quarterly-report-2026-Q3");

        // Act
        var retried = OutgoingEmailRequester.Command("send-quarterly-report-2026-Q3");

        // Assert
        Assert.Equal(first, retried);
        Assert.Equal(OutgoingEmailOrigin.Command, retried.Origin);
    }

    /// <summary>The revision is part of the identity, which is what makes an edited rule send again and an unchanged one send once.</summary>
    [Fact]
    public void Rule_TwoRevisionsOfOneRule_AreDifferentRequesters()
    {
        // Arrange
        var third = OutgoingEmailRequester.Rule("acknowledge-invoices", "3", AnsweredEmail);

        // Act
        var fourth = OutgoingEmailRequester.Rule("acknowledge-invoices", "4", AnsweredEmail);

        // Assert
        Assert.NotEqual(third, fourth);
        Assert.Equal(OutgoingEmailRequester.Rule("acknowledge-invoices", "3", AnsweredEmail), third);
    }

    /// <summary>One rule answering two emails is two sends, which is what keeps the email in the identity.</summary>
    [Fact]
    public void Rule_TwoEmailsUnderOneRevision_AreDifferentRequesters()
    {
        // Arrange
        var first = OutgoingEmailRequester.Rule("acknowledge-invoices", "3", AnsweredEmail);

        // Act
        var second = OutgoingEmailRequester.Rule(
            "acknowledge-invoices",
            "3",
            StoredEmailId.Create(Guid.Parse("0198f0a0-0000-7000-8000-000000000002")));

        // Assert
        Assert.NotEqual(first, second);
        Assert.Equal(OutgoingEmailOrigin.Rule, second.Origin);
    }

    /// <summary>What a record holds is what comes back, so a restored requester compares equal to the one that was written.</summary>
    [Fact]
    public void Create_FromStoredOriginAndIdentity_RestoresTheRequesterThatWasWritten()
    {
        // Arrange
        var written = OutgoingEmailRequester.Command("mfctl-4f2a");

        // Act
        var restored = OutgoingEmailRequester.Create(written.Origin, written.Identity);

        // Assert
        Assert.Equal(written, restored);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("send\u0007now")]
    public void Command_UnusableKey_IsRefused(string invocationIdentity) =>
        Assert.Throws<ArgumentException>(() => OutgoingEmailRequester.Command(invocationIdentity));

    /// <summary>The bound is the column's, so an identity that could not be stored is refused before anything is durable.</summary>
    [Fact]
    public void Command_KeyLongerThanTheColumn_IsRefused()
    {
        // Arrange
        var overlongKey = new string('k', OutgoingEmailRequester.MaximumIdentityLength + 1);

        // Act
        var thrown = Assert.Throws<ArgumentException>(() => OutgoingEmailRequester.Command(overlongKey));

        // Assert
        Assert.Equal("invocationIdentity", thrown.ParamName);
    }

    /// <summary>A rule name long enough to overflow the composed identity is reported against the name the caller wrote.</summary>
    [Fact]
    public void Rule_NameLongerThanTheComposedIdentityAllows_IsReportedAgainstTheName()
    {
        // Arrange
        var overlongName = new string('r', OutgoingEmailRequester.MaximumIdentityLength);

        // Act
        var thrown = Assert.Throws<ArgumentException>(
            () => OutgoingEmailRequester.Rule(overlongName, "3", AnsweredEmail));

        // Assert
        Assert.Equal("ruleName", thrown.ParamName);
    }

    /// <summary>The revision is the caller's too, so an overflow it caused names it rather than the rule beside it.</summary>
    [Fact]
    public void Rule_RevisionLongerThanTheComposedIdentityAllows_IsReportedAgainstTheRevision()
    {
        // Arrange
        var overlongRevision = new string('9', OutgoingEmailRequester.MaximumIdentityLength);

        // Act
        var thrown = Assert.Throws<ArgumentException>(
            () => OutgoingEmailRequester.Rule("archive-newsletters", overlongRevision, AnsweredEmail));

        // Assert
        Assert.Equal("revision", thrown.ParamName);
    }

    [Fact]
    public void Create_OriginOutsideTheDeclaredSet_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OutgoingEmailRequester.Create((OutgoingEmailOrigin)7, "mfctl-4f2a"));
}
