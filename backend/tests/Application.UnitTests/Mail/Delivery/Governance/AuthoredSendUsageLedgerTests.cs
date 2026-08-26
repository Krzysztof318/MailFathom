// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Governance;

public sealed class AuthoredSendUsageLedgerTests
{
    private const string Caller = "agent-key";

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    /// <summary>A deployment that bounded no caller counts nothing, so the ordinary posture holds no identity at all.</summary>
    [Fact]
    public void Admit_NoCeilingDeclared_RefusesNothingHoweverMuchIsAskedFor()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(AuthoredSendCeilings.Unbounded, ClockAt("2026-08-19T10:00:00Z"));

        // Act
        var reached = Enumerable
            .Range(0, 100)
            .Select(send => ledger.Admit(Caller, Send(send, recipientCount: 10)))
            .ToArray();

        // Assert
        Assert.All(reached, ceiling => Assert.Null(ceiling));
    }

    /// <summary>The caller that fills its period is admitted, and the message after it is refused naming the ceiling.</summary>
    [Fact]
    public void Admit_CallerAtItsMessageCeiling_AdmitsTheFillingMessageAndRefusesTheNext()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 2, maxRecipientsPerCaller: 0);
        Assert.Null(ledger.Admit(Caller, Send(1, recipientCount: 1)));

        // Act
        var filling = ledger.Admit(Caller, Send(2, recipientCount: 1));
        var past = ledger.Admit(Caller, Send(3, recipientCount: 1));

        // Assert
        Assert.Null(filling);
        Assert.Equal(AuthoredSendCeiling.CallerMessages, past);
    }

    /// <summary>The recipient ceiling counts the people rather than the messages, so one wide send can reach it.</summary>
    [Fact]
    public void Admit_CallerAtItsRecipientCeiling_RefusesTheNextMessage()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 0, maxRecipientsPerCaller: 5);
        Assert.Null(ledger.Admit(Caller, Send(1, recipientCount: 5)));

        // Act
        var reached = ledger.Admit(Caller, Send(2, recipientCount: 1));

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerRecipients, reached);
    }

    /// <summary>
    /// A message refused by a ceiling is not counted, so a caller that asked for more than its period holds can still
    /// send what does fit in it.
    /// </summary>
    [Fact]
    public void Admit_MessageThatWasRefused_IsChargedToNobody()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 0, maxRecipientsPerCaller: 4);

        // Act
        var refused = ledger.Admit(Caller, Send(1, recipientCount: 5));
        var admitted = ledger.Admit(Caller, Send(2, recipientCount: 4));

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerRecipients, refused);
        Assert.Null(admitted);
    }

    /// <summary>A retry is the same send asked for again, which is one charge however many times the call is repeated.</summary>
    [Fact]
    public void Admit_SameSendTwice_ChargesTheCallerOnce()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 2, maxRecipientsPerCaller: 0);

        // Act
        var first = ledger.Admit(Caller, Send(1, recipientCount: 1));
        var retried = ledger.Admit(Caller, Send(1, recipientCount: 1));
        var second = ledger.Admit(Caller, Send(2, recipientCount: 1));

        // Assert
        Assert.Null(first);
        Assert.Null(retried);
        Assert.Null(second);
    }

    /// <summary>A retry of a send this caller was already charged for is admitted even once its period has filled up.</summary>
    [Fact]
    public void Admit_RetryOfAChargedSendInAFullPeriod_IsAdmitted()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0);
        Assert.Null(ledger.Admit(Caller, Send(1, recipientCount: 1)));

        // Act
        var retried = ledger.Admit(Caller, Send(1, recipientCount: 1));
        var another = ledger.Admit(Caller, Send(2, recipientCount: 1));

        // Assert
        Assert.Null(retried);
        Assert.Equal(AuthoredSendCeiling.CallerMessages, another);
    }

    /// <summary>The judgement and the charge are one operation, so concurrent sends cannot both pass one remaining slot.</summary>
    [Fact]
    public void Admit_ConcurrentSendsFromOneCaller_AdmitsExactlyWhatThePeriodHolds()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 4, maxRecipientsPerCaller: 0);
        var sends = Enumerable.Range(0, 64).Select(send => Send(send, recipientCount: 1)).ToArray();
        var reached = new AuthoredSendCeiling?[sends.Length];

        // Act
        Parallel.For(0, sends.Length, position => reached[position] = ledger.Admit(Caller, sends[position]));

        // Assert
        Assert.Equal(4, reached.Count(ceiling => ceiling is null));
    }

    /// <summary>What one caller spent is not what another spent, which is the whole point of counting per caller.</summary>
    [Fact]
    public void Admit_SecondCaller_IsNotChargedForTheFirst()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0);
        Assert.Null(ledger.Admit(Caller, Send(1, recipientCount: 1)));

        // Act
        var another = ledger.Admit("another-key", Send(2, recipientCount: 1));

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerMessages, ledger.Admit(Caller, Send(3, recipientCount: 1)));
        Assert.Null(another);
    }

    /// <summary>A period that rolls over lifts the refusal, which is what a refused caller is told to wait for.</summary>
    [Fact]
    public void Admit_PeriodRolledOver_LiftsTheRefusal()
    {
        // Arrange
        var clock = ClockAt("2026-08-19T10:00:00Z");
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromHours(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            clock);
        Assert.Null(ledger.Admit(Caller, Send(1, recipientCount: 1)));

        // Act
        var withinThePeriod = ledger.Admit(Caller, Send(2, recipientCount: 1));
        clock.Advance(TimeSpan.FromHours(1));
        var afterTheRollOver = ledger.Admit(Caller, Send(3, recipientCount: 1));

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerMessages, withinThePeriod);
        Assert.Null(afterTheRollOver);
    }

    /// <summary>A period counting the most callers it can hold refuses one it is not already counting rather than growing.</summary>
    [Fact]
    public void Admit_MoreCallersThanOnePeriodHolds_RefusesTheCallerItCannotCount()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 10, maxRecipientsPerCaller: 0);

        for (var caller = 0; caller < AuthoredSendUsageLedger.MaximumCallersPerPeriod; caller++)
        {
            ledger.Admit(
                string.Create(CultureInfo.InvariantCulture, $"caller-{caller}"),
                Send(caller, recipientCount: 1));
        }

        // Act
        var reached = ledger.Admit("one-caller-too-many", Send(1, recipientCount: 1));

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerCount, reached);
        Assert.Null(ledger.Admit("caller-0", Send(90_001, recipientCount: 1)));
    }

    /// <summary>A principal nothing can be counted against is a defect in whoever asked rather than a refusal.</summary>
    [Fact]
    public void Admit_CallerNamingNobody_IsADefect()
    {
        // Arrange
        var ledger = LedgerOf(maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ledger.Admit(" ", Send(1, recipientCount: 1)));
    }

    private static AuthoredSendUsageLedger LedgerOf(long maxMessagesPerCaller, long maxRecipientsPerCaller) => new(
        AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller, maxRecipientsPerCaller),
        ClockAt("2026-08-19T10:00:00Z"));

    private static FakeTimeProvider ClockAt(string instant) =>
        new(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));

    private static OutgoingEmailRequest Send(int number, int recipientCount) => OutgoingEmailRequest.Create(
        Account,
        OutgoingEmailRequester.Command(string.Create(CultureInfo.InvariantCulture, $"send-{number}")),
        [.. Enumerable
            .Range(0, recipientCount)
            .Select(position => OutgoingRecipient.Create(
                Mailbox(string.Create(CultureInfo.InvariantCulture, $"person{position}@example.test")),
                OutgoingRecipientRole.To))]);

    private static EmailAddress Mailbox(string address)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, address, out var mailbox));

        return mailbox;
    }
}
