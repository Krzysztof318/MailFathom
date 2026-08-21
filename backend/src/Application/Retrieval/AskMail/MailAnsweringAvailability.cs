// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>Says whether this deployment can answer a question about the mailbox right now.</summary>
/// <remarks>
/// <para>
/// The set separates the two reasons a question cannot be answered, because they ask different things of whoever reads
/// them. A deployment that answers no questions is working as configured and nothing about it is going to change; one
/// that answers them and currently cannot has an operator with something to repair.
/// </para>
/// <para>
/// It is an admission decision rather than a report of the last provider call, which is the difference from
/// <see cref="Emails.Search.SemanticSearchCapability" />. What reads this decides whether the tool is offered at all, so
/// a value that stayed <see cref="Degraded" /> for as long as the last recorded failure did would leave a repaired
/// deployment permanently unable to demonstrate it: nothing else calls the chat endpoint, so nothing else would ever
/// record a better state.
/// </para>
/// </remarks>
public enum MailAnsweringAvailability
{
    /// <summary>This deployment does not answer questions about mail at all.</summary>
    /// <remarks>It declared no chat endpoint, or it embeds no mail, and either one makes answering something it was never configured to do.</remarks>
    Inactive = 0,

    /// <summary>This deployment answers questions and nothing currently stops it.</summary>
    Available = 1,

    /// <summary>This deployment answers questions and currently cannot.</summary>
    /// <remarks>A refused credential, an unreachable endpoint, or an embedding profile whose vectors nothing can place a query beside. Nothing about a request causes it and no request repairs it.</remarks>
    Degraded = 2,
}
