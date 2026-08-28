// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Presentation.Threads;

/// <summary>Which conversation is open, and which message in it somebody arrived at.</summary>
/// <param name="ThreadId">The conversation, or <see langword="null" /> where none is open.</param>
/// <param name="AtMessage">The message somebody arrived at, or <see langword="null" /> where they arrived at the conversation itself.</param>
/// <remarks>
/// <para>
/// One value rather than two states, because the two are decided together and never separately: arriving at a
/// conversation from a search result, from a citation, or from a row of the list is one act, and a message named
/// without the conversation it is in is not something this screen can open.
/// </para>
/// <para>
/// Where nobody arrived at a particular message the newest is the one opened, which is what somebody catching up on a
/// conversation came for. That is decided where the conversation is read rather than here, because it is a fact about
/// what the deployment answered rather than about what was asked.
/// </para>
/// </remarks>
internal sealed record ThreadOpening(Guid? ThreadId, Guid? AtMessage)
{
    /// <summary>Nothing open, which is what the screen starts at and what selecting nothing returns it to.</summary>
    internal static ThreadOpening Nothing { get; } = new(ThreadId: null, AtMessage: null);

    /// <summary>Gets whether a conversation is open at all.</summary>
    internal bool IsOpen => this.ThreadId is not null;
}
