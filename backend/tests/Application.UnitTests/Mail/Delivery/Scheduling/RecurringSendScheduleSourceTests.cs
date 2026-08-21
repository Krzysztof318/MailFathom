// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery.Scheduling;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Scheduling;

/// <summary>Covers which declarations reach the dispatch mechanism, and under which identity.</summary>
public sealed class RecurringSendScheduleSourceTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Declared = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An active declaration is dispatched under a key naming what declared it and which declaration it is.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AnActiveDeclaration_DeclaresOneDispatchUnderItsOwnIdentity()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore();
        var declaration = store.Publish(DeclarationOf("Daily at 09:00 Europe/Warsaw"));
        var source = new RecurringSendScheduleSource(store);

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        var schedule = Assert.Single(schedules);
        Assert.Equal($"recurring-send:{declaration.Id}", schedule.Id.Value);
        Assert.Equal(Account, schedule.AccountId);
        Assert.Equal(JobType.SendRecurringOccurrence, schedule.Payload.JobType);
    }

    /// <summary>The pass reads a bounded page rather than everything that repeats, which is what keeps every pass proportionate.</summary>
    /// <remarks>
    /// A deployment holding more repetitions than one pass can carry is a deployment with something wrong with it, so
    /// the bound is a ceiling rather than a page an unbounded read would eventually reach the end of. It is asserted
    /// here because nothing else names it: the store is handed a number, and which number is the decision.
    /// </remarks>
    [Fact]
    public async Task ReadSchedulesAsync_AnyPass_ReadsNoMoreDeclarationsThanTheBoundAllows()
    {
        // Arrange
        var store = Substitute.For<IRecurringSendStore>();
        store.ReadActiveAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        var source = new RecurringSendScheduleSource(store);

        // Act
        await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        await store.Received(1).ReadActiveAsync(
            RecurringSendBounds.MaximumActiveDeclarations,
            Arg.Any<CancellationToken>());
    }

    /// <summary>A stopped declaration declares nothing at all, including the occasion it would have produced next.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_ADeclarationThatWasStopped_DeclaresNothing()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore();
        store.Publish(DeclarationOf("Daily at 09:00") with { CancelledAt = Declared });
        var source = new RecurringSendScheduleSource(store);

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(schedules);
    }

    /// <summary>
    /// A stored schedule nothing can parse is left out rather than raised over, so one damaged row cannot stop every
    /// other repetition this deployment holds.
    /// </summary>
    [Fact]
    public async Task ReadSchedulesAsync_AStoredScheduleThatNoLongerParses_IsLeftOutWithoutStoppingTheRest()
    {
        // Arrange
        var store = new InMemoryRecurringSendStore();
        store.Publish(DeclarationOf("every so often"));
        var readable = store.Publish(DeclarationOf("Every 01:00:00") with
        {
            Id = RecurringSendId.Create(Guid.CreateVersion7()),
            Requester = OutgoingEmailRequester.Command("declare-2"),
        });
        var source = new RecurringSendScheduleSource(store);

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal($"recurring-send:{readable.Id}", Assert.Single(schedules).Id.Value);
    }

    /// <summary>A deployment that declared nothing costs the dispatch nothing, which is what every deployment did before.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_NoDeclarationAtAll_DeclaresNothing()
    {
        // Arrange
        var source = new RecurringSendScheduleSource(new InMemoryRecurringSendStore());

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(schedules);
    }

    private static RecurringSend DeclarationOf(string schedule)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        return new RecurringSend
        {
            Id = RecurringSendId.Create(Guid.CreateVersion7()),
            AccountId = Account,
            Requester = OutgoingEmailRequester.Command("declare-1"),
            Recipients = [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)],
            Schedule = schedule,
            DraftByteLength = 64,
            DeclaredAt = Declared,
        };
    }
}
