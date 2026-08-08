// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>What activating a declared geometry did, which is what an operator is told it started.</summary>
/// <remarks>
/// Three of these spend nothing and the first spends a mailbox, which is why they are apart: an operator who ran the
/// command twice has to be able to tell the run that began a reindex from the one that found it already running.
/// </remarks>
public enum EmbeddingProfileActivationOutcome
{
    /// <summary>A generation was registered for this geometry and a reindex began filling it.</summary>
    /// <remarks>
    /// Whatever was serving goes on serving until the new generation is complete. Where nothing was, retrieval stays
    /// lexical until the switch rather than reading a generation that is half built.
    /// </remarks>
    ReindexStarted = 0,

    /// <summary>This geometry is the generation already being built, and the reindex was left running.</summary>
    /// <remarks>Repeating the command is what an operator does after an index build failed, so it re-ensures the index rather than doing nothing at all.</remarks>
    AlreadyBuilding = 1,

    /// <summary>This geometry is already the generation retrieval reads, so nothing was registered and nothing spent.</summary>
    AlreadyServing = 2,

    /// <summary>A different generation is being built, so this activation was refused rather than started beside it.</summary>
    /// <remarks>
    /// One reindex at a time is the whole of the guarantee: two would put two partial generations in one table with one
    /// walk between them, and neither would ever reach the count that completes it. Cancelling the running one is what
    /// makes this activation possible.
    /// </remarks>
    DifferentReindexRunning = 3,
}
