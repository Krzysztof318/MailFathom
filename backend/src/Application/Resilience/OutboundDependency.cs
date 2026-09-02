// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Resilience;

/// <summary>Names one class of outbound dependency whose failures share a retry, timeout, and load-shedding budget.</summary>
/// <remarks>
/// <para>
/// The set is deliberately coarse. A class exists when its failure modes and its safe repetition rules differ from
/// every other class, not when a new call site appears, because each class is one configurable pipeline an operator
/// has to understand and tune.
/// </para>
/// <para>
/// The enumeration is also the pipeline key, so a value that is not declared here resolves nothing and fails loudly
/// instead of silently executing an operation with no resilience at all.
/// </para>
/// </remarks>
public enum OutboundDependency
{
    /// <summary>Connecting, negotiating TLS with, and authenticating an IMAP mailbox session.</summary>
    /// <remarks>
    /// Establishment is separated from retrieval because a rejected credential must never be repeated: repeating it
    /// can lock the mailbox account, which is a worse outcome than the failed synchronization run.
    /// </remarks>
    MailboxSessionEstablishment = 0,

    /// <summary>Listing, fetching, and streaming mailbox data over an established IMAP session.</summary>
    MailboxDataRetrieval = 1,

    /// <summary>Submitting an email to the SMTP server.</summary>
    /// <remarks>
    /// The only failure repeated here is a server's explicit temporary rejection. A submission that ended in an
    /// ambiguous transport failure may already have been accepted, and a repeated delivery is visible in the
    /// recipient's mailbox.
    /// </remarks>
    EmailDelivery = 2,

    /// <summary>Executing a command or query against the local PostgreSQL database.</summary>
    DatabaseCommandExecution = 3,

    /// <summary>Invoking a chat or embedding provider.</summary>
    AiProviderInvocation = 4,

    /// <summary>Exchanging a configured OAuth grant for a mailbox access token at an authorization server.</summary>
    /// <remarks>
    /// It is its own class rather than part of <see cref="MailboxSessionEstablishment" /> because the two disagree
    /// about repetition. Establishment must never repeat a rejected credential, since doing so can lock the mailbox
    /// account; a token request carries no mailbox password, and an authorization server answers an overload with an
    /// ordinary transient HTTP status that is safe to retry. Separating them is also what keeps a token request issued
    /// during establishment from nesting one retry budget inside another.
    /// </remarks>
    MailAuthorizationServerInvocation = 5,

    /// <summary>Calling the S3-compatible endpoint a deployment stores message content in.</summary>
    /// <remarks>
    /// It is its own class rather than part of <see cref="DatabaseCommandExecution" /> because the two are different
    /// remote parties with different failure modes: the database is local and its transient failures clear in
    /// milliseconds, while an object-storage endpoint may be across a network, rate-limits with an HTTP status, and
    /// refuses a rejected credential identically however often it is asked. A deployment storing content in a bucket
    /// still runs every metadata write against the database, so one being unavailable must not open the other's circuit.
    /// </remarks>
    ObjectStorageInvocation = 6,
}
