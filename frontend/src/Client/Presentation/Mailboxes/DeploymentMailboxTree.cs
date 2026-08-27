// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Folders;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>The mailbox tree as one deployment answers it, and as one person left it.</summary>
/// <remarks>
/// <para>
/// The folders are read off the session rather than beside it, which is one subscription and one act for the person:
/// the session already asks the deployment again when the signed-in identity changes, when the client is pointed
/// somewhere else, and when a lost connection comes back, and the tree follows every one of those without a second
/// timer, a second retry curve, or a second button. The root instructions refuse nested retry storms, and a tree that
/// retried on top of the session's own bounded attempts would be one.
/// </para>
/// <para>
/// What is drawn is a reduction of three things the tree holds and one it does not — the deployment's answer, what is
/// expanded, what the workspace is scoped to, and when it is being read. Reading the scope rather than keeping a
/// selection of its own is what makes a row read as selected when another screen narrows the workspace, instead of the
/// tree and the scope indicator disagreeing about where somebody is.
/// </para>
/// </remarks>
internal sealed class DeploymentMailboxTree : IMailboxTree
{
    private readonly DeploymentClient deployment;
    private readonly IClientSession session;
    private readonly IWorkspace workspace;
    private readonly IMailboxTreeMemory memory;
    private readonly TimeProvider clock;
    private readonly IStringLocalizer words;
    private readonly IState<IImmutableSet<string>> expanded;
    private readonly IState<int> asked;

    /// <summary>Initializes the tree over what serves it, what decides whether it may be read, and where it was left.</summary>
    /// <param name="deployment">Where the owner's mailboxes and their folders are asked for.</param>
    /// <param name="session">What the deployment allows this caller, and whether it can be reached at all.</param>
    /// <param name="workspace">The scope selecting a row narrows, which is also what marks one row selected.</param>
    /// <param name="memory">Where the arrangement of the tree outlives the run that made it.</param>
    /// <param name="clock">What a freshness gap is measured against.</param>
    /// <param name="words">Where the sentences a row is composed from come from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DeploymentMailboxTree(
        DeploymentClient deployment,
        IClientSession session,
        IWorkspace workspace,
        IMailboxTreeMemory memory,
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
        // a reader of their own — which for the folders would be a request per projection.
        var standing = State.FromFeed(this, session.Standing);

        this.expanded = State.Value(this, () => memory.Read().Expanded);

        // Two things make the folders worth reading again, and the read follows both. The session, so a sign-in, a
        // deployment somebody pointed the client at, and a connection that came back all reach here without the tree
        // listening for any of them; and the count below, because a session that answers a second time with the same
        // grant is one message MVUX does not republish — so a person pressing the button on a read that failed while
        // the session was fine would otherwise press something that did nothing.
        this.asked = State.Value(this, () => 0);

        var answered = State.FromFeed(
            this,
            Feed.Combine(standing, this.asked).SelectAsync(this.ReadFoldersAsync));

        this.Rows = Feed
            .Combine(answered, this.expanded, workspace.Scope)
            .Select(this.Draw)
            .AsListFeed();

        this.SynchronizationPaused = answered.Select(answer => !answer.SynchronizationEnabled);
    }

    /// <inheritdoc />
    public IListFeed<MailboxRow> Rows { get; }

    /// <inheritdoc />
    public IFeed<bool> SynchronizationPaused { get; }

    /// <inheritdoc />
    public async ValueTask ToggleAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        // Flipped inside the update rather than read, decided, and written back, so two rows opened in quick
        // succession are two flips of one set rather than the second one overwriting the first.
        await this.expanded.UpdateAsync(shown => Flip(shown, key), cancellationToken).ConfigureAwait(false);

        var next = await this.ShownAsync(cancellationToken).ConfigureAwait(false);

        await this.RememberAsync(next, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask SelectAsync(MailboxRow? row, CancellationToken cancellationToken)
    {
        if (row?.Scope is not { } narrowed)
        {
            return;
        }

        // The selection inside the old scope is dropped rather than carried, because what was selected was mail in
        // the folder somebody has just left and naming it beside the new place would be a scope describing nowhere.
        await this.workspace.Scope.UpdateAsync(_ => narrowed, cancellationToken).ConfigureAwait(false);

        var shown = await this.ShownAsync(cancellationToken).ConfigureAwait(false);

        await this.RememberAsync(shown, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask AskAgainAsync(CancellationToken cancellationToken)
    {
        this.session.Refresh();

        await this.asked.UpdateAsync(asked => asked + 1, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the owner's folders, once the session that decides whether they may be read has arrived.</summary>
    /// <remarks>Neither part of the trigger is read: what they are here for is when this runs, rather than what it asks for.</remarks>
    private async ValueTask<DeploymentMailFolders> ReadFoldersAsync(
        (SessionStanding Standing, int Asked) trigger,
        CancellationToken cancellationToken) =>
        await this.deployment.ReadMailFoldersAsync(cancellationToken).ConfigureAwait(false);

    private IImmutableList<MailboxRow> Draw(
        (DeploymentMailFolders Answered, IImmutableSet<string> Expanded, WorkspaceScope Scope) drawn) =>
        MailboxTreeShape.Of(
            drawn.Answered,
            drawn.Expanded,
            drawn.Scope,

            // One instant for the whole tree rather than one per row, so two folders refreshed together are never
            // described as having been refreshed at different times.
            this.clock.GetUtcNow(),
            this.words);

    /// <summary>Opens a row that is closed and closes one that is open, which is what one control on a row does.</summary>
    private static IImmutableSet<string> Flip(IImmutableSet<string>? shown, string key)
    {
        var opened = shown ?? ImmutableHashSet<string>.Empty;

        return opened.Contains(key) ? opened.Remove(key) : opened.Add(key);
    }

    /// <summary>Reads what is expanded right now, reading a state that has yet to answer as nothing being open.</summary>
    private async ValueTask<IImmutableSet<string>> ShownAsync(CancellationToken cancellationToken) =>
        await this.expanded.Value(cancellationToken).ConfigureAwait(false) ?? ImmutableHashSet<string>.Empty;

    /// <summary>Keeps where the tree is, without whatever is selected inside the scope it is narrowed to.</summary>
    private async ValueTask RememberAsync(IImmutableSet<string> shown, CancellationToken cancellationToken)
    {
        var scope = await this.workspace.Scope.Value(cancellationToken).ConfigureAwait(false)
            ?? WorkspaceScope.Everything;

        this.memory.Write(new RememberedMailboxes(scope with { Selection = ImmutableArray<string>.Empty }, shown));
    }
}
