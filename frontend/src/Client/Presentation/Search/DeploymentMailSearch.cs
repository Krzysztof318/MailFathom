// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Search;
using MailFathom.Client.Backend.Timeline;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Threads;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Search;

/// <summary>The ranked mail and recent searches one client run keeps.</summary>
public sealed class DeploymentMailSearch : IMailSearch
{
    internal const int PageSize = 20;
    internal const int RecentLimit = 5;

    private readonly DeploymentClient deployment;
    private readonly IWorkspace workspace;
    private readonly IMailThread thread;
    private readonly TimeProvider clock;
    private readonly IStringLocalizer words;
    private readonly IState<long> sessionRevision;
    private readonly IState<MailSearchRun> run;
    private readonly IState<MailSearchWindow> loaded;
    private readonly IState<MailSearchHistory> recent;
    private readonly IState<string> folderRole;
    private readonly IState<bool> pagingFailed;

    /// <summary>Initializes search over the deployment, workspace, and one conversation shared by the client.</summary>
    /// <param name="deployment">Where ranked mail is asked for.</param>
    /// <param name="session">What separates state held before and after the current session changes.</param>
    /// <param name="workspace">The mailbox scope a newly opened search starts from.</param>
    /// <param name="thread">The conversation a result opens.</param>
    /// <param name="clock">What a result's date is written relative to.</param>
    /// <param name="words">Where the sentences and special-use folder names come from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DeploymentMailSearch(
        DeploymentClient deployment,
        IClientSession session,
        IWorkspace workspace,
        IMailThread thread,
        TimeProvider clock,
        IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(thread);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(words);

        this.deployment = deployment;
        this.workspace = workspace;
        this.thread = thread;
        this.clock = clock;
        this.words = words;

        this.sessionRevision = State.FromFeed(this, session.Revision);

        this.IsOpen = State.Value(this, () => false);
        this.Query = State.Value(this, () => string.Empty);
        this.Account = State.Value(this, () => string.Empty);
        this.Folder = State.Value(this, () => string.Empty);
        this.Sender = State.Value(this, () => string.Empty);
        this.Recipient = State.Value(this, () => string.Empty);
        this.folderRole = State.Value(this, () => string.Empty);
        this.ReceivedOnOrAfter = State<DateTimeOffset>.Empty(this);
        this.ReceivedBefore = State<DateTimeOffset>.Empty(this);
        this.Unread = State<bool>.Empty(this);
        this.Flagged = State<bool>.Empty(this);
        this.HasAttachments = State<bool>.Empty(this);

        this.run = State.Value(this, () => MailSearchRun.Nothing);
        this.loaded = State.FromFeed(
            this,
            Feed.Combine(this.sessionRevision, this.run).SelectAsync(this.ReadAsync));
        this.recent = State.Value(this, () => MailSearchHistory.Nothing);
        this.pagingFailed = State.Value(this, () => false);

        this.Results = this.loaded.Select(this.Draw).AsListFeed();
        this.Recent = Feed.Combine(this.sessionRevision, this.recent)
            .Select(static current =>
                (IImmutableList<RecentMailSearch>)(current.Item1 == current.Item2.Revision
                    ? current.Item2.Entries
                    : ImmutableArray<RecentMailSearch>.Empty))
            .AsListFeed();
        this.Reading = this.loaded.Select(this.Describe);
        this.PagingFailed = this.pagingFailed;

