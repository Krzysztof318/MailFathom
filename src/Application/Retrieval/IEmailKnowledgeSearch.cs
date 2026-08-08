// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Retrieval;

/// <summary>Finds the mail relevant to a question, as bounded extracts an answer can be written from and traced to.</summary>
/// <remarks>
/// <para>
/// The retrieval side of answering, and the only route by which mail reaches a model. It exists as a port rather than as
/// a call into the search use case because the caller is an orchestration framework: what that framework may ask for is
/// a question in free text, and everything else about the retrieval — which accounts, which folders, how many passages,
/// how much of any message — is decided by whoever composed the run.
/// </para>
/// <para>
/// The scope is a parameter rather than part of the query for exactly that reason. It comes from the caller that has
/// already established what the requester may see, so no instruction reaching the model, and no query the model writes,
/// can widen it. An implementation narrows the scope further where the deployment serves fewer accounts than were named;
/// it never widens it.
/// </para>
/// <para>
/// Nothing here reaches a mail server. A question is answered from what synchronization has already stored and what
/// indexing has already derived, which is what keeps answering independent of IMAP availability, and no call of this port
/// changes any remote state.
/// </para>
/// </remarks>
public interface IEmailKnowledgeSearch
{
    /// <summary>Finds the passages relevant to one query within one scope.</summary>
    /// <param name="scope">The accounts and folders the passages may be drawn from, decided by the caller.</param>
    /// <param name="queryText">What to look for, which is free text a model may have written.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The passages, most relevant first, bounded in number and in the size of each, beside what the lookup considered to produce them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A query that finds nothing, and one that carries no usable text, both produce an empty list. Neither is a failure:
    /// the text is written by a model rather than by a caller who can be told to correct it, and a run whose retrieval
    /// found nothing still has an answer to give.
    /// </para>
    /// <para>
    /// The counts travel with the passages rather than being logged where they arise, because what an operator and an
    /// audit both ask about is a <em>run</em>, and a run makes several lookups. Only a caller that owns the run can add
    /// them up, so an implementation reports its own and adds nothing up itself.
    /// </para>
    /// </remarks>
    Task<EmailKnowledgeLookup> FindPassagesAsync(
        MailboxScope scope,
        string queryText,
        CancellationToken cancellationToken);
}
