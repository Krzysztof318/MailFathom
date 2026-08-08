// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Host.Configuration.Answering;

/// <summary>Turns the bound answering declaration into the four ceilings the boundaries that enforce them are registered with.</summary>
/// <remarks>
/// The mapping is separate from the options type for the reason every mapper in this directory is: the bound object is
/// mutable and binder-shaped, while what comes out is four validated values the retrieval, the run, the ledger, and the
/// response boundary are each allowed to assume. Nothing here returns <see langword="null" /> — unlike the provider
/// mappers, every deployment has these ceilings, and an absent section means the defaults rather than no answering.
/// </remarks>
internal static class MailAnsweringBudgetMapper
{
    /// <summary>Builds the budget a declaration describes.</summary>
    /// <param name="settings">The bound declaration, already validated.</param>
    /// <returns>The ceilings one question is subject to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settings" /> is <see langword="null" />.</exception>
    public static MailAnsweringBudget Map(MailAnsweringOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new MailAnsweringBudget(
            EmailKnowledgeBounds.Create(settings.MaxPassagesPerRetrieval, settings.MaxCharactersPerPassage),
            MailAnsweringRunBounds.Create(
                settings.MaxRetrievedCharactersPerRun,
                settings.MaxProviderCallsPerRun,
                settings.MaxTokensPerRun),
            MailAnsweringPeriodBounds.Create(
                settings.AggregatePeriod,
                settings.MaxRunsPerPeriod,
                settings.MaxTokensPerPeriod),
            MailAnswerBounds.Create(settings.MaxAnswerCharacters, settings.MaxCitations));
    }
}
