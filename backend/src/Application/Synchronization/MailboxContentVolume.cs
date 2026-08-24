// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Synchronization;

/// <summary>States how much mail content one folder run moved and which of its limits it reached.</summary>
/// <param name="FetchedBytes">How many raw MIME bytes the run read from the mail server.</param>
/// <param name="StoredBytes">How many of those bytes reached local content storage.</param>
/// <param name="StoredContentBytes">
/// How much local storage the stored content occupies now, which is what it occupied when the run began plus what the
/// run wrote. It is the level a ceiling is compared against, so it is reported whether or not one is configured.
/// </param>
/// <param name="DeferredForStorageEmailCount">
/// How many occurrences were recorded without their content because the deployment's storage had reached its ceiling.
/// They are counted apart from the oversized ones because the two have opposite futures: this one is fetched by a later
/// run.
/// </param>
/// <param name="DeferredForOwnerStorageEmailCount">
/// How many were recorded without their content because the owner this folder belongs to had reached theirs, while the
/// deployment still had room. Counted apart from the deployment's for the reason that one is counted apart from the
/// oversized: the two ask an operator for different things, and only this one leaves every other owner's mail arriving
/// whole.
/// </param>
/// <param name="RefilledEmailCount">How many occurrences deferred by an earlier run had their content fetched by this one.</param>
/// <param name="StoppedForContentBudget">
/// Whether the run ended because it had spent the bytes it was allowed to fetch, rather than because the folder held
/// nothing more. It is reported beside the count of remaining emails rather than folded into it, because the two ask
/// the operator for different things: more mail to discover is ordinary, and a run that keeps stopping for its budget
/// is a budget to raise.
/// </param>
/// <remarks>
/// The record carries counts and byte totals only. Which emails they were is not part of it, because a run's volume is
/// an operational measurement and naming its messages would make one out of mail content.
/// </remarks>
public sealed record MailboxContentVolume(
    long FetchedBytes,
    long StoredBytes,
    long StoredContentBytes,
    int DeferredForStorageEmailCount,
    int DeferredForOwnerStorageEmailCount,
    int RefilledEmailCount,
    bool StoppedForContentBudget)
{
    /// <summary>Gets the volume of a run that reached no folder and therefore moved nothing.</summary>
    public static MailboxContentVolume None { get; } = new(
        FetchedBytes: 0,
        StoredBytes: 0,
        StoredContentBytes: 0,
        DeferredForStorageEmailCount: 0,
        DeferredForOwnerStorageEmailCount: 0,
        RefilledEmailCount: 0,
        StoppedForContentBudget: false);
}
