// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>The message list as one deployment pages it, and as one person left it.</summary>
/// <remarks>
/// <para>
/// The list is read off the session and the workspace scope rather than beside them, which is one subscription and one
/// act for the person: the session already asks the deployment again when the signed-in identity changes, when the
/// client is pointed somewhere else, and when a lost connection comes back, and the list follows every one of those
/// without a second timer, a second retry curve, or a second button. The root instructions refuse nested retry storms,
/// and a list that retried on top of the session's own bounded attempts would be one.
/// </para>
/// <para>
/// It reloads on the <em>place</em> rather than on the scope, and that is load-bearing rather than an optimization: the
/// list writes what is selected back into the scope, so a list keyed on the whole scope would read a folder again every
/// time somebody clicked a row in it.
/// </para>
/// <para>
/// What is loaded is a bounded window over the timeline rather than everything that has been paged. Scrolling on takes
/// a page and drops the far one, and scrolling back asks for the dropped one again — which is why the request that
/// produced each page travels with it. The same value is what is written down when a folder is left, so returning is a
/// continuation rather than a jump to the top.
/// </para>
/// </remarks>
internal sealed class DeploymentMessageList : IMessageList
{
    /// <summary>How many rows one page holds.</summary>
    /// <remarks>
    /// Stated rather than left to the deployment's default, because the default is a tool's page size and this is a
    /// screen's: a list drawn a screenful at a time is a request per flick of a scroll. It is within what the surface
    /// accepts, and the window's own bound is what keeps the total loaded from following it upwards.
    /// </remarks>
    internal const int PageSize = 50;

    private readonly DeploymentClient deployment;
    private readonly IClientSession session;
    private readonly IWorkspace workspace;
    private readonly IMessageListMemory memory;
    private readonly TimeProvider clock;
    private readonly IStringLocalizer words;
    private readonly IState<MessageListArrangement> arranged;
    private readonly IState<int> asked;
    private readonly IState<bool> pagingFailed;
    private readonly IState<MessageWindow> loaded;

    private MessagePlace? openedPlace;

    /// <summary>Initializes the list over what serves it, where it is drawn from, and where it was left.</summary>
    /// <param name="deployment">Where a page of the owner's mail is asked for.</param>
    /// <param name="session">What the deployment allows this caller, and whether it can be reached at all.</param>
    /// <param name="workspace">The scope the list is drawn from and the selection it writes back into.</param>
    /// <param name="memory">Where the position and the arrangement outlive the folder being left and the run itself.</param>
    /// <param name="clock">What a message's date is written relative to.</param>
    /// <param name="words">Where the sentences a row is composed from come from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DeploymentMessageList(
        DeploymentClient deployment,
        IClientSession session,
        IWorkspace workspace,
        IMessageListMemory memory,
        TimeProvider clock,
        IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(words);

        this.deployment = deployment;
        this.session = session;
        this.workspace = workspace;
        this.memory = memory;
        this.clock = clock;
        this.words = words;

        // Held as a state rather than read as the session's own feed, for the reason every other reader of it holds
        // one: a feed is read from the start by whoever subscribes, and the projections below would otherwise each be
        // a reader of their own.
        var standing = State.FromFeed(this, session.Standing);

        // The place rather than the scope, so a row being clicked is not a folder being opened again.
        var place = workspace.Scope.Select(MessagePlace.Of);

        this.arranged = State.Value(this, () => MessageListArrangement.Default);
        this.pagingFailed = State.Value(this, () => false);

        // Why a counter beside the two triggers above: a session that answers a second time with the same grant is one
        // message MVUX does not republish, so a person pressing the button on a read that failed while the session was
        // fine would otherwise press something that did nothing. Arranging the list differently reaches the read the
        // same way, because a new arrangement invalidates every cursor held under the old one.
        this.asked = State.Value(this, () => 0);

        this.loaded = State.FromFeed(
            this,
            Feed.Combine(standing, place, this.asked).SelectAsync(this.OpenAsync));

