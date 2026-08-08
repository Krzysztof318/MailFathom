// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Reports what semantic retrieval can do on this server, as the protocol spells it.</summary>
/// <remarks>
/// <para>
/// It answers the question the retrieval mode cannot. A lexical answer from a server that never embeds and a lexical
/// answer from a server whose embedding credential expired look identical in
/// <see cref="EmailRetrievalMode" />, and only one of them means the results are narrower than this deployment intends.
/// A client reading both knows whether to say so.
/// </para>
/// <para>
/// Unlike the retrieval mode, this describes the server rather than the one call — with one exception that matters: a
/// query whose own provider call failed reports <see cref="Degraded" />, because that call is the freshest evidence
/// there is about the provider.
/// </para>
/// <para>
/// The transport carries its own enumeration for the reason <see cref="ListEmailsDirection" /> does: the member names
/// are the published wire values, so they belong to the boundary that publishes them.
/// </para>
/// </remarks>
internal enum SemanticSearchAvailability
{
    /// <summary>This server does not embed mail, so no search is ranked by meaning.</summary>
    /// <remarks>A deliberate deployment rather than a fault: every search is answered lexically, listing and content retrieval are unaffected, and nothing here is waiting to become available on its own.</remarks>
    Inactive = 0,

    /// <summary>This server embeds mail and its provider is answering, so searches are ranked both ways.</summary>
    /// <remarks>An individual call can still answer lexically — a provider that fails during that call reports the degraded value instead — and mail not yet embedded is still absent from the semantic half.</remarks>
    Available = 1,

    /// <summary>This server embeds mail but currently cannot place a query in that space, so searches are answered lexically until it recovers.</summary>
    /// <remarks>Results are narrower than this deployment intends. It is not an error, nothing about the request caused it, and retrying does not help; the server's operator has a credential, an endpoint, or a model declaration to fix, and recovery needs no restart.</remarks>
    Degraded = 2,
}
