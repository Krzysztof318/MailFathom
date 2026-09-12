// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
        Assert.Null(declared.Account);
        Assert.Equal(JobType.ReclaimContentObjects, declared.Payload.JobType);
        Assert.Same(recurrence, declared.Recurrence);
    }

    /// <summary>The dispatched segment begins the listing and already names the sweep the chain after it carries.</summary>
    /// <remarks>
    /// Naming it here is what keeps the identity out of the attempt. The document the enqueue commits is what every
    /// execution of that leased segment reads, so a repeated attempt hands the rest of the sweep on under one key
    /// instead of minting a second sweep and forking the chain.
    /// </remarks>
    [Fact]
    public async Task ReadSchedulesAsync_AConfiguredInterval_DispatchesTheSegmentThatBeginsTheListingAndNamesItsSweep()
    {
        // Arrange
        var recurrence = RecurrenceOf("Every 06:00:00");
        var source = new ContentObjectReclamationScheduleSource(recurrence);

        // Act
        var schedules = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        var payload = Assert.IsType<ReclaimContentObjectsJobPayload>(schedules[0].Payload);
        Assert.Null(payload.ResumeFrom);
        Assert.Equal(0, payload.Segment);
        Assert.NotEmpty(payload.SweepId);
    }

    /// <summary>Two occasions are two sweeps, or one would be answered with the chain the other was already walking.</summary>
    [Fact]
    public async Task ReadSchedulesAsync_ReadTwice_NamesASweepOfItsOwnEachTime()
    {
        // Arrange
        var source = new ContentObjectReclamationScheduleSource(RecurrenceOf("Every 06:00:00"));

        // Act
        var one = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);
        var another = await source.ReadSchedulesAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEqual(
            ((ReclaimContentObjectsJobPayload)one[0].Payload).SweepId,
            ((ReclaimContentObjectsJobPayload)another[0].Payload).SweepId);
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
