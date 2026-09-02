// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The one re-derivation run a scope may have, keyed by the scope exactly as the table is.</summary>
internal sealed class InMemoryStoredMailRederivationRunStore : IStoredMailRederivationRunStore
{
    private readonly Dictionary<string, StoredMailRederivationRun> runs = new(StringComparer.Ordinal);
    private readonly List<StoredMailRederivationRun> saves = [];

    /// <summary>Gets every state a run was saved in, which is what proves a segment committed the counts it accounts for.</summary>
    internal IReadOnlyList<StoredMailRederivationRun> Saves => this.saves;

    /// <summary>Reads the run recorded for a scope, whether or not it is still outstanding.</summary>
    /// <param name="scope">The scope to read.</param>
    /// <returns>The run, or <see langword="null" /> when the scope has never had one.</returns>
    internal StoredMailRederivationRun? Find(StoredMailScope scope) => this.runs.GetValueOrDefault(KeyOf(scope));

    /// <summary>Puts a run in front of a scope without going through the request path.</summary>
    /// <param name="run">The run to record.</param>
    internal void Arrange(StoredMailRederivationRun run) => this.runs[KeyOf(run.Scope)] = run;

    /// <inheritdoc />
    public Task<StoredMailRederivationRun?> FindAsync(StoredMailScope scope, CancellationToken cancellationToken) =>
        Task.FromResult(this.runs.GetValueOrDefault(KeyOf(scope)));

    /// <inheritdoc />
    public Task SaveAsync(
        IPersistenceSession session,
        StoredMailRederivationRun run,
        CancellationToken cancellationToken)
    {
        this.runs[KeyOf(run.Scope)] = run;
        this.saves.Add(run);

        return Task.CompletedTask;
    }

    private static string KeyOf(StoredMailScope scope) => $"{scope.Account.Id.Value}\0{scope.Folder?.Value}";
}
