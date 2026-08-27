// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Presentation.Workspace;

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>One visible line of the mailbox tree, with everything the view draws it from and nothing else.</summary>
/// <param name="Key">What this row is remembered by, which is its identity across a refresh and what its expansion is kept under.</param>
/// <param name="Kind">What the row stands for, which decides how it is drawn and whether it can be scoped to.</param>
/// <param name="Depth">How many levels in the row sits, which the view turns into the space in front of it.</param>
/// <param name="Name">What a person recognizes the row by, which is a mailbox's published name, a folder's own last level, or a composed sentence for the unified rows.</param>
/// <param name="UnreadCount">How many of the mail held here the mail server last reported without <c>\Seen</c>.</param>
/// <param name="StoredCount">How much mail this deployment holds here and would serve.</param>
/// <param name="Standing">Whether the copy is still being refreshed, said in the words a person acts on, and empty where the row stands for no single copy.</param>
/// <param name="Freshness">How long ago mail was last taken in, said as a band rather than as an instant, and empty on the same terms.</param>
/// <param name="IsUnreachable">Whether the mail server did not serve this deployment, so nothing is refreshing what is under this row.</param>
/// <param name="IsFailing">Whether the deployment's last attempt at what is under this row did not complete.</param>
/// <param name="IsBehind">Whether the last attempt ended with mail it had not yet taken in.</param>
/// <param name="IsExpandable">Whether anything is nested under this row.</param>
/// <param name="IsExpanded">Whether what is nested under this row is being shown.</param>
/// <param name="IsSelected">Whether the place this row stands for is the one in force.</param>
/// <param name="Scope">What selecting this row narrows the workspace to, or <see langword="null" /> where the row stands for no place a route can be asked about.</param>
/// <remarks>
/// <para>
/// The tree is drawn as one flat list of these rather than as nested controls, and the nesting is
/// <paramref name="Depth" />. That is what lets the list virtualize — an owner with several mailboxes and a provider
/// that nests deeply has more folders than a screen has rows — and it is what puts expansion in one place that a test
/// can read, rather than in the state of however many containers a tree control built.
/// </para>
/// <para>
/// Three facts about the copy travel separately because merging them would say the wrong thing about half the rows.
/// <paramref name="IsUnreachable" /> is a mail server that did not answer, which is waited out; <paramref name="IsFailing" />
/// is an attempt that went wrong, which is a mapping, a credential, or a defect; and <paramref name="IsBehind" /> is
/// neither, because mail can be outstanding under any standing. None of the three is drawn as a spinner: a folder
/// nobody has refreshed since Tuesday is not a folder that is loading.
/// </para>
/// <para>
/// Nothing of the mailbox is on it beyond the folder's own name, which is this owner's and is put in front of that
/// owner alone: no message, no subject, no correspondent, and no mail server, port, user name, or credential.
/// </para>
/// <para>
/// It is <c>partial</c> because <paramref name="Key" /> makes it eligible for MVUX's key-equality generation, which is
/// what carries a row's identity across a redraw so the list reuses the container rather than rebuilding every row
/// each time the tree is drawn again. The generator refuses to run on a sealed record that is not partial and says so
/// as <c>KE0001</c>, so the modifier is load-bearing rather than left over — the sibling records here carry no key and
/// therefore carry no modifier either.
/// </para>
/// </remarks>
public sealed partial record MailboxRow(
    string Key,
    MailboxRowKind Kind,
    int Depth,
    string Name,
    int UnreadCount,
    int StoredCount,
    string Standing,
    string Freshness,
    bool IsUnreachable,
    bool IsFailing,
    bool IsBehind,
    bool IsExpandable,
    bool IsExpanded,
    bool IsSelected,
    WorkspaceScope? Scope)
{
    /// <summary>How much space one level of nesting takes in front of a row.</summary>
    private const double LevelWidth = 16;

    /// <summary>Gets the space in front of the row, as the width of the spacer the view puts there.</summary>
    /// <remarks>
    /// A width rather than a margin, so the indentation is a element of the row's own layout instead of a
    /// <c>Thickness</c> composed in a record that has no business holding a framework type. It also keeps the row's
    /// content aligned with the disclosure control above it rather than shifted away from it.
    /// </remarks>
    public double IndentWidth => this.Depth * LevelWidth;

    /// <summary>Gets whether there is unread mail to announce, which is what the count beside the row is shown on.</summary>
    /// <remarks>Stated rather than left to the view to derive from the number, because a binding cannot compare and a row with nothing unread should carry no badge at all rather than one reading zero.</remarks>
    public bool HasUnread => this.UnreadCount > 0;

    /// <summary>Gets the unread count as the reader's own language writes a number.</summary>
    /// <remarks>Composed here rather than bound as a number, because a binding to a number formats it with whatever the framework's default is rather than with the culture the application is being read in.</remarks>
    public string UnreadText => this.UnreadCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Gets how much mail is held here, as the reader's own language writes a number.</summary>
    public string StoredText => this.StoredCount.ToString("N0", CultureInfo.CurrentCulture);

    /// <summary>Gets whether this row says how current what is under it is.</summary>
    /// <remarks>
    /// A mailbox always says so, because that is the question a person asks of the mailbox itself. A folder says so
    /// only where there is something to say — a folder being refreshed like every other one would otherwise repeat its
    /// mailbox's own sentence on every line of the tree, which is how a pane stops being readable. The unified rows and
    /// the hierarchy levels never say so: they stand for several copies, and one sentence about them would be a claim
    /// about whichever of them the reader had in mind.
    /// </remarks>
    public bool ShowsCopyState =>
        this.Kind is MailboxRowKind.Account
        || (this.Kind is MailboxRowKind.Folder && (this.IsUnreachable || this.IsFailing || this.IsBehind));

    /// <summary>Gets whether this row has something nested under it that is not being shown.</summary>
    public bool CanOpen => this.IsExpandable && !this.IsExpanded;

    /// <summary>Gets whether what is nested under this row is being shown and could be hidden.</summary>
    /// <remarks>
    /// Stated beside its opposite rather than derived from it in the view, for the reason every other pair here is:
    /// the converter that turns a decision into a visibility shows a control on an outright yes and on nothing else,
    /// so a control shown on the absence of one would be on the screen before anything had decided.
    /// </remarks>
    public bool CanClose => this.IsExpandable && this.IsExpanded;

    /// <summary>Gets whether selecting this row narrows the workspace to anywhere.</summary>
    public bool IsSelectable => this.Scope is not null;
}
