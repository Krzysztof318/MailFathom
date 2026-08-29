// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Client.Presentation.Messages;

/// <summary>One line of the message list, with everything the view draws it from and nothing else.</summary>
/// <param name="Key">The message's own identity, which is what this row is matched by across a redraw and what the scope names it by.</param>
/// <param name="ThreadId">The conversation the message belongs to, or <see langword="null" /> where nothing has placed it in one.</param>
/// <param name="Correspondent">Who the message is with — its sender, or its recipients where the place is mail this owner sent.</param>
/// <param name="Subject">What the message is about, or the words standing in for a message that carried no subject.</param>
/// <param name="Preview">The opening of the message's own text, and empty where nothing has extracted it yet.</param>
/// <param name="Received">When it arrived, written the way the reader's own language writes a time today and a date before that.</param>
/// <param name="Announcement">The whole row as one sentence, which is what a screen reader is given instead of six controls.</param>
/// <param name="IsUnread">Whether the mail server last reported the message without <c>\Seen</c>.</param>
/// <param name="IsFlagged">Whether it last reported it with <c>\Flagged</c>.</param>
/// <param name="IsAnswered">Whether it last reported it with <c>\Answered</c>.</param>
/// <param name="HasAttachments">Whether the message carries anything besides its body and its inline resources.</param>
/// <param name="AttachmentCount">How many of those there are.</param>
/// <remarks>
/// <para>
/// Everything on it was drawn from the page that carried the message, so a list of fifty rows is one request. Nothing
/// here is a body and nothing is fetched per row: a screen that asked for a sender or a preview separately would be a
/// request per visible line, which is the shape this record exists to make impossible.
/// </para>
/// <para>
/// Whether the row is the one in force is deliberately absent. Selection belongs to the list control and is mirrored
/// into the workspace scope, so a row carrying its own copy would be a second opinion about the same fact — and one
/// that had to be redrawn on every click.
/// </para>
/// <para>
/// The conversation is the one member here nothing draws. It is carried because selecting a row is how a conversation
/// is opened, and the identity that opens one is published on the message rather than derivable from anything else the
/// row holds — a screen that had to ask the deployment which conversation a selected message is in would be a request
/// per click.
/// </para>
/// <para>
/// It is <c>partial</c> because <paramref name="Key" /> makes it eligible for MVUX's key-equality generation, which is
/// what carries a row's identity across a redraw: a page taken onto the window updates the rows it changed and leaves
/// the containers of the rest alone, and what was selected stays selected. The generator refuses to run on a sealed
/// record that is not partial and says so as <c>KE0001</c>.
/// </para>
/// <para>
/// Every word on it is this owner's own correspondence. It is put in front of that owner alone and reaches no log, no
/// telemetry, and no local store — which is why what is written down when a folder is left is a cursor and never a row.
/// </para>
/// </remarks>
public sealed partial record MessageRow(
    string Key,
    Guid? ThreadId,
    string Correspondent,
    string Subject,
    string Preview,
    string Received,
    string Announcement,
    bool IsUnread,
    bool IsFlagged,
    bool IsAnswered,
    bool HasAttachments,
    int AttachmentCount)
{
    /// <summary>An undrawn row used only while a reusable row control receives its binding.</summary>
    internal static MessageRow Nothing { get; } = new(
        string.Empty,
        ThreadId: null,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        IsUnread: false,
        IsFlagged: false,
        IsAnswered: false,
        HasAttachments: false,
        AttachmentCount: 0);

    /// <summary>Gets why this message matched a search, or empty when this is an ordinary timeline row.</summary>
    public string MatchReason { get; init; } = string.Empty;

    /// <summary>Gets the extracts around matched words, or empty when no word extract explains the match.</summary>
    public string MatchExtract { get; init; } = string.Empty;

    /// <summary>Gets whether this row carries the explanation only a search result has.</summary>
    public bool HasMatchReason => this.MatchReason.Length > 0;

    /// <summary>Gets whether this row carries an extract around matched words.</summary>
    public bool HasMatchExtract => this.MatchExtract.Length > 0;

    /// <summary>Gets whether there is an opening of the message's own text to draw.</summary>
    /// <remarks>
    /// A message this deployment holds but has not extracted yet has none, which is an ordinary state of a folder still
    /// being taken in rather than a message with nothing in it. The row draws one line less rather than an empty one.
    /// </remarks>
    public bool HasPreview => this.Preview.Length > 0;

    /// <summary>Gets whether the number of attachments is worth drawing beside the mark saying there are any.</summary>
    /// <remarks>One attachment is what the mark already says; a number beside it would be the same fact written twice.</remarks>
    public bool ShowsAttachmentCount => this.AttachmentCount > 1;

    /// <summary>Gets how many attachments there are, as the reader's own language writes a number.</summary>
    /// <remarks>Composed here rather than bound as a number, because a binding to a number formats it with the framework's default rather than with the culture the application is being read in.</remarks>
    public string AttachmentCountText => this.AttachmentCount.ToString("N0", CultureInfo.CurrentCulture);
}
