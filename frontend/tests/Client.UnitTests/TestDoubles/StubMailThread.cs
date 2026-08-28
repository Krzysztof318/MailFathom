// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.Presentation.Threads;

namespace MailFathom.Client.UnitTests.TestDoubles;

/// <summary>A conversation that answers with what a test handed it and records what was asked of it.</summary>
/// <remarks>
/// The Mail space hands every one of these on rather than deciding any of them, so what its tests need is a
/// conversation that says whether it was reached and with what — the behaviour of a real one is asserted where it is
/// built.
/// </remarks>
internal sealed class StubMailThread : IMailThread
{
    /// <summary>Builds a conversation drawing the messages a test states.</summary>
    /// <param name="messages">What the conversation answers with.</param>
    internal StubMailThread(params ThreadMessageRow[] messages)
    {
        IImmutableList<ThreadMessageRow> drawn = [.. messages];

        this.Reading = Feed.Async(_ => ValueTask.FromResult(ThreadReading.Nothing));
        this.Messages = Feed.Async(_ => ValueTask.FromResult(drawn)).AsListFeed();
        this.HasMoreMessages = Feed.Async(_ => ValueTask.FromResult(this.More));
        this.PagingFailed = Feed.Async(_ => ValueTask.FromResult(false));
    }

    /// <summary>Gets or sets whether the stub reports more of the conversation after what has been read.</summary>
    internal bool More { get; set; }

    /// <summary>Gets every conversation the stub was asked to open, in order.</summary>
    internal List<(Guid? ThreadId, Guid? AtMessage)> Opened { get; } = [];

    /// <summary>Gets every message the stub was asked to expand or collapse, in order.</summary>
    internal List<string> Toggled { get; } = [];

    /// <summary>Gets every message the stub was asked for the whole of, in order.</summary>
    internal List<string> Whole { get; } = [];

    /// <summary>Gets every message the stub was asked to read again with its remote pictures, in order.</summary>
    internal List<string> Remote { get; } = [];

    /// <summary>Gets every attachment the stub was asked to save, in order.</summary>
    internal List<MailAttachmentRequest> Saved { get; } = [];

    /// <summary>Gets every attachment the stub was asked to stop saving, in order.</summary>
    internal List<MailAttachmentRequest> Cancelled { get; } = [];

    /// <summary>Gets how many times the stub was asked for another page of the conversation.</summary>
    internal int Pages { get; private set; }

    /// <summary>Gets how many times the stub was asked to read the deployment again.</summary>
    internal int Asks { get; private set; }

    /// <inheritdoc />
    public IFeed<ThreadReading> Reading { get; }

    /// <inheritdoc />
    public IListFeed<ThreadMessageRow> Messages { get; }

    /// <inheritdoc />
    public IFeed<bool> HasMoreMessages { get; }

    /// <inheritdoc />
    public IFeed<bool> PagingFailed { get; }

    /// <inheritdoc />
    public ValueTask OpenAsync(Guid? threadId, Guid? atMessage, CancellationToken cancellationToken)
    {
        this.Opened.Add((threadId, atMessage));

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ToggleAsync(string key, CancellationToken cancellationToken)
    {
        this.Toggled.Add(key);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ShowWholeMessageAsync(string key, CancellationToken cancellationToken)
    {
        this.Whole.Add(key);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask ShowRemoteContentAsync(string key, CancellationToken cancellationToken)
    {
        this.Remote.Add(key);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask SaveAttachmentAsync(MailAttachmentRequest request, CancellationToken cancellationToken)
    {
        this.Saved.Add(request);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void CancelAttachment(MailAttachmentRequest request) => this.Cancelled.Add(request);

    /// <inheritdoc />
    public ValueTask ShowMoreAsync(CancellationToken cancellationToken)
    {
        this.Pages++;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask AskAgainAsync(CancellationToken cancellationToken)
    {
        this.Asks++;

        return ValueTask.CompletedTask;
    }
}
