// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Mcp.Tools;

/// <summary>Reports how a search found what it returned, as the protocol spells it.</summary>
/// <remarks>
/// <para>
/// The field exists from the first release although it names one value today. Retrieval becomes hybrid when the RAG work
/// lands, and a client that had been given no way to tell lexical results from semantic ones would either have to infer
/// it from a server version or discover the change by reasoning wrongly about the results. Publishing the mode now costs
/// one field and means the later work widens this enumeration instead of reshaping a response.
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
}
