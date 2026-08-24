// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    /// A pass reads the move twice — once to find out whether to carry it, and once inside the commit that records what
    /// it carried — so which of the two an operator's decision arrives before is the difference between a pass that does
    /// nothing and a pass that does its work and finds the move changed underneath it.
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
