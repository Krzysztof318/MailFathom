// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Move;

/// <summary>Where the move has got to, and how much of the deployment's content the database still holds.</summary>
/// <remarks>
/// The two halves are read together because neither answers the operator's question alone: what a move copied says
/// nothing about what is left, and what is left says nothing about whether anything is carrying it. Read in one act so
/// the pair an operator is shown describes one moment rather than two that happen to agree.
/// </remarks>
/// <param name="Run">The move this deployment last had, or <see langword="null" /> when none was ever asked for.</param>
/// <param name="Backlog">What the database still holds, whether or not a move is under way.</param>
public sealed record StoredContentMoveProgress(StoredContentMoveRun? Run, StoredContentBacklog Backlog);
