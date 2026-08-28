// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Threads;

/// <summary>One author of the conversation, as the header names them.</summary>
/// <param name="Key">The address they wrote from, which is what this entry is matched by across a redraw.</param>
/// <param name="Author">The name they wrote under, or the address they wrote from where no message carried one.</param>
/// <param name="MessageCount">How many of the conversation's messages are theirs, as the reader's own language writes a number.</param>
/// <param name="Announcement">The entry as one sentence, which is what a screen reader is given instead of two labels.</param>
/// <remarks>
/// <para>
/// Drawn from what the deployment said about the whole conversation rather than from the messages in hand, which is the
/// point of it being published at all: a header derived by walking the messages would name whoever happened to be on
/// the first page and would change as more of the conversation arrived.
/// </para>
/// <para>
/// It is <c>partial</c> because <paramref name="Key" /> makes it eligible for MVUX's key-equality generation, which is
/// what carries an author across a redraw: a page taken onto the conversation updates the entries whose counts changed
/// and leaves the containers of the rest alone. The generator refuses to run on a sealed record that is not partial and
/// says so as <c>KE0001</c>.
/// </para>
/// </remarks>
public sealed partial record ThreadParticipantRow(
    string Key,
    string Author,
    string MessageCount,
    string Announcement);
