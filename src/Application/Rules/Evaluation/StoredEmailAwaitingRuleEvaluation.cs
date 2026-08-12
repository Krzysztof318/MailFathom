// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Facts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.Evaluation;

/// <summary>One stored email a pass is about to evaluate, with everything a condition can read without a second query.</summary>
/// <param name="StoredEmailId">The local identity the pass records its progress and its evaluation against.</param>
/// <param name="Facts">The metadata every fact but the body text is resolved from.</param>
/// <param name="AwaitsExtraction">
/// Whether text is still expected to be derived from this email's stored content. It separates a message whose body text
/// has not been extracted <em>yet</em> from one that will never have any, which is the difference between skipping the
/// email until the text arrives and evaluating it now with the fact absent.
/// </param>
public sealed record StoredEmailAwaitingRuleEvaluation(
    StoredEmailId StoredEmailId,
    MailRuleEmailFacts Facts,
    bool AwaitsExtraction);
