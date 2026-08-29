// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Search;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A ranked list that records every act the Mail space hands through to it.</summary>
internal sealed class StubMailSearch : IMailSearch
{
    internal StubMailSearch(params MessageRow[] rows)
    {
        IImmutableList<MessageRow> drawn = [.. rows];
        this.Results = Feed.Async(_ => ValueTask.FromResult(drawn)).AsListFeed();
        this.Recent = Feed.Async(_ => ValueTask.FromResult<IImmutableList<RecentMailSearch>>([])).AsListFeed();
        this.Reading = Feed.Async(_ => ValueTask.FromResult(MailSearchReading.Nothing));
        this.PagingFailed = Feed.Async(_ => ValueTask.FromResult(false));
    }

    internal int Opens { get; private set; }

    internal int Closes { get; private set; }

    internal int ScopeUses { get; private set; }

    internal int Searches { get; private set; }

    internal int Pages { get; private set; }

    internal int Widens { get; private set; }

    internal List<MessageRow> Opened { get; } = [];

    internal List<RecentMailSearch> Repeated { get; } = [];

    internal List<MailSearchFilter> Cleared { get; } = [];

    public IState<bool> IsOpen { get; } = State.Value(new object(), () => false);

    public IState<string> Query { get; } = State.Value(new object(), () => string.Empty);

    public IState<string> Account { get; } = State.Value(new object(), () => string.Empty);

    public IState<string> Folder { get; } = State.Value(new object(), () => string.Empty);

    public IState<string> Sender { get; } = State.Value(new object(), () => string.Empty);

    public IState<string> Recipient { get; } = State.Value(new object(), () => string.Empty);

    public IState<DateTimeOffset> ReceivedOnOrAfter { get; } = State<DateTimeOffset>.Empty(new object());

    public IState<DateTimeOffset> ReceivedBefore { get; } = State<DateTimeOffset>.Empty(new object());

    public IState<bool> Unread { get; } = State<bool>.Empty(new object());

    public IState<bool> Flagged { get; } = State<bool>.Empty(new object());

    public IState<bool> HasAttachments { get; } = State<bool>.Empty(new object());

    public IListFeed<MessageRow> Results { get; }

    public IListFeed<RecentMailSearch> Recent { get; }

    public IFeed<MailSearchReading> Reading { get; }

    public IFeed<bool> PagingFailed { get; }

    public ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        this.Opens++;
        return ValueTask.CompletedTask;
    }

    public ValueTask CloseAsync(CancellationToken cancellationToken)
    {
        this.Closes++;
        return ValueTask.CompletedTask;
    }

    public ValueTask UseCurrentScopeAsync(CancellationToken cancellationToken)
    {
        this.ScopeUses++;
        return ValueTask.CompletedTask;
    }

    public ValueTask SearchAsync(CancellationToken cancellationToken)
    {
        this.Searches++;
        return ValueTask.CompletedTask;
    }

    public ValueTask ShowMoreAsync(CancellationToken cancellationToken)
    {
        this.Pages++;
        return ValueTask.CompletedTask;
    }

    public ValueTask WidenAsync(CancellationToken cancellationToken)
    {
        this.Widens++;
        return ValueTask.CompletedTask;
    }

    public ValueTask OpenResultAsync(MessageRow result, CancellationToken cancellationToken)
    {
        this.Opened.Add(result);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetUnreadAsync(bool? value, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask SetFlaggedAsync(bool? value, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask SetHasAttachmentsAsync(bool? value, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask ClearFilterAsync(MailSearchFilter filter, CancellationToken cancellationToken)
    {
        this.Cleared.Add(filter);
        return ValueTask.CompletedTask;
    }

    public ValueTask RepeatAsync(RecentMailSearch recent, CancellationToken cancellationToken)
    {
        this.Repeated.Add(recent);
        return ValueTask.CompletedTask;
    }
}
