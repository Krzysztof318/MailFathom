// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>The answer to somebody asking for a whole-mailbox rule run.</summary>
/// <param name="Run">The run the account has outstanding, which is this request's own when <paramref name="Accepted" /> is true.</param>
/// <param name="Accepted">
/// Whether this request is what put the run in front of the account. A request that finds one already outstanding is
/// answered with that run rather than refused: the caller asked for the account's mail to be re-evaluated and it is
/// going to be, which is the answer they wanted even though nothing new was written.
/// </param>
public sealed record MailRuleEvaluationRunRequest(MailRuleEvaluationRun Run, bool Accepted);
