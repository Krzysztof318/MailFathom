// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Messages;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A list that answers with what a test handed it and records what was asked of it.</summary>
/// <remarks>
/// The Mail space hands every one of these on rather than deciding any of them, so what its tests need is a list that
/// says whether it was reached and under what arrangement — the behaviour of a real one is asserted where it is built.
/// </remarks>
internal sealed class StubMessageList : IMessageList
{
    private readonly IState<MessageListArrangement> arranged;

    /// <summary>Builds a list drawing the rows a test states.</summary>
    /// <param name="rows">What the list answers with.</param>
    internal StubMessageList(params MessageRow[] rows)
    {
        IImmutableList<MessageRow> drawn = [.. rows];

        this.Rows = Feed.Async(_ => ValueTask.FromResult(drawn)).AsListFeed();
        this.Chosen = State<IImmutableList<MessageRow>>.Empty(this);
        this.arranged = State.Value(this, () => MessageListArrangement.Default);
        this.Arrangement = this.arranged;
        this.HasMoreAfter = Feed.Async(_ => ValueTask.FromResult(this.More));
        this.HasMoreBefore = Feed.Async(_ => ValueTask.FromResult(this.Earlier));
        this.PagingFailed = Feed.Async(_ => ValueTask.FromResult(false));
    }

    /// <summary>Gets or sets whether the stub reports more mail after what is loaded.</summary>
    internal bool More { get; set; }

    /// <summary>Gets or sets whether the stub reports more mail before what is loaded.</summary>
    internal bool Earlier { get; set; }

    /// <summary>Gets how many times the list was asked for another page forward.</summary>
    internal int Forwards { get; private set; }

    /// <summary>Gets how many times the list was asked for another page backward.</summary>
    internal int Backwards { get; private set; }

    /// <summary>Gets how many times the list was asked to read the deployment again.</summary>
    internal int Asks { get; private set; }

    /// <summary>Gets every arrangement the list was asked to read under, in order.</summary>
    internal List<MessageListArrangement> Arranged { get; } = [];

    /// <inheritdoc />
    public IListFeed<MessageRow> Rows { get; }

    /// <inheritdoc />
    public IState<IImmutableList<MessageRow>> Chosen { get; }

    /// <inheritdoc />
    public IFeed<MessageListArrangement> Arrangement { get; }

    /// <inheritdoc />
    public IFeed<bool> HasMoreAfter { get; }

    /// <inheritdoc />
    public IFeed<bool> HasMoreBefore { get; }

    /// <inheritdoc />
    public IFeed<bool> PagingFailed { get; }

    /// <inheritdoc />
    public ValueTask ShowMoreAsync(CancellationToken cancellationToken)
    {
        this.Forwards++;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ShowEarlierAsync(CancellationToken cancellationToken)
    {
        this.Backwards++;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask ArrangeAsync(
        MessageListArrangement arrangement,
        CancellationToken cancellationToken)
    {
        this.Arranged.Add(arrangement);

        await this.arranged.UpdateAsync(_ => arrangement, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask AskAgainAsync(CancellationToken cancellationToken)
    {
        this.Asks++;

        return ValueTask.CompletedTask;
    }
}
