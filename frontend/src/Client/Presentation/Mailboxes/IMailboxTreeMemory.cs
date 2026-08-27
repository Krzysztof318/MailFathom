// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Mailboxes;

/// <summary>Where the arrangement of the mailbox tree outlives the run that made it.</summary>
/// <remarks>
/// <para>
/// A seam for the same reason the deployment choice has one: what is behind it is a platform store no unit test can
/// reach, while everything decided around it is ordinary logic that ought to be tested.
/// </para>
/// <para>
/// What is written is MailFathom's own name for a mailbox, its own alias for a folder, and the names the owner's mail
/// server gave the levels above one — nothing else of the mailbox, and no message, correspondent, address, server, or
/// credential. Mail metadata is sensitive by default, so the reason is stated rather than assumed: a tree that reopened
/// collapsed and scoped to everything would make every restart begin with the person finding their way back, which is
/// the thing this client exists not to do. It is kept where the platform keeps one person's own preferences, on that
/// person's own device, and it is never sent anywhere, logged, or put in telemetry.
/// </para>
/// </remarks>
public interface IMailboxTreeMemory
{
    /// <summary>Reads where the tree was left.</summary>
    /// <returns>What was remembered, or <see cref="RememberedMailboxes.Nothing" /> where nothing was or what was kept is no longer readable.</returns>
    RememberedMailboxes Read();

    /// <summary>Keeps where the tree is now, so that starting the client again opens it here.</summary>
    /// <param name="remembered">The arrangement to keep.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="remembered" /> is <see langword="null" />.</exception>
    void Write(RememberedMailboxes remembered);
}
