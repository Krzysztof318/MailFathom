// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Answering.Audit;

/// <summary>States how one answering run ended, at the granularity somebody reading the record months later acts on.</summary>
/// <remarks>
/// <para>
/// Bounded on purpose, and bounded to what an ending <em>is</em> rather than to what caused it. A provider's own
/// classification of a failed call says which remote condition occurred, which is a question about the endpoint and is
/// answered by the log and the provider health gauge; this says whether the person who asked received an answer, which
/// is the question the record exists for.
/// </para>
/// <para>
/// A run refused before it began — this deployment answers no questions, or the period has spent its allowance — reaches
/// no value here, because no run happened to record.
/// </para>
/// </remarks>
public enum MailAnsweringRunOutcome
{
    /// <summary>The run produced the answer the caller received.</summary>
    Answered = 0,

    /// <summary>The run reached the provider and the provider ended it without producing any text.</summary>
    AnswerEmpty = 1,

    /// <summary>A provider call failed and ended the run before an answer was written.</summary>
    ProviderFailed = 2,

    /// <summary>The run reached what one question may spend and was stopped before an answer was written.</summary>
    RunBudgetExhausted = 3,

    /// <summary>The caller cancelled, or the host began shutting down, before the run finished.</summary>
    Cancelled = 4,

    /// <summary>The run ended in a way none of the above names.</summary>
    Failed = 5,
}
