// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>What one question produced: the answer, the mail it was drawn from, and what had to be cut to publish it.</summary>
/// <param name="AnswerText">The answer, which is never empty.</param>
/// <param name="Citations">The emails the run drew on, one per email, in the order it first reached each.</param>
/// <param name="AnswerWasTruncated">Whether the answer is shorter than the model wrote it.</param>
/// <param name="CitationsWereTruncated">Whether the run drew on more emails than are listed.</param>
/// <remarks>
/// <para>
/// The citations are what the run <em>retrieved</em> rather than what the model demonstrably used. Nothing outside the
/// model knows which of them it drew on, so publishing the narrower set would state something this system cannot
/// observe.
/// </para>
/// <para>
/// Both truncation flags exist so that a cut is never silent. A shortened answer read as a complete one is the failure
/// this shape prevents, and a citation list that lost an entry matters as much: a claim traced to a message the response
/// no longer names cannot be checked.
/// </para>
/// </remarks>
public sealed record AskMailResult(
    string AnswerText,
    IReadOnlyList<MailAnswerCitation> Citations,
    bool AnswerWasTruncated,
    bool CitationsWereTruncated);
