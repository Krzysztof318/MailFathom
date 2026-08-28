// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Mail;
using MailFathom.Client.Backend.Threads;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.Session;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Threads;

/// <summary>The conversation as one deployment pages it, and as one person has opened it.</summary>
/// <remarks>
/// <para>
/// It is read off the session and off what the message list has selected, which is one subscription and one act for the
/// person: selecting a message in the mail space is how a conversation is reached there, and the session already asks
/// the deployment again when the signed-in identity changes, when the client is pointed somewhere else, and when a lost
/// connection comes back. Nothing here retries on top of that — the root instructions refuse nested retry storms.
/// </para>
/// <para>
/// Everything else that reaches a conversation names one instead. A search result and a citation both arrive at one
/// message inside an exchange, so <see cref="OpenAsync" /> takes the message beside the conversation and the screen
/// opens there rather than at the beginning.
/// </para>
/// <para>
/// What each message added arrived with the conversation, so a thread of thirty messages is one request. The whole of a
/// message — its quoted history with it — is a read of its own, made only for the message somebody asked it of, which
/// is what keeps opening a conversation from being thirty requests and keeps the eighth reply from redrawing the seven
/// above it.
/// </para>
/// </remarks>
internal sealed class DeploymentMailThread : IMailThread
{
    /// <summary>How many messages one page of a conversation holds.</summary>
    /// <remarks>
    /// Stated rather than left to the deployment's default, because the default is a tool's page size and this is a
    /// screen's: a conversation is read from its beginning and most of them end inside one page, so a page a screenful
    /// short of the whole exchange would be a request per scroll. It is within what the surface accepts.
    /// </remarks>
    internal const int PageSize = 50;

    private readonly DeploymentClient deployment;
    private readonly IClientSession session;
    private readonly IMailAttachmentSaver attachmentSaver;
    private readonly IStringLocalizer words;
    private readonly IState<ThreadOpening> opened;
    private readonly IState<int> asked;
    private readonly IState<bool> pagingFailed;
    private readonly IState<IImmutableDictionary<string, ThreadMessageDetail>> disclosed;
    private readonly IState<ThreadWindow> loaded;
    private readonly ConcurrentDictionary<MailAttachmentRequest, CancellationTokenSource> attachmentDownloads = [];

    /// <summary>Initializes the conversation over what serves it, what decides it may be read, and what opens one.</summary>
    /// <param name="deployment">Where a page of a conversation is asked for.</param>
    /// <param name="session">What the deployment allows this caller, and whether it can be reached at all.</param>
    /// <param name="messages">The list whose selection is how a conversation is reached in the mail space.</param>
    /// <param name="attachmentSaver">Lets the reader choose where one requested attachment is written.</param>
    /// <param name="words">Where the sentences a conversation is composed from come from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DeploymentMailThread(
        DeploymentClient deployment,
        IClientSession session,
        IMessageList messages,
        IMailAttachmentSaver attachmentSaver,
        IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(attachmentSaver);
        ArgumentNullException.ThrowIfNull(words);

        this.deployment = deployment;
        this.session = session;
        this.attachmentSaver = attachmentSaver;
        this.words = words;

        // Held as a state rather than read as the session's own feed, for the reason every other reader of it holds
        // one: a feed is read from the start by whoever subscribes, and each projection below would otherwise be a
        // reader of its own.
        var standing = State.FromFeed(this, session.Standing);

        this.opened = State.Value(this, () => ThreadOpening.Nothing);
        this.asked = State.Value(this, () => 0);
        this.pagingFailed = State.Value(this, () => false);
        this.disclosed = State.Value(
            this,
            () => (IImmutableDictionary<string, ThreadMessageDetail>)ImmutableDictionary<string, ThreadMessageDetail>
                .Empty
                .WithComparers(StringComparer.Ordinal));

        this.loaded = State.FromFeed(
            this,
            Feed.Combine(standing, this.opened, this.asked).SelectAsync(this.ReadAsync));

        this.Reading = this.loaded.Select(window => ThreadShape.Header(window, this.words));
        this.Messages = Feed.Combine(this.loaded, this.disclosed).Select(this.Draw).AsListFeed();
        this.HasMoreMessages = this.loaded.Select(static window => window.HasMore);
        this.PagingFailed = this.pagingFailed;

