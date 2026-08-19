// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

public sealed class OutgoingEmailRequesterTests
{
    private static readonly StoredEmailId AnsweredEmail =
        StoredEmailId.Create(Guid.Parse("0198f0a0-0000-7000-8000-000000000001"));

    private static readonly RecurringSendId Declaration =
        RecurringSendId.Create(Guid.Parse("0198f0a0-2222-7000-8000-000000000001"));

    private static readonly DateTimeOffset Occurrence = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

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

    /// <summary>
    /// Two occasions of one declaration are two requesters, which is what makes a year of Mondays a year of messages
    /// rather than one message a repeat delivery keeps reading back.
    /// </summary>
    [Fact]
    public void Schedule_TwoOccasionsOfOneDeclaration_AreDifferentRequesters()
    {
        // Arrange
        var monday = OutgoingEmailRequester.Schedule(Declaration, Occurrence);

        // Act
        var theMondayAfter = OutgoingEmailRequester.Schedule(Declaration, Occurrence.AddDays(7));

        // Assert
        Assert.NotEqual(monday, theMondayAfter);
        Assert.Equal(OutgoingEmailOrigin.Schedule, theMondayAfter.Origin);
    }

    /// <summary>
    /// Two dispatches reaching one occasion compose one requester, which is what makes the outbox answer the second
    /// with the record the first one wrote instead of sending the occasion twice.
    /// </summary>
    [Fact]
    public void Schedule_OneOccasionReachedTwice_IsOneRequester()
    {
        // Arrange
        var first = OutgoingEmailRequester.Schedule(Declaration, Occurrence);

        // Act
        var second = OutgoingEmailRequester.Schedule(Declaration, Occurrence.ToOffset(TimeSpan.FromHours(2)));

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>Two declarations reaching the same occasion are two requesters, so one mailbox's repetitions stay separate.</summary>
    [Fact]
    public void Schedule_TwoDeclarationsAtOneOccasion_AreDifferentRequesters()
    {
        // Arrange
        var first = OutgoingEmailRequester.Schedule(Declaration, Occurrence);

        // Act
        var second = OutgoingEmailRequester.Schedule(
            RecurringSendId.Create(Guid.Parse("0198f0a0-2222-7000-8000-000000000002")),
            Occurrence);

        // Assert
        Assert.NotEqual(first, second);
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

    /// <summary>
    /// Two distinct rules must never compose one identity. A rule named <c>a</c> at revision <c>b@c</c> and one named
    /// <c>a@b</c> at revision <c>c</c> would otherwise write the same key, and the unique index would read the second
    /// rule's genuine send as a retry of the first one's — which is a message nobody is ever told was not sent.
    /// </summary>
    [Theory]
    [InlineData("archive@newsletters", "3")]
    [InlineData("archive-newsletters", "3@4")]
    [InlineData("archive#newsletters", "3")]
    [InlineData("archive-newsletters", "3#4")]
    public void Rule_PartCarryingASeparatorTheIdentityIsComposedWith_IsRefused(string ruleName, string revision) =>
        Assert.Throws<ArgumentException>(() => OutgoingEmailRequester.Rule(ruleName, revision, AnsweredEmail));

    /// <summary>A stored rule identity carries both separators, so restoring one is not held to the parts' rule.</summary>
    [Fact]
    public void Create_ARuleIdentityReadBackFromARecord_IsRestoredUnchanged()
    {
        // Arrange
        var recorded = OutgoingEmailRequester.Rule("archive-newsletters", "3", AnsweredEmail);

        // Act
        var restored = OutgoingEmailRequester.Create(OutgoingEmailOrigin.Rule, recorded.Identity);

        // Assert
        Assert.Equal(recorded, restored);
    }

    [Fact]
    public void Create_OriginOutsideTheDeclaredSet_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OutgoingEmailRequester.Create((OutgoingEmailOrigin)7, "mfctl-4f2a"));
}
