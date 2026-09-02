// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The one move of stored content a deployment may have, held exactly as the single-row table holds it.</summary>
internal sealed class InMemoryStoredContentMoveRunStore : IStoredContentMoveRunStore
{
    private readonly List<StoredContentMoveRun> saves = [];
    private Func<StoredContentMoveRun, StoredContentMoveRun>? decideOnRead;
    private int decisionReadNumber;
    private int readCount;

    /// <summary>Gets every state the move was saved in, which is what proves a pass committed what it accounts for.</summary>
    internal IReadOnlyList<StoredContentMoveRun> Saves => this.saves;

    /// <summary>Gets the move as it now stands, or <see langword="null" /> when the deployment has never had one.</summary>
    internal StoredContentMoveRun? Current { get; private set; }

    /// <summary>Puts a move in front of the deployment without going through the control.</summary>
    /// <param name="arranged">The move to record.</param>
    internal void Arrange(StoredContentMoveRun arranged) => this.Current = arranged;

    /// <summary>Changes the recorded move just before one numbered read, which is how a decision taken mid-pass is arranged.</summary>
    /// <param name="readNumber">Which read of the move the change lands in front of, counted from one.</param>
    /// <param name="decide">What the move becomes, applied to whatever is recorded when that read happens.</param>
    /// <remarks>
    /// Read one is always the pass finding out whether to carry at all. What read two is depends on whether it carried
    /// anything: a pass that carries a payload reads the move again between payloads, to see whether an operator has
    /// paused it since, so read two is that check and the commit's own read comes after it; a pass that carries nothing
    /// never reaches that check, and read two is the commit's. So a decision arranged on read two lands mid-pass on a
    /// walk with payloads in front of it and inside the commit on one without, which is the difference between a pass
    /// that stops after the payload in flight and a pass that does its work and finds the move changed underneath it.
    /// </remarks>
    internal void ArrangeDecisionOnRead(int readNumber, Func<StoredContentMoveRun, StoredContentMoveRun> decide)
    {
        this.decisionReadNumber = readNumber;
        this.decideOnRead = decide;
    }

    /// <inheritdoc />
    public Task<StoredContentMoveRun?> FindAsync(CancellationToken cancellationToken)
    {
        this.readCount++;

        if (this.readCount == this.decisionReadNumber && this.decideOnRead is { } decide && this.Current is { } recorded)
        {
            this.decideOnRead = null;
            this.Current = decide(recorded);
        }

        return Task.FromResult(this.Current);
    }

    /// <inheritdoc />
    public Task SaveAsync(IPersistenceSession session, StoredContentMoveRun saved, CancellationToken cancellationToken)
    {
        this.Current = saved;
        this.saves.Add(saved);

        return Task.CompletedTask;
    }
}