        this.Chosen = State<IImmutableList<MessageRow>>.Empty(this);
        this.Rows = this.loaded.Select(this.Draw).AsListFeed().Selection(this.Chosen);
        this.Arrangement = this.arranged;
        this.HasMoreAfter = this.loaded.Select(static window => window.HasMoreAfter);
        this.HasMoreBefore = this.loaded.Select(static window => window.HasMoreBefore);
        this.PagingFailed = this.pagingFailed;

        // What makes the selection the application's rather than the control's. MVUX owns the subscription's lifetime,
        // so it ends with this instance.
        this.Chosen.ForEach(this.NarrowAsync);
    }

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
    public ValueTask ShowMoreAsync(CancellationToken cancellationToken) =>
        this.PageAsync(MailTimelinePageDirection.Forward, cancellationToken);

    /// <inheritdoc />
    public ValueTask ShowEarlierAsync(CancellationToken cancellationToken) =>
        this.PageAsync(MailTimelinePageDirection.Backward, cancellationToken);

    /// <inheritdoc />
    public async ValueTask ArrangeAsync(
        MessageListArrangement arrangement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arrangement);

        await this.arranged.UpdateAsync(_ => arrangement, cancellationToken).ConfigureAwait(false);

        await this.asked.UpdateAsync(static asked => asked + 1, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask AskAgainAsync(CancellationToken cancellationToken)
    {
        this.session.Refresh();

        await this.asked.UpdateAsync(static asked => asked + 1, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the first page of the place in force, once the session that decides whether it may be read has arrived.</summary>
    /// <remarks>
    /// Neither the standing nor the counter is read: what they are here for is when this runs rather than what it asks
    /// for. Which page is read is the place's — a place somebody is arriving at reopens where it was left, and a place
    /// they are already in is being read again on purpose and therefore from its leading end.
    /// </remarks>
    private async ValueTask<MessageWindow> OpenAsync(
        (SessionStanding Standing, MessagePlace Place, int Asked) trigger,
        CancellationToken cancellationToken)
    {
        var place = trigger.Place;
        var arriving = !place.Equals(this.openedPlace);
        var remembered = this.memory.Read(place.RememberedAs);

        var arrangement = arriving
            ? remembered.Arrangement
            : await this.arranged.Value(cancellationToken).ConfigureAwait(false) ?? MessageListArrangement.Default;

        this.openedPlace = place;

        await this.arranged.UpdateAsync(_ => arrangement, cancellationToken).ConfigureAwait(false);
        await this.pagingFailed.UpdateAsync(static _ => false, cancellationToken).ConfigureAwait(false);

        var page = await this.ReopeningAsync(
            place,
            arrangement,
            arriving ? remembered.Cursor : null,
            arriving ? remembered.Direction : MailTimelinePageDirection.Forward,
            cancellationToken).ConfigureAwait(false);

        var window = MessageWindow.Opening(place, arrangement, page);

        this.Remember(window);

        return window;
    }

    /// <summary>Reads a page from a remembered cursor, falling back to the leading end where the cursor is refused.</summary>
    /// <remarks>
    /// A cursor names the list it was taken from, so one written down under an arrangement this deployment no longer
    /// serves the same way — a folder re-indexed, a version that orders differently — is refused rather than honoured.
    /// That is a position to give up rather than an error to show: nobody typed it, and the list somebody asked for
    /// still exists. Only a cursor is retried this way, because it is the only value here this client did not compose.
    /// </remarks>
    private async ValueTask<MessagePage> ReopeningAsync(
        MessagePlace place,
        MessageListArrangement arrangement,
        string? cursor,
        MailTimelinePageDirection direction,
        CancellationToken cancellationToken)
    {
        if (cursor is null)
        {
            // Forward rather than the direction handed in: a read from the leading end has no row to read away from,
            // which the deployment refuses rather than answers, and the pair is only ever remembered together.
            return await this.ReadAsync(
                place,
                arrangement,
                cursor: null,
                MailTimelinePageDirection.Forward,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await this.ReadAsync(place, arrangement, cursor, direction, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DeploymentFailure refusal) when (refusal.Reason is DeploymentFailureReason.RequestRefused)
        {
            return await this.ReadAsync(
                place,
                arrangement,
                cursor: null,
                MailTimelinePageDirection.Forward,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Asks the deployment for one page and keeps the request that produced it.</summary>
    private async ValueTask<MessagePage> ReadAsync(
        MessagePlace place,
        MessageListArrangement arrangement,
        string? cursor,
        MailTimelinePageDirection direction,
        CancellationToken cancellationToken)
    {
        var answered = await this.deployment.ReadMailTimelineAsync(
            arrangement.QueryFor(place, cursor, direction, PageSize),
            cancellationToken).ConfigureAwait(false);

        return MessagePage.Of(answered, cursor, direction);
    }

    /// <summary>Takes one more page onto the window at the end being scrolled towards.</summary>
    /// <remarks>
    /// <para>
    /// A page that did not arrive is reported beside the list rather than as the list's own failure, because what is
    /// already drawn is still true: putting the whole list into an error state would take a folder's worth of mail off
    /// the screen over one request.
    /// </para>
    /// <para>
    /// Everything the read says afterwards is said about the reading it was started during, so each of them is written
    /// only while that reading is still the one loaded. A request in flight when somebody opens another folder — or asks
    /// for this one to be read again — is answered by a list that has already opened afresh, and letting the abandoned
    /// read finish onto it would splice a page onto a window that holds nothing of it, or move an indicator over a
    /// reading it never saw.
    /// </para>
    /// </remarks>
    private async ValueTask PageAsync(
        MailTimelinePageDirection direction,
        CancellationToken cancellationToken)
    {
        if (await this.loaded.Value(cancellationToken).ConfigureAwait(false) is not { } window)
        {
            return;
        }

        var cursor = direction is MailTimelinePageDirection.Forward
            ? window.ForwardCursor
            : window.BackwardCursor;

        if (cursor is null)
        {
            return;
        }

        MessagePage page;

        try
        {
            page = await this.ReadAsync(window.Place, window.Arrangement, cursor, direction, cancellationToken)
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

        await this.loaded.UpdateAsync(
            loaded => loaded?.Extended(page, direction, window),
            cancellationToken).ConfigureAwait(false);

        if (await this.StillLoadedAsync(window, cancellationToken).ConfigureAwait(false) is not { } extended)
        {
            return;
        }

        await this.pagingFailed.UpdateAsync(static _ => false, cancellationToken).ConfigureAwait(false);

        this.Remember(extended);
    }

    /// <summary>Reads the loaded window back, where it is still of the list a read was started for.</summary>
    /// <param name="window">The window that read was started from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The window loaded now, or <see langword="null" /> where the list has moved on to another one.</returns>
    private async ValueTask<MessageWindow?> StillLoadedAsync(
        MessageWindow window,
        CancellationToken cancellationToken)
    {
        var current = await this.loaded.Value(cancellationToken).ConfigureAwait(false);

        return current?.IsOf(window) is true ? current : null;
    }

    /// <summary>Writes what is selected in the list into the scope every other space reads.</summary>
    /// <remarks>
    /// The scope's own place is left exactly as it was: what a selection changes is what is selected, and rewriting the
    /// account or the folder from here would be the list telling the tree where somebody is.
    /// </remarks>
    private async ValueTask NarrowAsync(
        IImmutableList<MessageRow>? chosen,
        CancellationToken cancellationToken)
    {
        IImmutableList<string> selected = [.. (chosen ?? []).Select(static row => row.Key)];

        await this.workspace.Scope.UpdateAsync(
            scope => (scope ?? WorkspaceScope.Everything) with
            {
                Selection = selected,
                BodySelection = string.Empty,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Keeps where the list is, which is the request that reopens its leading page.</summary>
    private void Remember(MessageWindow window) => this.memory.Write(
        new RememberedMessageList(
            window.Place.RememberedAs,
            window.LeadingPage?.ReadCursor,
            window.LeadingPage?.ReadDirection ?? MailTimelinePageDirection.Forward,
            window.Arrangement));

    private IImmutableList<MessageRow> Draw(MessageWindow window) =>
        MessageListShape.Of(

            // One instant for the whole list rather than one per row, so two messages that arrived together are never
            // dated as having arrived on different days.
            window,
            this.clock.GetUtcNow(),
            this.words);
}
