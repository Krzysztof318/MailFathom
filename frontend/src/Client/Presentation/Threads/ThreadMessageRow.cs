// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Client.Presentation.Spaces.Mail.Reading;

namespace MailFathom.Client.Presentation.Threads;

/// <summary>One message of the conversation, with everything the screen draws it from and nothing else.</summary>
/// <param name="Key">The message's own identity, which is what this row is matched by across a redraw and what the whole of the message is read by.</param>
/// <param name="Author">Who wrote the message — the name they wrote under, or the address they wrote from.</param>
/// <param name="Subject">What the message is about, or the words standing in for a message that carried no subject.</param>
/// <param name="Recipients">Who it went to, already written as the line the expanded message shows.</param>
/// <param name="Contribution">What this message added, with its quoted history and signature already trimmed off by the deployment.</param>
/// <param name="Written">When the message was written, in full rather than in the bands a list shows, because a conversation spans years.</param>
/// <param name="Announcement">The whole line as one sentence, which is what a screen reader is given instead of six controls.</param>
/// <param name="IsExpanded">Whether the message shows what it added rather than the one line it collapses to.</param>
/// <param name="IsOpenedAt">Whether this is the message the conversation was opened at, which is the one the screen scrolls to.</param>
/// <param name="IsUnread">Whether the mail server last reported the message without <c>\Seen</c>.</param>
/// <param name="IsFlagged">Whether it last reported it with <c>\Flagged</c>.</param>
/// <param name="IsAnswered">Whether it last reported it with <c>\Answered</c>.</param>
/// <param name="HasAttachments">Whether the message carries anything besides its body and its inline resources.</param>
/// <param name="AttachmentCount">How many of those there are.</param>
/// <param name="Message">Everything the pane draws around the whole message, or <see langword="null" /> where nobody has asked for it.</param>
/// <param name="WholeMessage">The whole message as a reading pane draws it, or <see langword="null" /> where nobody has asked for it.</param>
/// <param name="IsReadingWholeMessage">Whether the whole message is on its way.</param>
/// <param name="WholeMessageFailed">Whether the last attempt to read the whole message did not arrive.</param>
/// <remarks>
/// <para>
/// Everything down to the contribution came out of the page that carried the conversation, so a thread of thirty
/// messages is one request until somebody asks for the whole of one. That asymmetry is the screen: what each message
/// added is what a conversation is read as, and the quoted history under it is one gesture away rather than eight
/// copies of the first message down the page.
/// </para>
/// <para>
/// It is <c>partial</c> because <paramref name="Key" /> makes it eligible for MVUX's key-equality generation, which is
/// what carries a message's identity across a redraw: expanding one message rebuilds the rows and leaves the containers
/// of the others alone, so nothing that was scrolled to moves and nothing that was drawn is drawn again.
/// </para>
/// <para>
/// Every word on it is this owner's own correspondence. It is put in front of that owner alone and reaches no log, no
/// telemetry, and no local store.
/// </para>
/// </remarks>
public sealed partial record ThreadMessageRow(
    string Key,
    string Author,
    string Subject,
    string Recipients,
    string Contribution,
    string Written,
    string Announcement,
    bool IsExpanded,
    bool IsOpenedAt,
    bool IsUnread,
    bool IsFlagged,
    bool IsAnswered,
    bool HasAttachments,
    int AttachmentCount,
    MailMessageReading? Message,
    MailBodyReading? WholeMessage,
    bool IsReadingWholeMessage,
    bool WholeMessageFailed)
{
    /// <summary>Gets whether there is anything of what the message added to draw.</summary>
    /// <remarks>
    /// A message this deployment holds but has not extracted yet has none, which is an ordinary state of a mailbox
    /// still being taken in rather than a message with nothing in it. The expanded message says so rather than showing
    /// a blank.
    /// </remarks>
    public bool HasContribution => this.Contribution.Length > 0;

    /// <summary>Gets whether the message is expanded and nothing has been extracted of what it added.</summary>
    public bool AwaitsContribution => this.IsExpanded && !this.HasContribution;

    /// <summary>Gets whether there is anybody the message went to worth drawing.</summary>
    /// <remarks>
    /// A message whose recipients no header carried draws one line less rather than a label with nothing after it,
    /// which is what a row of a list already does with a preview nothing has extracted.
    /// </remarks>
    public bool HasRecipients => this.Recipients.Length > 0;

    /// <summary>Gets whether the message's headers and attachments are drawn.</summary>
    public bool ShowsMessage => this.IsExpanded && this.Message is not null;

    /// <summary>Gets whether the whole message is drawn under what it added.</summary>
    public bool ShowsWholeMessage => this.IsExpanded && this.WholeMessage is not null;

    /// <summary>Gets whether the reader may still ask for the whole message, quoted history and all.</summary>
    /// <remarks>
    /// Stated as its own affirmative rather than read as the absence of a reading, so the offer is on the screen only
    /// once the message is open and only while there is something to ask for. A read that did not arrive is the one
    /// case where there is something to ask for and this is still false: the notice below carries the ask itself, and
    /// two controls offering the same thing would read as two different ones.
    /// </remarks>
    public bool OffersWholeMessage =>
        this.IsExpanded && this.WholeMessage is null && !this.IsReadingWholeMessage && !this.WholeMessageFailed;

    /// <summary>Gets whether the whole message is on its way, which is what the message says while it waits.</summary>
    public bool AwaitsWholeMessage => this.IsExpanded && this.IsReadingWholeMessage;

    /// <summary>Gets whether the last attempt at the whole message did not arrive, which is what the retry is offered on.</summary>
    public bool ShowsWholeMessageFailure => this.IsExpanded && this.WholeMessageFailed;

    /// <summary>Gets whether the number of attachments is worth drawing beside the mark saying there are any.</summary>
    public bool ShowsAttachmentCount => this.AttachmentCount > 1;

    /// <summary>Gets how many attachments there are, as the reader's own language writes a number.</summary>
    public string AttachmentCountText => this.AttachmentCount.ToString("N0", CultureInfo.CurrentCulture);
}
