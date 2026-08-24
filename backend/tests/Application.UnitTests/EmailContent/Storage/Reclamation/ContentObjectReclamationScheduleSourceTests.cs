// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage.Reclamation;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;
using Xunit;

namespace MailFathom.Application.UnitTests.EmailContent.Storage.Reclamation;

/// <summary>Covers the one recurring sweep a deployment storing mail in a bucket declares.</summary>
public sealed class ContentObjectReclamationScheduleSourceTests
{
    /// <summary>One schedule for the whole deployment, because it sweeps one bucket and an object gives no account away.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AConfiguredInterval_DeclaresOneSweepBelongingToNoAccount()
    {
        // Arrange
        var recurrence = RecurrenceOf("Every 06:00:00");
        var source = new ContentObjectReclamationScheduleSource(recurrence);

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        var declared = Assert.Single(schedules);
        Assert.Null(declared.AccountId);
        Assert.Equal(JobType.ReclaimContentObjects, declared.Payload.JobType);
        Assert.Same(recurrence, declared.Recurrence);
    }

    /// <summary>The dispatched segment begins the listing, and the chain after it is what carries the rest.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AConfiguredInterval_DispatchesTheSegmentThatBeginsTheListing()
    {
        // Arrange
        var recurrence = RecurrenceOf("Every 06:00:00");
        var source = new ContentObjectReclamationScheduleSource(recurrence);

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        var payload = Assert.IsType<ReclaimContentObjectsJobPayload>(schedules[0].Payload);
        Assert.Null(payload.ResumeFrom);
        Assert.Null(payload.SweepId);
    }

    /// <summary>The identity keys the schedule's durable state, so it has to mean the same thing on every instance.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_AConfiguredInterval_KeysTheScheduleByOneDeploymentWideIdentity()
    {
        // Arrange
        var recurrence = RecurrenceOf("Daily at 03:00");
        var source = new ContentObjectReclamationScheduleSource(recurrence);

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("content-object-reclamation", schedules[0].Id.Value);
    }

    [Fact]
    public void Construction_WithoutARecurrence_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() => new ContentObjectReclamationScheduleSource(null!));

    private static JobRecurrence RecurrenceOf(string declaration)
    {
        Assert.True(JobRecurrence.TryParse(declaration, out var recurrence, out _));

        return recurrence!;
    }
}
