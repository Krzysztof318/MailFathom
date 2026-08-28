// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Timeline;

namespace MailFathom.Client.Backend.Threads;

/// <summary>One message of a conversation, and where it sits in that conversation.</summary>
/// <param name="Position">The zero-based place the message holds in the conversation's own order, which continues across pages.</param>
/// <param name="AnsweredId">The message this one answers among the ones served, or <see langword="null" /> where it is a root of what is served.</param>
/// <param name="Email">The message itself, in the same shape a list row carries.</param>
/// <remarks>
/// <para>
/// The message is the list route's own row rather than a second shape, so this client parses one message across the
/// whole surface and the two routes cannot come to disagree about one of them. Its preview is what this message added
/// with the quoted history and the signature block trimmed off, which is what keeps the eighth reply from redrawing the
/// seven above it, and its identity is what the whole message is reached by.
/// </para>
/// <para>
/// A message whose parent sits in a folder an operator withheld is served as a root naming nothing, so the gap the
/// withheld message would leave discloses nothing about it.
/// </para>
/// </remarks>
public sealed record DeploymentThreadMessage(
    int Position,
    Guid? AnsweredId,
    DeploymentMailMessage? Email);