        // What makes selecting a message in the list opening its conversation. MVUX owns the subscription's lifetime,
        // so it ends with this instance.
        messages.Chosen.ForEach(this.FollowAsync);
    }

    /// <inheritdoc />
    public IFeed<ThreadReading> Reading { get; }

    /// <inheritdoc />
    public IListFeed<ThreadMessageRow> Messages { get; }

    /// <inheritdoc />
    public IFeed<bool> HasMoreMessages { get; }

    /// <inheritdoc />
    public IFeed<bool> PagingFailed { get; }

    /// <inheritdoc />
    public async ValueTask OpenAsync(Guid? threadId, Guid? atMessage, CancellationToken cancellationToken)
    {
        var opening = threadId is { } conversation
            ? new ThreadOpening(conversation, atMessage)
            : ThreadOpening.Nothing;

        // Written before the opening rather than after it, so the conversation that arrives is drawn with nothing the
        // one before it had opened. Both are states, so a value equal to what is held reaches nobody as a change.
        await this.disclosed.UpdateAsync(_ => Nothing, cancellationToken).ConfigureAwait(false);

        await this.opened.UpdateAsync(_ => opening, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask ToggleAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (await this.DetailOfAsync(key, cancellationToken).ConfigureAwait(false) is not { } detail)
        {
            return;
        }

        // Collapsing drops the whole message with the answer about its remote pictures, so an allowance never outlives
        // the expansion it was given during.
        var toggled = detail.Expanded ? ThreadMessageDetail.Collapsed : detail with { Expanded = true };

        await this.disclosed
            .UpdateAsync(held => (held ?? Nothing).SetItem(key, toggled), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask ShowWholeMessageAsync(string key, CancellationToken cancellationToken) =>
        this.ReadWholeAsync(key, remoteImages: false, cancellationToken);

    /// <inheritdoc />
    public ValueTask ShowRemoteContentAsync(string key, CancellationToken cancellationToken) =>
        this.ReadWholeAsync(key, remoteImages: true, cancellationToken);

    /// <inheritdoc />
    public async ValueTask SaveAttachmentAsync(
        MailAttachmentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = request.Message.ToString("D", CultureInfo.InvariantCulture);

        if (await this.DetailOfAsync(key, cancellationToken).ConfigureAwait(false) is not
            { Expanded: true, Message: { } message } detail)
        {
            return;
        }

        var attachment = message.Attachments.FirstOrDefault(held => held.Position == request.Position);

        if (attachment is null)
        {
            return;
        }

        using var download = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (!this.attachmentDownloads.TryAdd(request, download))
        {
            return;
        }

        var standing = MailAttachmentStanding.None;

        try
        {
            await this.WriteAttachmentStandingAsync(
                key,
                detail,
                request.Position,
                MailAttachmentStanding.Downloading,
                cancellationToken).ConfigureAwait(false);

            var saved = await this.attachmentSaver.SaveAsync(
                attachment,
                (destination, token) => this.deployment.DownloadMailAttachmentAsync(
                    request.Message,
                    request.Position,
                    attachment.SizeOctets,
                    destination,
                    token),
                download.Token).ConfigureAwait(false);

            standing = saved ? MailAttachmentStanding.Downloaded : MailAttachmentStanding.None;
        }
        catch (OperationCanceledException) when (download.IsCancellationRequested)
        {
            standing = MailAttachmentStanding.None;
        }
#pragma warning disable CA1031 // Every platform adapter failure must leave this per-item command in a rendered state.
        catch (Exception)
#pragma warning restore CA1031
        {
            standing = MailAttachmentStanding.Failed;
        }
        finally
        {
            this.attachmentDownloads.TryRemove(request, out _);
        }

        if (await this.DetailOfAsync(key, CancellationToken.None).ConfigureAwait(false) is
            { Expanded: true, Message: not null } current)
        {
            await this.WriteAttachmentStandingAsync(
                key,
                current,
                request.Position,
                standing,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void CancelAttachment(MailAttachmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (this.attachmentDownloads.TryGetValue(request, out var download))
        {
            download.Cancel();
        }
    }

    /// <inheritdoc />
    public async ValueTask ShowMoreAsync(CancellationToken cancellationToken)
    {
        if (await this.loaded.Value(cancellationToken).ConfigureAwait(false) is not { } window)
        {
            return;
        }

        if (window.ThreadId is not { } conversation || window.NextCursor is not { } cursor)
        {
            return;
        }

        DeploymentMailThreadPage page;

        try
        {
            page = await this.deployment
                .ReadMailThreadAsync(conversation, PageSize, cursor, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DeploymentFailure)
        {
            if (await this.StillLoadedAsync(window, cancellationToken).ConfigureAwait(false) is not null)
            {
                await this.pagingFailed.UpdateAsync(static _ => true, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        // Everything the read says afterwards is said about the conversation it was started for, so it is written only
        // while that conversation is still the one open: a page in flight when somebody opens another exchange is
        // answered by a screen that has already moved, and splicing it on would put one conversation's messages under
        // another's header.
        await this.loaded
            .UpdateAsync(held => held?.IsOf(window) is true ? held.Extended(page) : held, cancellationToken)
            .ConfigureAwait(false);

        if (await this.StillLoadedAsync(window, cancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        await this.pagingFailed.UpdateAsync(static _ => false, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask AskAgainAsync(CancellationToken cancellationToken)
    {
        this.session.Refresh();

        await this.asked.UpdateAsync(static asked => asked + 1, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>An empty set of disclosures, which is a conversation nobody has opened anything inside.</summary>
    private static IImmutableDictionary<string, ThreadMessageDetail> Nothing { get; } =
        ImmutableDictionary<string, ThreadMessageDetail>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>Opens the conversation of the one message the list has selected, and closes it for anything else.</summary>
    /// <remarks>
    /// One message names one conversation; several name none, because a question asked about four messages is not a
    /// conversation to read. A message nothing has placed in a conversation names none either, which is an ordinary
    /// state of mail this deployment has stored and not yet threaded.
    /// </remarks>
    private async ValueTask FollowAsync(IImmutableList<MessageRow>? chosen, CancellationToken cancellationToken)
    {
        var row = chosen is [var only] ? only : null;

        // The row writes its own identity as the invariant form of the message's, which is the same identity the
        // conversation names its messages by.
        var message = row is not null && Guid.TryParseExact(row.Key, "D", out var parsed) ? parsed : (Guid?)null;

        await this.OpenAsync(row?.ThreadId, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the first page of whatever conversation is open, once the session that decides it may be has arrived.</summary>
    /// <remarks>
    /// Neither the standing nor the counter is read: what they are here for is when this runs rather than what it asks
    /// for. A conversation nobody has opened is not a read at all, which is the state the screen starts in and the one
    /// selecting several messages returns it to.
    /// </remarks>
    private async ValueTask<ThreadWindow> ReadAsync(
        (SessionStanding Standing, ThreadOpening Opening, int Asked) trigger,
        CancellationToken cancellationToken)
    {
        var opening = trigger.Opening;

        if (opening.ThreadId is not { } conversation)
        {
            return ThreadWindow.Nothing;
        }

        await this.pagingFailed.UpdateAsync(static _ => false, cancellationToken).ConfigureAwait(false);

        var page = await this.deployment
            .ReadMailThreadAsync(conversation, PageSize, cursor: null, cancellationToken)
            .ConfigureAwait(false);

        return ThreadWindow.Opening(opening, page);
    }

    /// <summary>Reads the whole of one message, in the terms the reader asked for it.</summary>
    /// <remarks>
    /// The read is held against the conversation it was started under exactly as a page is: a message opened while one
    /// exchange was on the screen and answered after another has been belongs to neither, and drawing it would put one
    /// message's body under another conversation's messages.
    /// </remarks>
    private async ValueTask ReadWholeAsync(string key, bool remoteImages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!Guid.TryParseExact(key, "D", out var message))
        {
            return;
        }

        if (await this.loaded.Value(cancellationToken).ConfigureAwait(false) is not { IsOpen: true } window)
        {
            return;
        }

        if (await this.DetailOfAsync(key, cancellationToken).ConfigureAwait(false) is not { } detail)
        {
            return;
        }

        await this.WriteAsync(
            key,
            detail with
            {
                Expanded = true,
                IsReadingWholeMessage = true,
                WholeMessageFailed = false,
                RemoteImages = remoteImages,
            },
            cancellationToken).ConfigureAwait(false);

        if (detail.Message is null)
        {
            DeploymentMailMessageDetail messageDetail;

            try
            {
                messageDetail = await this.deployment
                    .ReadMailMessageAsync(message, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DeploymentFailure)
            {
                await this.FailWholeReadAsync(key, window, remoteImages, cancellationToken).ConfigureAwait(false);

                return;
            }

            if (await this.StillLoadedAsync(window, cancellationToken).ConfigureAwait(false) is null
                || await this.DetailOfAsync(key, cancellationToken).ConfigureAwait(false) is not { Expanded: true } described)
            {
                return;
            }

            await this.WriteAsync(key, described with { Message = messageDetail }, cancellationToken).ConfigureAwait(false);
        }

        MailBodyReading? whole;

        try
        {
            var body = await this.deployment
                .ReadMailBodyAsync(message, remoteImages, cancellationToken)
                .ConfigureAwait(false);

            whole = MailBodyReading.Of(body, this.words);
        }
        catch (DeploymentFailure)
        {
            whole = null;
        }

        if (await this.StillLoadedAsync(window, cancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        // Read again rather than written onto what was held when the read began: collapsing a message drops the whole
        // of it, and a reader who closed the message while it was on its way would otherwise have it opened back up by
        // an answer to a question they had already withdrawn.
        if (await this.DetailOfAsync(key, cancellationToken).ConfigureAwait(false) is not { Expanded: true } current)
        {
            return;
        }

        await this.WriteAsync(
            key,
            current with
            {
                WholeMessage = whole,
                IsReadingWholeMessage = false,
                WholeMessageFailed = whole is null,
                RemoteImages = remoteImages,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask FailWholeReadAsync(
        string key,
        ThreadWindow window,
        bool remoteImages,
        CancellationToken cancellationToken)
    {
        if (await this.StillLoadedAsync(window, cancellationToken).ConfigureAwait(false) is null
            || await this.DetailOfAsync(key, cancellationToken).ConfigureAwait(false) is not { Expanded: true } current)
        {
            return;
        }

        await this.WriteAsync(
            key,
            current with
            {
                IsReadingWholeMessage = false,
                WholeMessageFailed = true,
                RemoteImages = remoteImages,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads how much of one message is shown, or nothing where the conversation does not hold it.</summary>
    private async ValueTask<ThreadMessageDetail?> DetailOfAsync(string key, CancellationToken cancellationToken)
    {
        if (await this.loaded.Value(cancellationToken).ConfigureAwait(false) is not { } window)
        {
            return null;
        }

        var message = window.Messages.FirstOrDefault(
            held => string.Equals(ThreadShape.KeyOf(held), key, StringComparison.Ordinal));

        if (message is null)
        {
            return null;
        }

        var held = await this.disclosed.Value(cancellationToken).ConfigureAwait(false) ?? Nothing;

        return ThreadShape.DetailOf(message, held, ThreadShape.OpenedKey(window));
    }

    private ValueTask WriteAsync(string key, ThreadMessageDetail detail, CancellationToken cancellationToken) =>
        this.disclosed.UpdateAsync(held => (held ?? Nothing).SetItem(key, detail), cancellationToken);

    private ValueTask WriteAttachmentStandingAsync(
        string key,
        ThreadMessageDetail detail,
        int position,
        MailAttachmentStanding standing,
        CancellationToken cancellationToken) =>
        this.WriteAsync(
            key,
            detail with { Attachments = detail.Attachments.SetItem(position, standing) },
            cancellationToken);

    /// <summary>Reads the loaded conversation back, where it is still the one a read was started for.</summary>
    private async ValueTask<ThreadWindow?> StillLoadedAsync(
        ThreadWindow window,
        CancellationToken cancellationToken)
    {
        var current = await this.loaded.Value(cancellationToken).ConfigureAwait(false);

        return current?.IsOf(window) is true ? current : null;
    }

    private IImmutableList<ThreadMessageRow> Draw(
        (ThreadWindow Window, IImmutableDictionary<string, ThreadMessageDetail> Disclosed) drawn) =>
        ThreadShape.Messages(drawn.Window ?? ThreadWindow.Nothing, drawn.Disclosed ?? Nothing, this.words);
}
