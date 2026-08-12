// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Runs;

/// <summary>What asking for a whole-mailbox classification run produced.</summary>
/// <param name="Run">The run the account now has outstanding, which is the one already going when nothing was started.</param>
/// <param name="Accepted">Whether this request is what put that run in front of the account.</param>
/// <remarks>
/// A request that started nothing is an answer rather than a refusal, and the terms it carried are deliberately not
/// applied to the run already under way: a walk that has scored half a mailbox as a dry run cannot become one that acts
/// halfway through, because the half already behind it would never be acted on. The caller is told which of the two
/// happened and can read the outstanding run's own terms from it.
/// </remarks>
public sealed record SpamClassificationRunRequest(SpamClassificationRun Run, bool Accepted);
