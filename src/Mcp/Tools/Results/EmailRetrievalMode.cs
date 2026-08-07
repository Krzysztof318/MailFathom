// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Reports how a search found what it returned, as the protocol spells it.</summary>
/// <remarks>
/// <para>
/// Which value a search reports is a fact about that one call rather than about the deployment. An instance configured
/// for hybrid retrieval answers lexically for as long as its embedding provider is unreachable, so a client that read a
/// capability instead of this field would draw the wrong conclusion about why a message it expected is missing.
/// </para>
/// <para>
/// The transport carries its own enumeration for the reason <see cref="ListEmailsDirection" /> does: the member names are
/// the published wire values, so they belong to the boundary that publishes them.
/// </para>
/// </remarks>
internal enum EmailRetrievalMode
{
    /// <summary>The results were matched and ranked by full-text search over the words the mail is written in.</summary>
    /// <remarks>A word the query does not contain finds nothing, however close its meaning: there is no embedding, no chat model, and no query rewriting anywhere in this path.</remarks>
    Lexical = 0,

    /// <summary>The results were matched both by full-text search and by embedding similarity, and the two orderings were combined.</summary>
    /// <remarks>A message can appear here without carrying any of the query's words. Still no chat model and no query rewriting: the query is embedded and compared, never interpreted.</remarks>
    Hybrid = 1,
}
