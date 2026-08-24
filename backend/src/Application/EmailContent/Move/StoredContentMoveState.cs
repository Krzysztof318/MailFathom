// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Move;

/// <summary>Says what the one move of a deployment's stored content into the bucket is currently doing.</summary>
/// <remarks>
/// <para>
/// Three states rather than a pair of flags, because an operator asking about the move asks one question — is it
/// working, is it waiting for me, or is it over — and two independent booleans would admit a fourth answer that means
/// nothing.
/// </para>
/// <para>
/// The state is what a bounded pass reads before it copies anything, so pausing is a write to this and nothing else. No
/// work is cancelled and no attempt is abandoned mid-payload: the pass that is already running finishes the payload it
/// holds, and the next one finds the move stopped.
/// </para>
/// </remarks>
public enum StoredContentMoveState
{
    /// <summary>The deployment carries the move forward whenever its bounded pass next runs.</summary>
    Running = 0,

    /// <summary>The operator stopped it, and it stays exactly where it is until they resume it.</summary>
    Paused = 1,

    /// <summary>The walk reached the end of every payload kind, so nothing is left for it to reach.</summary>
    /// <remarks>
    /// It says the walk finished rather than that every payload moved. A payload the move refused to repoint is counted
    /// as failed and left database-backed, and asking for a further move is what walks it again.
    /// </remarks>
    Completed = 2,
}
