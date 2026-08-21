// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves what a schedule's durable state gets from PostgreSQL rather than from its own code.</summary>
/// <remarks>
/// A recurring dispatch decides what to do about one schedule by reading a row it wrote on an earlier pass, and it
/// writes that row back through an upsert rather than by choosing between an insert and an update. Both halves are
/// structural: a conflict target naming the wrong column, or a read that stopped matching the identity it was keyed
/// by, would pass a unit test against a hand-written fake and would reach an operator as a schedule that seeds itself
/// again on every pass — which is a mailbox walk that never happens, because a seeded schedule dispatches nothing.
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedJobScheduleStoreTests(MailFathomOrchestrationFixture orchestration)
{
    private static readonly DateTimeOffset ObservedFrom = new(2026, 8, 13, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A schedule is written once and advanced afterwards, and the second write has to be the same statement.</summary>
    [Fact]
    public async Task SaveAsync_AScheduleWrittenAndThenAdvanced_KeepsOneRowCarryingTheLatestOccasion()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scheduleId = JobScheduleId.Create($"mail-rules:integration:{Guid.CreateVersion7():N}");
        var occurrence = ObservedFrom.AddDays(1);

        // Act
        await SaveAsync(
            services,
            new JobScheduleState { Id = scheduleId, ObservedFrom = ObservedFrom },
            cancellationToken);
        await SaveAsync(
            services,
            new JobScheduleState
            {
                Id = scheduleId,
                ObservedFrom = ObservedFrom,
                LastOccurrenceAt = occurrence,
                LastDispatchedJobId = JobId.Create(Guid.CreateVersion7()),
            },
            cancellationToken);

        // Assert
        var states = await ReadAsync(services, scheduleId, cancellationToken);
        var state = Assert.Single(states).Value;
        Assert.Equal(ObservedFrom, state.ObservedFrom);
        Assert.Equal(occurrence, state.LastOccurrenceAt);
        Assert.Equal(occurrence, state.CountedFrom);
        Assert.NotNull(state.LastDispatchedJobId);
    }

    /// <summary>A schedule nothing has written is absent rather than empty, which is what the first pass seeds against.</summary>
    [Fact]
    public async Task ReadAsync_AScheduleNoPassHasWritten_AnswersWithNothingForIt()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var scheduleId = JobScheduleId.Create($"mail-rules:integration:{Guid.CreateVersion7():N}");

        // Act
        var states = await ReadAsync(services, scheduleId, cancellationToken);

        // Assert
        Assert.Empty(states);
    }

    private static Task<bool> SaveAsync(
        OrchestratedMailFathomServices services,
        JobScheduleState state,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IJobScheduleStore>().SaveAsync(state, token);

                return true;
            },
            cancellationToken);

    private static Task<IReadOnlyDictionary<string, JobScheduleState>> ReadAsync(
        OrchestratedMailFathomServices services,
        JobScheduleId scheduleId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IJobScheduleStore>().ReadAsync([scheduleId], token),
            cancellationToken);
}
