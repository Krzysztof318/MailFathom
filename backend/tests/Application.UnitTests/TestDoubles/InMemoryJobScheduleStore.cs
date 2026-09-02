// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Scheduling;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>What each recurring dispatch has already done, keyed by identity exactly as the table is.</summary>
internal sealed class InMemoryJobScheduleStore : IJobScheduleStore
{
    private readonly Dictionary<string, JobScheduleState> states = new(StringComparer.Ordinal);
    private readonly List<JobScheduleState> saves = [];

    /// <summary>Gets every state that was written, in order, which is what proves a schedule advanced.</summary>
    internal IReadOnlyList<JobScheduleState> Saves => this.saves;

    /// <summary>Puts a schedule's state in place without going through a pass.</summary>
    /// <param name="state">The state to record.</param>
    internal void Arrange(JobScheduleState state) => this.states[state.Id.Value] = state;

    /// <summary>Reads the state a schedule currently has.</summary>
    /// <param name="id">The schedule to read.</param>
    /// <returns>The state, or <see langword="null" /> when the schedule has none.</returns>
    internal JobScheduleState? Find(JobScheduleId id) => this.states.GetValueOrDefault(id.Value);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<string, JobScheduleState>> ReadAsync(
        IReadOnlyCollection<JobScheduleId> ids,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, JobScheduleState>>(
            ids.Select(id => this.states.GetValueOrDefault(id.Value))
                .OfType<JobScheduleState>()
                .ToDictionary(state => state.Id.Value, StringComparer.Ordinal));

    /// <inheritdoc />
    public Task SaveAsync(JobScheduleState state, CancellationToken cancellationToken)
    {
        this.states[state.Id.Value] = state;
        this.saves.Add(state);

        return Task.CompletedTask;
    }
}
