// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Search;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.Client.Presentation.Spaces.Mail;

/// <summary>The model behind <see cref="MailPage"/>: the space correspondence is read in.</summary>
/// <remarks>
/// <para>
/// Which mailboxes there are, how current each copy is, and which folder is being read are not this space's to answer.
/// They are one tree, that tree is the client's scope selector, and the frame renders it — because the list here, the
/// search, and the field a question is composed in are all about wherever the tree says somebody is. A copy of the
/// mailboxes drawn here beside it would be the same answer twice, the second already stale relative to the first.
/// </para>
/// <para>
/// What this space owns is the list of that folder's mail and the controls that arrange it, and it owns neither by
/// holding one: the list is the run's own, for the reason the tree and the workspace are, so leaving the space and
/// coming back is finding the list where it was rather than reading its first page again.
/// </para>
/// <para>
/// The conversation the list opens is the run's own on the same terms, and it is read through here rather than held:
/// selecting a message is how one is reached in this space, and a search result or a citation reaches the same
/// conversation by naming a message in it, so a conversation this model owned would be one of two.
/// </para>
/// <para>
/// Beside that is the space's own reading of the session: whether correspondence may be put in front of this caller at
/// all, read here rather than derived from a request the deployment refused.
/// </para>
/// </remarks>
public partial record MailModel
{
    private readonly IMessageList messages;
    private readonly IMailThread thread;
    private readonly IWorkspace workspace;
    private readonly IMailSearch search;

    /// <summary>Initializes the space over what decides whether it may be offered, and the list and conversation it is read through.</summary>
    /// <param name="session">What the deployment allows this caller.</param>
    /// <param name="messages">The run's own message list, drawn from wherever the mailbox tree says somebody is.</param>
    /// <param name="thread">The run's own conversation, which is whatever the list has one message selected in.</param>
    /// <param name="workspace">The scope a selected passage narrows for the intent field.</param>
    /// <param name="search">The run's own ranked list, which opens the same conversation without replacing itself.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    [ActivatorUtilitiesConstructor]
    public MailModel(
        IClientSession session,
        IMessageList messages,
        IMailThread thread,
        IWorkspace workspace,
        IMailSearch search)
        : this(session, messages, thread, workspace, search, openedMessageKey: null)
    {
    }

