// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Mailboxes;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A tree that answers with what a test handed it and records what was asked of it.</summary>
/// <remarks>
/// The frame's model hands every one of these on rather than deciding any of them, so what its tests need is a tree
/// that says whether it was reached — the shape of a real one is asserted where it is built.
/// </remarks>
internal sealed class StubMailboxTree : IMailboxTree
{
    private readonly IImmutableList<MailboxRow> rows;

    /// <summary>Builds a tree drawing the rows a test states.</summary>
    /// <param name="rows">What the tree answers with.</param>
    internal StubMailboxTree(params MailboxRow[] rows)
    {
        this.rows = [.. rows];
        this.Rows = Feed.Async(_ => ValueTask.FromResult(this.rows)).AsListFeed();
        this.SynchronizationPaused = Feed.Async(_ => ValueTask.FromResult(this.Paused));
    }

    /// <summary>Gets or sets whether the deployment behind this tree has stopped refreshing.</summary>
    internal bool Paused { get; set; }

    /// <summary>Gets the keys the tree was asked to open or close, in order.</summary>
    internal List<string> Toggled { get; } = [];

    /// <summary>Gets the rows the tree was asked to narrow to, in order.</summary>
    internal List<MailboxRow?> Selected { get; } = [];

    /// <summary>Gets how many times the tree was asked to read the deployment again.</summary>
    internal int Asks { get; private set; }

    /// <inheritdoc />
    public IListFeed<MailboxRow> Rows { get; }

    /// <inheritdoc />
    public IFeed<bool> SynchronizationPaused { get; }

    /// <inheritdoc />
    public ValueTask ToggleAsync(string key, CancellationToken cancellationToken)
    {
        this.Toggled.Add(key);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask SelectAsync(MailboxRow? row, CancellationToken cancellationToken)
    {
        this.Selected.Add(row);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask AskAgainAsync(CancellationToken cancellationToken)
    {
        this.Asks++;

        return ValueTask.CompletedTask;
    }
}
