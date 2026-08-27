// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Messages;

/// <summary>Where a message list's position and arrangement outlive both the folder being left and the run itself.</summary>
/// <remarks>
/// <para>
/// A seam for the reason the mailbox tree's memory is one: what is behind it is a platform store no unit test can
/// reach, while everything decided around it is ordinary logic that ought to be tested.
/// </para>
/// <para>
/// Two lifetimes rather than one, which is what makes this its own interface instead of a second call on the tree's.
/// Moving between folders and back has to return to each of them, so the run remembers several places; a restart has to
/// return to the one somebody was in, which is the one the tree reopens on, so a store that kept every place a run
/// visited would be growing a list of a person's folders on their disk for a value only one of them will be read from.
/// </para>
/// <para>
/// What is written is a cursor, a folder alias, an account identifier, and a role — mail metadata, and therefore
/// personal data. It is kept where the platform keeps one person's own preferences, on that person's own device, and it
/// is never sent anywhere, logged, or put in telemetry.
/// </para>
/// </remarks>
public interface IMessageListMemory
{
    /// <summary>Reads where a place was left.</summary>
    /// <param name="placeKey">The place, as <see cref="MessagePlace.RememberedAs" /> composes it.</param>
    /// <returns>What was remembered, or <see cref="RememberedMessageList.Nothing" /> where nothing was or what was kept is no longer readable.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="placeKey" /> is <see langword="null" /> or empty.</exception>
    RememberedMessageList Read(string placeKey);

    /// <summary>Keeps where a place is now.</summary>
    /// <param name="remembered">The position and arrangement to keep.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="remembered" /> is <see langword="null" />.</exception>
    void Write(RememberedMessageList remembered);
}