    /// <summary>Initializes the same mail model for the phone route that draws one message from the open conversation.</summary>
    /// <param name="openedMessage">The conversation row the route opened.</param>
    /// <param name="session">What the deployment allows this caller.</param>
    /// <param name="messages">The run's own message list, drawn from wherever the mailbox tree says somebody is.</param>
    /// <param name="thread">The run's own conversation, which is whatever the list has one message selected in.</param>
    /// <param name="workspace">The scope a selected passage narrows for the intent field.</param>
    /// <param name="search">The run's own ranked list, which opens the same conversation without replacing itself.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailModel(
        ThreadMessageRow openedMessage,
        IClientSession session,
        IMessageList messages,
        IMailThread thread,
        IWorkspace workspace,
        IMailSearch search)
        : this(session, messages, thread, workspace, search, KeyOf(openedMessage))
    {
    }

    private MailModel(
        IClientSession session,
        IMessageList messages,
        IMailThread thread,
        IWorkspace workspace,
        IMailSearch search,
        string? openedMessageKey)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(search);

        this.messages = messages;
        this.thread = thread;
        this.workspace = workspace;
        this.search = search;

        this.WithholdsMail = session.Standing.Select(standing => standing.Withholds(ClientCapability.Mail));

        this.ReadsOldestFirst = messages.Arrangement.Select(static arrangement => arrangement.OldestFirst);
        this.KeepsUnreadOnly = messages.Arrangement.Select(static arrangement => arrangement.UnreadOnly);
        this.KeepsFlaggedOnly = messages.Arrangement.Select(static arrangement => arrangement.FlaggedOnly);
        this.KeepsWithAttachmentsOnly =
            messages.Arrangement.Select(static arrangement => arrangement.WithAttachmentsOnly);
        this.KeepsJunk = messages.Arrangement.Select(static arrangement => arrangement.IncludeJunk);
        this.KeepsLessThanEverything =
            messages.Arrangement.Select(static arrangement => arrangement.KeepsLessThanEverything);
        this.KeepsEverything =
            messages.Arrangement.Select(static arrangement => !arrangement.KeepsLessThanEverything);
        this.ShowsTimeline = search.IsOpen.Select(static isOpen => !isOpen);
        this.OpenedThreadMessage = thread.Messages.AsFeed().Select(
            rows => openedMessageKey is null
                ? ThreadMessageRow.Nothing
                : rows.FirstOrDefault(row => string.Equals(row.Key, openedMessageKey, StringComparison.Ordinal))
                    ?? ThreadMessageRow.Nothing);
    }

    /// <summary>Whether this session keeps the space correspondence is read in from being put in front of this caller.</summary>
    /// <remarks>
    /// The space's own reading of the session the frame reads, stated as an affirmative for the reason
    /// <see cref="SessionStanding.Withholds" /> gives: a control shown on the absence of an offer would be on the
    /// screen before the session had answered.
    /// </remarks>
    public IFeed<bool> WithholdsMail { get; }

    /// <summary>The loaded lines of the folder's mail, in the order the list is read in.</summary>
    /// <remarks>The run's own list read through this model rather than one built here, so moving between spaces keeps where it was scrolled and costs no second page.</remarks>
    public IListFeed<MessageRow> Messages => this.messages.Rows;

    /// <summary>What is selected in the list, which is what the rest of the client reads as the scope of a question.</summary>
    public IState<IImmutableList<MessageRow>> Chosen => this.messages.Chosen;

    /// <summary>Makes the rows selected in the list the application's selection, which is what every other space reads as the scope of a question.</summary>
    /// <param name="chosen">The rows selected in the list, or none.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the selection has been written.</returns>
    /// <remarks>
    /// Written from the list control rather than through MVUX's <c>Selection</c> operator, because that operator keeps
    /// the list feed transient until a selector attaches, and the list is drawn through a <c>FeedView</c> whose value
    /// template is the selector — a feed that stayed transient would never leave progress, which is a blank Mail space
    /// after the deployment has already answered.
    /// </remarks>
    public ValueTask ChooseAsync(IImmutableList<MessageRow> chosen, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chosen);

        return this.Chosen.UpdateAsync(_ => chosen, cancellationToken);
    }

    /// <summary>Whether there is more mail after what is loaded.</summary>
    public IFeed<bool> HasMoreAfter => this.messages.HasMoreAfter;

    /// <summary>Whether there is more mail before what is loaded, which there is once the window has moved on.</summary>
    public IFeed<bool> HasMoreBefore => this.messages.HasMoreBefore;

    /// <summary>Whether the last attempt to take another page did not arrive.</summary>
    public IFeed<bool> PagingFailed => this.messages.PagingFailed;

    /// <summary>Whether the ranked list is shown instead of the timeline.</summary>
    public IState<bool> IsSearchOpen => this.search.IsOpen;

    /// <summary>Whether the ordinary timeline is shown instead of search.</summary>
    public IFeed<bool> ShowsTimeline { get; }

    /// <summary>The query text being edited.</summary>
    public IState<string> SearchQuery => this.search.Query;

    /// <summary>The account constraint being edited.</summary>
    public IState<string> SearchAccount => this.search.Account;

    /// <summary>The folder constraint being edited.</summary>
    public IState<string> SearchFolder => this.search.Folder;

    /// <summary>The sender constraint being edited.</summary>
    public IState<string> SearchSender => this.search.Sender;

    /// <summary>The recipient constraint being edited.</summary>
    public IState<string> SearchRecipient => this.search.Recipient;

    /// <summary>The inclusive received-date constraint being edited.</summary>
    public IState<DateTimeOffset> SearchReceivedOnOrAfter => this.search.ReceivedOnOrAfter;

    /// <summary>The exclusive received-date constraint being edited.</summary>
    public IState<DateTimeOffset> SearchReceivedBefore => this.search.ReceivedBefore;

    /// <summary>The read-state constraint being edited.</summary>
    public IState<bool> SearchUnread => this.search.Unread;

    /// <summary>The flag-state constraint being edited.</summary>
    public IState<bool> SearchFlagged => this.search.Flagged;

    /// <summary>The attachment constraint being edited.</summary>
    public IState<bool> SearchHasAttachments => this.search.HasAttachments;

    /// <summary>The ranked rows loaded for the current search.</summary>
    public IListFeed<MessageRow> SearchResults => this.search.Results;

    /// <summary>The searches kept for this run.</summary>
    public IListFeed<RecentMailSearch> RecentSearches => this.search.Recent;

    /// <summary>What the current result list says about its scope and semantic capability.</summary>
    public IFeed<MailSearchReading> SearchReading => this.search.Reading;

    /// <summary>Whether the last attempt to read another result page failed.</summary>
    public IFeed<bool> SearchPagingFailed => this.search.PagingFailed;

    /// <summary>Whether the list is read oldest first.</summary>
    public IFeed<bool> ReadsOldestFirst { get; }

    /// <summary>Whether the list keeps only unread mail.</summary>
    public IFeed<bool> KeepsUnreadOnly { get; }

    /// <summary>Whether the list keeps only flagged mail.</summary>
    public IFeed<bool> KeepsFlaggedOnly { get; }

    /// <summary>Whether the list keeps only mail carrying an attachment.</summary>
    public IFeed<bool> KeepsWithAttachmentsOnly { get; }

    /// <summary>Whether the list lets junk mail take part where the place would otherwise leave it out.</summary>
    public IFeed<bool> KeepsJunk { get; }

    /// <summary>Whether anything the list keeps narrows it, which is what the mark on the filters is shown on.</summary>
    /// <remarks>Stated so a list showing less than the folder holds says so on the control that did it, rather than reading as a folder with less mail in it than it has.</remarks>
    public IFeed<bool> KeepsLessThanEverything { get; }

    /// <summary>Whether the list keeps everything the place holds, which is what an empty folder is said on.</summary>
    /// <remarks>
    /// Stated beside its opposite rather than derived from it in the view, because the two lead to different sentences
    /// and the converter that turns a decision into a visibility shows a control on an outright yes and on nothing
    /// else: a place holding no mail and a place whose mail this list is keeping out are not the same thing to be told,
    /// and neither may be announced while the answer is still on its way.
    /// </remarks>
    public IFeed<bool> KeepsEverything { get; }

    /// <summary>The conversation the selected message is in, as its header is drawn.</summary>
    /// <remarks>The run's own conversation read through this model, so a citation that opened one and a row that opened one are the same screen rather than two.</remarks>
    public IFeed<ThreadReading> Thread => this.thread.Reading;

    /// <summary>The messages of that conversation, in the conversation's own order.</summary>
    public IListFeed<ThreadMessageRow> ThreadMessages => this.thread.Messages;

    /// <summary>Whether there is more of the conversation after what has been read.</summary>
    public IFeed<bool> HasMoreThreadMessages => this.thread.HasMoreMessages;

    /// <summary>Whether the last attempt to read more of the conversation did not arrive.</summary>
    public IFeed<bool> ThreadPagingFailed => this.thread.PagingFailed;

    /// <summary>The live conversation row named by the phone's message route.</summary>
    /// <remarks>Projected from the run-wide conversation rather than copied from navigation data, so a body read or attachment update reaches both compositions.</remarks>
    public IFeed<ThreadMessageRow> OpenedThreadMessage { get; }

    /// <summary>Shows what one message of the conversation added, or collapses it back to a line.</summary>
    /// <param name="key">The message, as its row names itself.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the message has been opened or closed.</returns>
    /// <remarks>It carries the message it acts on, so the command generated from it runs per message and reports its progress on the message rather than over the conversation.</remarks>
    public ValueTask ToggleThreadMessage(string key, CancellationToken cancellationToken) =>
        this.thread.ToggleAsync(key, cancellationToken);

    /// <summary>Reads the whole of one message, which is what its quoted history is reached by.</summary>
    /// <param name="key">The message, as its row names itself.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>A task completing once the whole message has arrived or the attempt has been reported.</returns>
    public ValueTask ShowWholeThreadMessage(string key, CancellationToken cancellationToken) =>
        this.thread.ShowWholeMessageAsync(key, cancellationToken);

    /// <summary>Reads one whole message again, this time fetching what it asks for from somebody else's server.</summary>
    /// <param name="key">The message, as its row names itself.</param>
    /// <param name="cancellationToken">Abandons the read.</param>
    /// <returns>A task completing once the second read has been asked for.</returns>
    public ValueTask ShowThreadRemoteContent(string key, CancellationToken cancellationToken) =>
        this.thread.ShowRemoteContentAsync(key, cancellationToken);

    /// <summary>Streams one attachment into the destination the reader chooses.</summary>
    public ValueTask SaveAttachment(
        MailAttachmentRequest request,
        CancellationToken cancellationToken) =>
        this.thread.SaveAttachmentAsync(request, cancellationToken);

    /// <summary>Stops streaming one attachment.</summary>
    public void CancelAttachment(MailAttachmentRequest request) => this.thread.CancelAttachment(request);

    /// <summary>Makes a passage selected in the body the narrowest part of the shared scope.</summary>
    public ValueTask UseBodySelection(string selection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return this.workspace.Scope.UpdateAsync(
            scope => (scope ?? WorkspaceScope.Everything) with { BodySelection = selection },
            cancellationToken);
    }

    /// <summary>Takes the next page of the conversation onto the end of what has been read.</summary>
    /// <param name="cancellationToken">Abandons the page.</param>
    /// <returns>A task completing once the page has arrived or the attempt has been reported.</returns>
    public ValueTask ShowMoreThreadMessages(CancellationToken cancellationToken) =>
        this.thread.ShowMoreAsync(cancellationToken);

    /// <summary>Asks the deployment for the conversation again, which is what a person presses when it did not arrive.</summary>
    /// <param name="cancellationToken">Abandons the ask.</param>
    /// <returns>A task completing once the ask has been made.</returns>
    public ValueTask RetryThread(CancellationToken cancellationToken) =>
        this.thread.AskAgainAsync(cancellationToken);

    /// <summary>Shows search, taking the current mailbox scope when this run has not searched yet.</summary>
    public ValueTask OpenSearch(CancellationToken cancellationToken) =>
        this.search.OpenAsync(cancellationToken);

    /// <summary>Returns to the timeline without discarding the ranked list.</summary>
    public ValueTask CloseSearch(CancellationToken cancellationToken) =>
        this.search.CloseAsync(cancellationToken);

    /// <summary>Replaces the account and folder constraints with the mailbox tree's current place.</summary>
    public ValueTask UseCurrentSearchScope(CancellationToken cancellationToken) =>
        this.search.UseCurrentScopeAsync(cancellationToken);

    /// <summary>Runs the query and visible constraints from their leading page.</summary>
    public ValueTask SearchMail(CancellationToken cancellationToken) =>
        this.search.SearchAsync(cancellationToken);

    /// <summary>Takes the next result page onto the ranked list.</summary>
    public ValueTask ShowMoreSearchResults(CancellationToken cancellationToken) =>
        this.search.ShowMoreAsync(cancellationToken);

    /// <summary>Removes the place constraints and immediately searches all mail.</summary>
    public ValueTask WidenSearch(CancellationToken cancellationToken) =>
        this.search.WidenAsync(cancellationToken);

    /// <summary>Opens one result's conversation at that message without replacing the ranked list.</summary>
    public ValueTask OpenSearchResult(MessageRow result, CancellationToken cancellationToken) =>
        this.search.OpenResultAsync(result, cancellationToken);

    /// <summary>Runs one recent search again.</summary>
    public ValueTask RepeatSearch(RecentMailSearch recent, CancellationToken cancellationToken) =>
        this.search.RepeatAsync(recent, cancellationToken);

    /// <summary>Removes the account constraint alone.</summary>
    public ValueTask ClearSearchAccount(CancellationToken cancellationToken) =>
        this.search.ClearFilterAsync(MailSearchFilter.Account, cancellationToken);

    /// <summary>Removes the folder constraint alone.</summary>
    public ValueTask ClearSearchFolder(CancellationToken cancellationToken) =>
        this.search.ClearFilterAsync(MailSearchFilter.Folder, cancellationToken);

    /// <summary>Removes the sender constraint alone.</summary>
    public ValueTask ClearSearchSender(CancellationToken cancellationToken) =>
        this.search.ClearFilterAsync(MailSearchFilter.Sender, cancellationToken);

    /// <summary>Removes the recipient constraint alone.</summary>
    public ValueTask ClearSearchRecipient(CancellationToken cancellationToken) =>
        this.search.ClearFilterAsync(MailSearchFilter.Recipient, cancellationToken);

    /// <summary>Removes the beginning of the date range alone.</summary>
    public ValueTask ClearSearchReceivedOnOrAfter(CancellationToken cancellationToken) =>
        this.search.ClearFilterAsync(MailSearchFilter.ReceivedOnOrAfter, cancellationToken);

    /// <summary>Removes the end of the date range alone.</summary>
    public ValueTask ClearSearchReceivedBefore(CancellationToken cancellationToken) =>
        this.search.ClearFilterAsync(MailSearchFilter.ReceivedBefore, cancellationToken);

    /// <summary>Takes the next page onto the end of the list.</summary>
    /// <param name="cancellationToken">Abandons the page.</param>
    /// <returns>A task completing once the page has arrived or the attempt has been reported.</returns>
    /// <remarks>It carries no parameter, so the command generated from it reports its own progress and the control bound to it is disabled while the page is on its way.</remarks>
    public ValueTask ShowMore(CancellationToken cancellationToken) =>
        this.messages.ShowMoreAsync(cancellationToken);

    /// <summary>Takes the previous page back onto the start of the list.</summary>
    /// <param name="cancellationToken">Abandons the page.</param>
    /// <returns>A task completing once the page has arrived or the attempt has been reported.</returns>
    public ValueTask ShowEarlier(CancellationToken cancellationToken) =>
        this.messages.ShowEarlierAsync(cancellationToken);

    /// <summary>Asks the deployment for the list again, which is what a person presses when it did not arrive.</summary>
    /// <param name="cancellationToken">Abandons the ask.</param>
    /// <returns>A task completing once the ask has been made.</returns>
    public ValueTask RetryMessages(CancellationToken cancellationToken) =>
        this.messages.AskAgainAsync(cancellationToken);

    /// <summary>Reads the list from the other end of the timeline.</summary>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the list is being read again.</returns>
    public ValueTask ReverseOrder(CancellationToken cancellationToken) =>
        this.ArrangeAsync(
            arrangement => arrangement with
            {
                Order = arrangement.Order is MailTimelineOrder.OldestFirst
                    ? MailTimelineOrder.NewestFirst
                    : MailTimelineOrder.OldestFirst,
            },
            cancellationToken);

    /// <summary>Keeps only unread mail, or stops doing so.</summary>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the list is being read again.</returns>
    public ValueTask ToggleUnreadOnly(CancellationToken cancellationToken) =>
        this.ArrangeAsync(
            arrangement => arrangement with { UnreadOnly = !arrangement.UnreadOnly },
            cancellationToken);

    /// <summary>Keeps only flagged mail, or stops doing so.</summary>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the list is being read again.</returns>
    public ValueTask ToggleFlaggedOnly(CancellationToken cancellationToken) =>
        this.ArrangeAsync(
            arrangement => arrangement with { FlaggedOnly = !arrangement.FlaggedOnly },
            cancellationToken);

    /// <summary>Keeps only mail carrying an attachment, or stops doing so.</summary>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the list is being read again.</returns>
    public ValueTask ToggleWithAttachmentsOnly(CancellationToken cancellationToken) =>
        this.ArrangeAsync(
            arrangement => arrangement with { WithAttachmentsOnly = !arrangement.WithAttachmentsOnly },
            cancellationToken);

    /// <summary>Lets junk mail take part in the list, or stops doing so.</summary>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the list is being read again.</returns>
    /// <remarks>It changes nothing in a junk folder somebody opened on purpose, which takes part whatever this says.</remarks>
    public ValueTask ToggleJunk(CancellationToken cancellationToken) =>
        this.ArrangeAsync(
            arrangement => arrangement with { IncludeJunk = !arrangement.IncludeJunk },
            cancellationToken);

    /// <summary>Applies one change to how the list is arranged and reads it again under the result.</summary>
    /// <remarks>
    /// The change is composed from what is in force rather than from what a control shows, because the two can differ
    /// for as long as a read is under way — and a toggle that wrote what its own visual state said would then undo the
    /// change beside it.
    /// </remarks>
    private async ValueTask ArrangeAsync(
        Func<MessageListArrangement, MessageListArrangement> change,
        CancellationToken cancellationToken)
    {
        var arrangement = await this.messages.Arrangement ?? MessageListArrangement.Default;

        await this.messages.ArrangeAsync(change(arrangement), cancellationToken).ConfigureAwait(false);
    }

    private static string KeyOf(ThreadMessageRow message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return message.Key;
    }
}
