// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Something a run knows about its own reach, which the plan states rather than leaving to be inferred.</summary>
/// <remarks>
/// A closed set rather than a sentence, for the same reason the block catalogue is closed: a limitation written as prose
/// is a limitation a client cannot draw differently from the answer, and a reader skims past it. Each member names
/// something that makes an answer narrower than the question, and a plan carrying none is a run that reached everything
/// it was asked about.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PresentationLimitation>))]
public enum PresentationLimitation
{
    /// <summary>Retrieval stopped at its bound, so mail matching the question was left unread.</summary>
    RetrievalTruncated = 0,

    /// <summary>Some mail the answer would have rested on could not be read.</summary>
    SourcesUnavailable = 1,

    /// <summary>Ranking by meaning was unavailable, so only the words the question used were matched.</summary>
    SemanticRankingUnavailable = 2,

    /// <summary>Part of the local copy is behind the mail server, so the answer may describe a state that has moved on.</summary>
    LocalCopyBehind = 3,

    /// <summary>The plan reached its own bound, so blocks the run composed were left out of it.</summary>
    BlocksOmitted = 4,

    /// <summary>The question reached beyond the mail this deployment holds, so part of it was answered from nothing.</summary>
    OutsideRetainedMail = 5,
}
