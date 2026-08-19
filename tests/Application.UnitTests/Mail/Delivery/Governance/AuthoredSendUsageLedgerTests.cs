// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Governance;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Governance;

public sealed class AuthoredSendUsageLedgerTests
{
    private const string Caller = "agent-key";

    /// <summary>A deployment that bounded no caller counts nothing, so the ordinary posture holds no identity at all.</summary>
    [Fact]
    public void FindReachedCeiling_NoCeilingDeclared_RefusesNothingHoweverMuchIsCharged()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(AuthoredSendCeilings.Unbounded, ClockAt("2026-08-19T10:00:00Z"));

        // Act
        for (var send = 0; send < 100; send++)
        {
            ledger.Charge(Caller, RecordNumber(send), recipientCount: 10);
        }

        // Assert
        Assert.Null(ledger.FindReachedCeiling(Caller, recipientCount: 10));
    }

    /// <summary>The caller that fills its period is admitted, and the message after it is refused naming the ceiling.</summary>
    [Fact]
    public void FindReachedCeiling_CallerAtItsMessageCeiling_AdmitsTheFillingMessageAndRefusesTheNext()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 2, maxRecipientsPerCaller: 0),
            ClockAt("2026-08-19T10:00:00Z"));
        ledger.Charge(Caller, RecordNumber(1), recipientCount: 1);

        // Act
        var filling = ledger.FindReachedCeiling(Caller, recipientCount: 1);
        ledger.Charge(Caller, RecordNumber(2), recipientCount: 1);
        var past = ledger.FindReachedCeiling(Caller, recipientCount: 1);

        // Assert
        Assert.Null(filling);
        Assert.Equal(AuthoredSendCeiling.CallerMessages, past);
    }

    /// <summary>The recipient ceiling counts the people rather than the messages, so one wide send can reach it.</summary>
    [Fact]
    public void FindReachedCeiling_CallerAtItsRecipientCeiling_RefusesTheNextMessage()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 0, maxRecipientsPerCaller: 5),
            ClockAt("2026-08-19T10:00:00Z"));
        ledger.Charge(Caller, RecordNumber(1), recipientCount: 5);

        // Act
        var reached = ledger.FindReachedCeiling(Caller, recipientCount: 1);

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerRecipients, reached);
    }

    /// <summary>A retry of one send is the record the first call produced, so a careful client is charged once.</summary>
    [Fact]
    public void Charge_SameRecordTwice_ChargesTheCallerOnce()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 2, maxRecipientsPerCaller: 0),
            ClockAt("2026-08-19T10:00:00Z"));

        // Act
        ledger.Charge(Caller, RecordNumber(1), recipientCount: 1);
        ledger.Charge(Caller, RecordNumber(1), recipientCount: 1);

        // Assert
        Assert.Null(ledger.FindReachedCeiling(Caller, recipientCount: 1));
    }

    /// <summary>What one caller spent is not what another spent, which is the whole point of counting per caller.</summary>
    [Fact]
    public void FindReachedCeiling_SecondCaller_IsNotChargedForTheFirst()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            ClockAt("2026-08-19T10:00:00Z"));
        ledger.Charge(Caller, RecordNumber(1), recipientCount: 1);

        // Act
        var reached = ledger.FindReachedCeiling("another-key", recipientCount: 1);

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerMessages, ledger.FindReachedCeiling(Caller, recipientCount: 1));
        Assert.Null(reached);
    }

    /// <summary>A period that rolls over lifts the refusal, which is what a refused caller is told to wait for.</summary>
    [Fact]
    public void FindReachedCeiling_PeriodRolledOver_LiftsTheRefusal()
    {
        // Arrange
        var clock = ClockAt("2026-08-19T10:00:00Z");
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromHours(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            clock);
        ledger.Charge(Caller, RecordNumber(1), recipientCount: 1);

        // Act
        var withinThePeriod = ledger.FindReachedCeiling(Caller, recipientCount: 1);
        clock.Advance(TimeSpan.FromHours(1));
        var afterTheRollOver = ledger.FindReachedCeiling(Caller, recipientCount: 1);

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerMessages, withinThePeriod);
        Assert.Null(afterTheRollOver);
    }

    /// <summary>A period counting the most callers it can hold refuses one it is not already counting rather than growing.</summary>
    [Fact]
    public void FindReachedCeiling_MoreCallersThanOnePeriodHolds_RefusesTheCallerItCannotCount()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 10, maxRecipientsPerCaller: 0),
            ClockAt("2026-08-19T10:00:00Z"));

        for (var caller = 0; caller < AuthoredSendUsageLedger.MaximumCallersPerPeriod; caller++)
        {
            ledger.Charge(
                string.Create(CultureInfo.InvariantCulture, $"caller-{caller}"),
                RecordNumber(caller + 1),
                recipientCount: 1);
        }

        // Act
        var reached = ledger.FindReachedCeiling("one-caller-too-many", recipientCount: 1);

        // Assert
        Assert.Equal(AuthoredSendCeiling.CallerMessages, reached);
        Assert.Null(ledger.FindReachedCeiling("caller-0", recipientCount: 1));
    }

    /// <summary>A principal nothing can be counted against is a defect in whoever asked rather than a refusal.</summary>
    [Fact]
    public void FindReachedCeiling_CallerNamingNobody_IsADefect()
    {
        // Arrange
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            ClockAt("2026-08-19T10:00:00Z"));

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ledger.FindReachedCeiling(" ", recipientCount: 1));
    }

    private static FakeTimeProvider ClockAt(string instant) =>
        new(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));

    private static OutgoingEmailId RecordNumber(int number) =>
        OutgoingEmailId.Create(new Guid(number, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]));
}
