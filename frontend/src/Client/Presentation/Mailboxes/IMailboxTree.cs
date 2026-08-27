// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>The one mailbox tree a run has, and the client's scope selector.</summary>
/// <remarks>
/// <para>
/// One for the run rather than one per screen, for the reason the workspace and the session are: a model is built and
/// discarded as its view is navigated to and away from, so a tree each screen kept would forget what was open the
/// moment somebody moved between spaces — and would ask the deployment for the same folders again while doing it.
/// </para>
/// <para>
/// Selecting a row narrows the workspace, which is what makes this the scope selector rather than a picture of one:
/// the list, the search, and the field a question is composed in all read that same scope, so changing folder here is
/// the whole of changing what everything else is about.
/// </para>
/// </remarks>
public interface IMailboxTree
{
    /// <summary>The visible lines of the tree, outermost first, in the order they are drawn.</summary>
    /// <remarks>
    /// A list feed rather than a feed of a list, so the three states this genuinely has are the framework's rather
    /// than each one remembered by a view: the read under way, the read that failed, and the owner who owns no mailbox
    /// at all.
    /// </remarks>
    IListFeed<MailboxRow> Rows { get; }

    /// <summary>Whether this deployment has stopped refreshing these mailboxes at all.</summary>
    /// <remarks>The deployment's switch rather than the owner's, and beside the tree rather than on a row, because no per-folder value carries it and a screen that could not tell the two apart would report every folder as failing or none of them.</remarks>
    IFeed<bool> SynchronizationPaused { get; }

    /// <summary>Shows or hides what is nested under one row.</summary>
    /// <param name="key">The row's key, as <see cref="MailboxRow.Key" /> carries it.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the tree and what is remembered of it both say so.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="key" /> is <see langword="null" /> or empty.</exception>
    ValueTask ToggleAsync(string key, CancellationToken cancellationToken);

    /// <summary>Narrows the workspace to what one row stands for.</summary>
    /// <param name="row">The row somebody chose.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the scope and what is remembered of it both say so.</returns>
    /// <remarks>A row standing for a level of a mail server's hierarchy that is not itself a folder narrows nothing, because no route on the client surface can be asked about one. It is drawn and it is not a choice.</remarks>
    ValueTask SelectAsync(MailboxRow? row, CancellationToken cancellationToken);

    /// <summary>Asks the deployment again, which is what a person presses when a read did not arrive.</summary>
    /// <param name="cancellationToken">Abandons the ask.</param>
    /// <returns>A task completing once the ask has been made.</returns>
    ValueTask AskAgainAsync(CancellationToken cancellationToken);
}