        this.sessionRevision.ForEach(this.ResetForAsync);
    }

    /// <summary>Whether the ranked list is shown instead of the timeline.</summary>
    public IState<bool> IsOpen { get; }

    /// <summary>The query text being edited.</summary>
    public IState<string> Query { get; }

    /// <summary>The account constraint being edited.</summary>
    public IState<string> Account { get; }

    /// <summary>The folder constraint being edited.</summary>
    public IState<string> Folder { get; }

    /// <summary>The sender constraint being edited.</summary>
    public IState<string> Sender { get; }

    /// <summary>The recipient constraint being edited.</summary>
    public IState<string> Recipient { get; }

    /// <summary>The inclusive received-date constraint being edited.</summary>
    public IState<DateTimeOffset> ReceivedOnOrAfter { get; }

    /// <summary>The exclusive received-date constraint being edited.</summary>
    public IState<DateTimeOffset> ReceivedBefore { get; }

    /// <summary>The read-state constraint being edited, empty for both states.</summary>
    public IState<bool> Unread { get; }

    /// <summary>The flag-state constraint being edited, empty for both states.</summary>
    public IState<bool> Flagged { get; }

    /// <summary>The attachment constraint being edited, empty for both states.</summary>
    public IState<bool> HasAttachments { get; }

    /// <summary>The result rows loaded for the current search.</summary>
    public IListFeed<MessageRow> Results { get; }

    /// <summary>The searches kept for this run, newest first.</summary>
    public IListFeed<RecentMailSearch> Recent { get; }

    /// <summary>What the result list says about its scope and semantic capability.</summary>
    public IFeed<MailSearchReading> Reading { get; }

    /// <summary>Whether the last attempt to take another result page failed.</summary>
    public IFeed<bool> PagingFailed { get; }

    /// <summary>Shows search and starts its filters at the place currently in force.</summary>
    public async ValueTask OpenAsync(CancellationToken cancellationToken)
    {
        var active = await this.sessionRevision.Option(cancellationToken).ConfigureAwait(false);
        if (!active.IsSome(out var session))
        {
            return;
        }

        await this.ResetForAsync(session, cancellationToken).ConfigureAwait(false);
        await this.IsOpen.SetAsync(true, cancellationToken).ConfigureAwait(false);

        if ((await this.run.Value(cancellationToken).ConfigureAwait(false)).Query is null)
        {
            await this.UseCurrentScopeAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Returns to the timeline without discarding search state.</summary>
    public ValueTask CloseAsync(CancellationToken cancellationToken) =>
        this.IsOpen.SetAsync(false, cancellationToken);

    /// <summary>Takes the current mailbox-tree place as the account and folder filters.</summary>
    public async ValueTask UseCurrentScopeAsync(CancellationToken cancellationToken)
    {
        var scope = await this.workspace.Scope.Value(cancellationToken).ConfigureAwait(false) ?? WorkspaceScope.Everything;

        await this.Account.SetAsync(scope.Account ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await this.SetFolderAsync(
            scope.Role is { } role ? $"role:{role}" : scope.Folder,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Runs the current query and filters from their leading page.</summary>
    public async ValueTask SearchAsync(CancellationToken cancellationToken)
    {
        var active = await this.sessionRevision.Option(cancellationToken).ConfigureAwait(false);
        if (!active.IsSome(out var session))
        {
            return;
        }

        await this.ResetForAsync(session, cancellationToken).ConfigureAwait(false);
        var query = await this.ComposeAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(query.Query))
        {
            return;
        }

        var sequence = (await this.run.Value(cancellationToken).ConfigureAwait(false)).Sequence;
        await this.run.SetAsync(new MailSearchRun(session, query, sequence + 1), cancellationToken).ConfigureAwait(false);
        await this.pagingFailed.SetAsync(false, cancellationToken).ConfigureAwait(false);

        var held = await this.recent.Value(cancellationToken).ConfigureAwait(false);
        var entry = new RecentMailSearch(query.Query, query);
        ImmutableArray<RecentMailSearch> updated =
            [entry, .. held.Entries.Where(recent => !string.Equals(recent.Key, entry.Key, StringComparison.Ordinal)).Take(RecentLimit - 1)];
        await this.recent.SetAsync(new MailSearchHistory(session, updated), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Takes the next page onto the ranked list already drawn.</summary>
    public async ValueTask ShowMoreAsync(CancellationToken cancellationToken)
    {
        if (await this.loaded.Value(cancellationToken).ConfigureAwait(false) is not
            { Query: { } query, NextCursor: { } cursor } window)
        {
            return;
        }

        DeploymentMailSearchPage page;

        try
        {
            page = await this.deployment.SearchMailAsync(
                query with { Cursor = cursor },
                cancellationToken).ConfigureAwait(false);
        }
        catch (DeploymentFailure)
        {
            if (Equals(await this.loaded.Value(cancellationToken).ConfigureAwait(false), window))
            {
                await this.pagingFailed.SetAsync(true, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (Equals(await this.loaded.Value(cancellationToken).ConfigureAwait(false), window))
        {
            await this.loaded.SetAsync(window.Extended(page), cancellationToken).ConfigureAwait(false);
            await this.pagingFailed.SetAsync(false, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Removes account and folder constraints and immediately searches all mail.</summary>
    public async ValueTask WidenAsync(CancellationToken cancellationToken)
    {
        await this.Account.SetAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        await this.Folder.SetAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        await this.SearchAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a search result's conversation at that message without replacing the result list.</summary>
    public ValueTask OpenResultAsync(MessageRow result, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        var message = Guid.TryParseExact(result.Key, "D", out var parsed) ? parsed : (Guid?)null;
        return this.thread.OpenAsync(result.ThreadId, message, cancellationToken);
    }

    /// <summary>Sets the read-state constraint, or removes it.</summary>
    public ValueTask SetUnreadAsync(bool? value, CancellationToken cancellationToken) =>
        this.Unread.SetAsync(value, cancellationToken);

    /// <summary>Sets the flag-state constraint, or removes it.</summary>
    public ValueTask SetFlaggedAsync(bool? value, CancellationToken cancellationToken) =>
        this.Flagged.SetAsync(value, cancellationToken);

    /// <summary>Sets the attachment constraint, or removes it.</summary>
    public ValueTask SetHasAttachmentsAsync(bool? value, CancellationToken cancellationToken) =>
        this.HasAttachments.SetAsync(value, cancellationToken);

    /// <summary>Removes one constraint and leaves every other part of the search untouched.</summary>
    public ValueTask ClearFilterAsync(MailSearchFilter filter, CancellationToken cancellationToken) => filter switch
    {
        MailSearchFilter.Account => this.Account.SetAsync(string.Empty, cancellationToken),
        MailSearchFilter.Folder => this.SetFolderAsync(folder: null, cancellationToken),
        MailSearchFilter.Sender => this.Sender.SetAsync(string.Empty, cancellationToken),
        MailSearchFilter.Recipient => this.Recipient.SetAsync(string.Empty, cancellationToken),
        MailSearchFilter.ReceivedOnOrAfter => this.ReceivedOnOrAfter.SetAsync(null, cancellationToken),
        MailSearchFilter.ReceivedBefore => this.ReceivedBefore.SetAsync(null, cancellationToken),
        MailSearchFilter.Unread => this.Unread.SetAsync(null, cancellationToken),
        MailSearchFilter.Flagged => this.Flagged.SetAsync(null, cancellationToken),
        MailSearchFilter.HasAttachments => this.HasAttachments.SetAsync(null, cancellationToken),
        _ => ValueTask.CompletedTask,
    };

    /// <summary>Restores one recent search into the editor and asks it again.</summary>
    public async ValueTask RepeatAsync(RecentMailSearch recent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recent);

        var query = recent.Search;
        await this.Query.SetAsync(query.Query, cancellationToken).ConfigureAwait(false);
        await this.Account.SetAsync(query.Account ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await this.SetFolderAsync(query.Folder, cancellationToken).ConfigureAwait(false);
        await this.Sender.SetAsync(query.Sender ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await this.Recipient.SetAsync(query.Recipient ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await this.ReceivedOnOrAfter.SetAsync(query.ReceivedOnOrAfter, cancellationToken).ConfigureAwait(false);
        await this.ReceivedBefore.SetAsync(query.ReceivedBefore, cancellationToken).ConfigureAwait(false);
        await this.Unread.SetAsync(query.Unread, cancellationToken).ConfigureAwait(false);
        await this.Flagged.SetAsync(query.Flagged, cancellationToken).ConfigureAwait(false);
        await this.HasAttachments.SetAsync(query.HasAttachments, cancellationToken).ConfigureAwait(false);
        await this.SearchAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<MailSearchQuery> ComposeAsync(CancellationToken cancellationToken) => new()
    {
        Query = (await this.Query.Value(cancellationToken).ConfigureAwait(false) ?? string.Empty).Trim(),
        Account = Named(await this.Account.Value(cancellationToken).ConfigureAwait(false)),
        Folder = await this.FolderForQueryAsync(cancellationToken).ConfigureAwait(false),
        Sender = Named(await this.Sender.Value(cancellationToken).ConfigureAwait(false)),
        Recipient = Named(await this.Recipient.Value(cancellationToken).ConfigureAwait(false)),
        ReceivedOnOrAfter = await Optional(this.ReceivedOnOrAfter, cancellationToken).ConfigureAwait(false),
        ReceivedBefore = await Optional(this.ReceivedBefore, cancellationToken).ConfigureAwait(false),
        Unread = await Optional(this.Unread, cancellationToken).ConfigureAwait(false),
        Flagged = await Optional(this.Flagged, cancellationToken).ConfigureAwait(false),
        HasAttachments = await Optional(this.HasAttachments, cancellationToken).ConfigureAwait(false),
        PageSize = PageSize,
    };

    private async ValueTask<MailSearchWindow> ReadAsync(
        (long Revision, MailSearchRun Run) trigger,
        CancellationToken cancellationToken)
    {
        if (trigger.Revision != trigger.Run.Revision || trigger.Run.Query is not { } query)
        {
            return MailSearchWindow.Nothing;
        }

        var page = await this.deployment.SearchMailAsync(query, cancellationToken).ConfigureAwait(false);
        return MailSearchWindow.Opening(query, page);
    }

    private IImmutableList<MessageRow> Draw(MailSearchWindow window)
    {
        var role = window.Query?.Folder?.StartsWith("role:", StringComparison.Ordinal) is true
            ? window.Query.Folder[5..]
            : null;
        var place = window.Query is null
            ? MessagePlace.Everything
            : new MessagePlace(window.Query.Account, role is null ? window.Query.Folder : null, role);
        var now = this.clock.GetUtcNow();

        return [.. window.Results.Select(result => this.Draw(result, place, now))];
    }

    private MessageRow Draw(DeploymentMailSearchResult result, MessagePlace place, DateTimeOffset now)
    {
        var row = MessageListShape.Of(DeploymentMailSearch.ToMessage(result), place, now, this.words);
        var reason = this.words[ReasonKey(result.Origin)].Value;
        var extract = string.Join(Environment.NewLine, result.Extracts);
        var explanation = extract.Length is 0 ? reason : $"{reason}. {extract}";

        return row with
        {
            Announcement = $"{row.Announcement} {explanation}",
            MatchReason = reason,
            MatchExtract = extract,
        };
    }

    private MailSearchReading Describe(MailSearchWindow window)
    {
        if (window.Query is not { } query)
        {
            return MailSearchReading.Nothing;
        }

        return new MailSearchReading(
            HasSearched: true,
            Scope: this.ScopeOf(query),
            HasMore: window.NextCursor is not null,
            SemanticSearchInactive: window.SemanticStanding is SemanticSearchStanding.Inactive,
            SemanticSearchDegraded: window.SemanticStanding is SemanticSearchStanding.Degraded);
    }

    private string ScopeOf(MailSearchQuery query)
    {
        var folder = query.Folder?.StartsWith("role:", StringComparison.Ordinal) is true
            ? this.RoleName(query.Folder[5..])
            : query.Folder;

        return (query.Account, folder) switch
        {
            (null, null) => this.words[MailSearchWords.ScopeEverythingKey].Value,
            ({ } account, null) => this.words[MailSearchWords.ScopeAccountKey, account].Value,
            (null, { } onlyFolder) => this.words[MailSearchWords.ScopeAccountKey, onlyFolder].Value,
            ({ } account, { } namedFolder) => this.words[MailSearchWords.ScopeFolderKey, account, namedFolder].Value,
        };
    }

    private static DeploymentMailMessage ToMessage(DeploymentMailSearchResult result) => new(
        result.Id,
        result.Account,
        result.Folder,
        result.ThreadId,
        result.Subject,
        result.ReceivedAt,
        result.SentAt,
        result.SenderAddress,
        result.SenderDisplayName,
        result.Recipients,
        result.Unread,
        result.Flagged,
        result.Answered,
        result.HasAttachments,
        result.AttachmentCount,
        result.SizeOctets,
        result.Preview);

    private static string ReasonKey(MailSearchMatchOrigin origin) => origin switch
    {
        MailSearchMatchOrigin.LexicalRanking => MailSearchWords.LexicalKey,
        MailSearchMatchOrigin.SemanticRanking => MailSearchWords.SemanticKey,
        MailSearchMatchOrigin.BothRankings => MailSearchWords.BothKey,
        _ => MailSearchWords.UnknownKey,
    };

    private static string? Named(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async ValueTask<string?> FolderForQueryAsync(CancellationToken cancellationToken)
    {
        var folder = Named(await this.Folder.Value(cancellationToken).ConfigureAwait(false));
        var role = Named(await this.folderRole.Value(cancellationToken).ConfigureAwait(false));

        return role is not null && string.Equals(folder, this.RoleName(role), StringComparison.Ordinal)
            ? $"role:{role}"
            : folder;
    }

    private async ValueTask SetFolderAsync(string? folder, CancellationToken cancellationToken)
    {
        var role = folder?.StartsWith("role:", StringComparison.Ordinal) is true ? folder[5..] : null;
        await this.folderRole.SetAsync(role ?? string.Empty, cancellationToken).ConfigureAwait(false);
        await this.Folder.SetAsync(role is null ? folder ?? string.Empty : this.RoleName(role), cancellationToken)
            .ConfigureAwait(false);
    }

    private string RoleName(string role) => this.words[MailboxWords.RoleResourceKeyFor(role)].Value;

    private async ValueTask ResetForAsync(long revision, CancellationToken cancellationToken)
    {
        var held = await this.run.Value(cancellationToken).ConfigureAwait(false);

        if (held.Revision == revision)
        {
            return;
        }

        await this.run.SetAsync(new MailSearchRun(revision, Query: null, Sequence: 0), cancellationToken)
            .ConfigureAwait(false);
        await this.recent.SetAsync(new MailSearchHistory(revision, []), cancellationToken).ConfigureAwait(false);

        if (held.Revision is null)
        {
            return;
        }

        await this.IsOpen.SetAsync(false, cancellationToken).ConfigureAwait(false);
        await this.Query.SetAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        await this.Account.SetAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        await this.SetFolderAsync(folder: null, cancellationToken).ConfigureAwait(false);
        await this.Sender.SetAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        await this.Recipient.SetAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        await this.ReceivedOnOrAfter.SetAsync(null, cancellationToken).ConfigureAwait(false);
        await this.ReceivedBefore.SetAsync(null, cancellationToken).ConfigureAwait(false);
        await this.Unread.SetAsync(null, cancellationToken).ConfigureAwait(false);
        await this.Flagged.SetAsync(null, cancellationToken).ConfigureAwait(false);
        await this.HasAttachments.SetAsync(null, cancellationToken).ConfigureAwait(false);
        await this.pagingFailed.SetAsync(false, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<T?> Optional<T>(IState<T> state, CancellationToken cancellationToken)
        where T : struct
    {
        var value = await state.Option(cancellationToken).ConfigureAwait(false);
        return value.IsSome(out var stated) ? stated : null;
    }

    private readonly record struct MailSearchRun(long? Revision, MailSearchQuery? Query, int Sequence)
    {
        internal static MailSearchRun Nothing { get; } = new(Revision: null, Query: null, Sequence: 0);
    }

    private readonly record struct MailSearchHistory(
        long? Revision,
        ImmutableArray<RecentMailSearch> Entries)
    {
        internal static MailSearchHistory Nothing { get; } = new(Revision: null, []);
    }
}
