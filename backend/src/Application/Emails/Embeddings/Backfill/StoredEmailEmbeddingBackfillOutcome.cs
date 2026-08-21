// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>Why one run of the embedding backfill ended.</summary>
/// <remarks>
/// The distinction a caller acts on is whether the run stopped because it ran out of budget or because there was
/// nothing left to do: the first is paced by a short interval and the second by a long one, and collapsing the two
/// would either idle an instance that has a mailbox to backfill or sweep an instance that has nothing to find.
/// </remarks>
public enum StoredEmailEmbeddingBackfillOutcome
{
    /// <summary>The run spent its batch budget with messages still awaiting embedding, and the position it committed is where the next run resumes.</summary>
    BatchBudgetSpent = 0,

    /// <summary>The walk reached the end of the stored mail, so the sweep ended and the next one starts at the beginning.</summary>
    SweepCompleted = 1,

    /// <summary>The instance has activated no profile, so there is no vector space to embed into and nothing was spent.</summary>
    NoActiveProfile = 2,

    /// <summary>The configured model is not the one the active profile records, so the run wrote nothing rather than mixing two geometries into one space.</summary>
    GeneratorDisagreesWithProfile = 3,

    /// <summary>A provider call ended without vectors, which ends the run rather than the next message's turn.</summary>
    ProviderFailed = 4,

    /// <summary>The period's spend ceiling is reached, so the run ended and the next one waits for the period to roll over.</summary>
    /// <remarks>
    /// The one ending that names its own resumption. Every other reason to stop is either settled by an operator or
    /// retried on an interval chosen by how likely work is to be there; this one has an exact instant after which
    /// sweeping is worth doing again, and <see cref="StoredEmailEmbeddingBackfillResult.SpendPeriodEndsAt" /> carries
    /// it so the worker waits for that rather than polling a ceiling it already knows binds.
    /// </remarks>
    SpendCeilingReached = 5,
}
