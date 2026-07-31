// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

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
}
