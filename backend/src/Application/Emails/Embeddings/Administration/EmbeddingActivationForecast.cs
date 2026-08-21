// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Generations;

namespace MailFathom.Application.Emails.Embeddings.Administration;

/// <summary>What activating the declared geometry would do, read before anything is written.</summary>
/// <remarks>
/// A forecast rather than a result, which is why it is not
/// <see cref="EmbeddingProfileActivationOutcome" />: the two describe the same four situations at opposite ends of one
/// command, and only one of them may claim that something happened. The forecast is what decides the sentence an
/// operator is asked to confirm; the outcome is what the run afterwards reports. A race between them changes the
/// answer, and it is the outcome that is authoritative.
/// </remarks>
public enum EmbeddingActivationForecast
{
    /// <summary>No generation of this geometry exists, so activating would register one and begin a paid reindex.</summary>
    /// <remarks>The one member that spends, which is why it is the only one weighed against the spend ceiling.</remarks>
    WouldStartReindex = 0,

    /// <summary>This geometry is already the generation being built, so activating would leave that reindex running.</summary>
    WouldResumeReindex = 1,

    /// <summary>This geometry is already the generation searches are answered from, so activating would change nothing.</summary>
    AlreadyServing = 2,

    /// <summary>A different generation is being built, so activating would be refused rather than started beside it.</summary>
    DifferentReindexRunning = 3,
}
