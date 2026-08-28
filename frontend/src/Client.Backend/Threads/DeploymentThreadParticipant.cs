// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Threads;

/// <summary>Somebody who has written in the conversation, and how much of it is theirs.</summary>
/// <param name="Address">The address they wrote from, as their messages wrote it.</param>
/// <param name="DisplayName">The name their most recent message wrote, or <see langword="null" /> where none of them carried one.</param>
/// <param name="MessageCount">How many of the conversation's messages they sent.</param>
/// <remarks>
/// An author rather than an addressee, and drawn from the whole conversation rather than from the page in hand — which
/// is the point of the deployment publishing it at all: a header derived here would be this client paging a
/// conversation in order to name who is in it.
/// </remarks>
public sealed record DeploymentThreadParticipant(string? Address, string? DisplayName, int MessageCount);
