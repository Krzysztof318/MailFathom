// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Embeddings.Generation;

/// <summary>How one message's turn at being embedded ended.</summary>
/// <remarks>
/// Three of the four are conditions of the instance rather than of the message, and they are apart because they ask an
/// operator for different things: activate a profile, reconcile a declaration with what was activated, or wait for a
/// provider. Only <see cref="Embedded" /> says something about the mail.
/// </remarks>
public enum StoredEmailEmbeddingOutcome
{
    /// <summary>Every passage of the message now carries a vector under the active profile.</summary>
    /// <remarks>Reached with a count of zero by a message that was already current, which is the ordinary result of offering one twice.</remarks>
    Embedded = 0,

    /// <summary>This instance has activated no profile, so there is no space to place a passage in.</summary>
    /// <remarks>Not a failure. An instance serving lexical search alone is a supported deployment, and this is what it looks like from here.</remarks>
    NoActiveProfile = 1,

    /// <summary>The generator this process is configured with produces vectors of a different space than the active profile records.</summary>
    /// <remarks>
    /// Terminal until an operator acts, and refused rather than written: vectors of another geometry stored under this
    /// profile would make retrieval quietly worse instead of failing, which is the hardest kind of defect to attribute.
    /// It is what an edited declaration nobody activated looks like from the generation path.
    /// </remarks>
    GeneratorDisagreesWithProfile = 2,

    /// <summary>A provider call ended without vectors, and <see cref="StoredEmailEmbeddingRun.Failure" /> says how.</summary>
    /// <remarks>Whatever was committed before the failure stays durable; the passages the call was for keep waiting.</remarks>
    ProviderFailed = 3,
}
