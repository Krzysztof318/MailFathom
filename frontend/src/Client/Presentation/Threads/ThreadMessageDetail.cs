// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Spaces.Mail.Reading;

namespace MailFathom.Client.Presentation.Threads;

/// <summary>How much of one message of the conversation the reader has asked to see.</summary>
/// <param name="Expanded">Whether the message shows what it added rather than the one line it collapses to.</param>
/// <param name="WholeMessage">The whole message, quoted history included, or <see langword="null" /> where nobody has asked for it.</param>
/// <param name="IsReadingWholeMessage">Whether the whole message is on its way.</param>
/// <param name="WholeMessageFailed">Whether the last attempt to read the whole message did not arrive.</param>
/// <param name="RemoteImages">Whether this reading of the whole message asked for its remote pictures.</param>
/// <remarks>
/// <para>
/// Two gestures rather than one, and the split is the point of the screen: expanding shows what the message added,
/// which the conversation already carried and which therefore costs nothing, and the whole message — the quoted history
/// with it — is a read of its own that only happens because somebody asked. A thread that fetched every body would be
/// thirty requests to draw one exchange, and one that drew the quoted history inline would be the same paragraph eight
/// times.
/// </para>
/// <para>
/// The answer about remote pictures travels with the message it was given for, exactly as it does in the reading pane:
/// collapsing a message drops the whole of this, so no allowance outlives the reason it was given.
/// </para>
/// </remarks>
internal sealed record ThreadMessageDetail(
    bool Expanded,
    MailBodyReading? WholeMessage,
    bool IsReadingWholeMessage,
    bool WholeMessageFailed,
    bool RemoteImages)
{
    /// <summary>A message showing the one line it collapses to.</summary>
    internal static ThreadMessageDetail Collapsed { get; } = new(
        Expanded: false,
        WholeMessage: null,
        IsReadingWholeMessage: false,
        WholeMessageFailed: false,
        RemoteImages: false);

    /// <summary>A message showing what it added, with nothing yet asked of the whole of it.</summary>
    internal static ThreadMessageDetail Opened { get; } = Collapsed with { Expanded = true };
}
