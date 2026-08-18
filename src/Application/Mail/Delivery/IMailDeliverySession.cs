// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Transmission;

namespace MailFathom.Application.Mail.Delivery;

/// <summary>Holds one account authenticated against its submission server for as long as a delivery needs it.</summary>
/// <remarks>
/// <para>
/// This is the only type in MailFathom able to reach a submission server, and it is deliberately a different type from
/// every mailbox session rather than a mode of one. Synchronization, reconciliation, content retrieval, and every MCP
/// tool reach a server through a session that exposes no way to obtain this one, so a refactor cannot give a read path
/// the ability to send: a read path never holds something that has it.
/// </para>
/// <para>
/// What the session publishes is the connection, what the server said about it, and the one act that reaches the
/// outside world. It neither composes nor stores a message: a submission cannot be undone, so it is issued from a
/// durable record that already says what is being transmitted and how far a previous attempt got.
/// </para>
/// <para>
/// One session is used by one caller at a time and is not safe for concurrent use. It owns a connection for as long as
/// it is open, so it is short-lived by design and the caller disposes it when its work ends.
/// </para>
/// </remarks>
public interface IMailDeliverySession : IAsyncDisposable
{
    /// <summary>Gets what the server declared it will accept, read from the greeting this session was opened with.</summary>
    /// <remarks>
    /// The value belongs to this session's connection rather than to the account, because a submission endpoint behind
    /// a load balancer answers two connections with two greetings. A caller that cached it across sessions would bound
    /// a message against a server it is no longer talking to.
    /// </remarks>
    MailDeliveryCapabilities Capabilities { get; }

    /// <summary>Offers the envelope and transmits the message, reporting what the server said about each.</summary>
    /// <param name="request">Who the message is from, who is still owed it, and the bytes to transmit.</param>
    /// <param name="envelope">Filled with what the server answers about each address, whether or not this call returns.</param>
    /// <param name="cancellationToken">Cancels the submission; the record the caller holds decides what a cancelled one means.</param>
    /// <returns>What the server answered about the message.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> or <paramref name="envelope" /> is <see langword="null" />.</exception>
    /// <exception cref="MailDeliveryUnavailableException">Thrown when the submission server did not serve the operation within its configured resilience budget.</exception>
    /// <exception cref="TimeoutException">Thrown when the server stopped answering within the command budget, which says nothing about what it received.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller stopped waiting, which likewise says nothing about what the server received.</exception>
    /// <remarks>
    /// <para>
    /// It is called once per attempt and never repeated inside one. A submission that failed part-way cannot be told
    /// apart from one that succeeded, so repeating it here would put a second copy in the mailbox of everybody the
    /// envelope had already reached — which is why the delivery pipeline repeats a server's explicit temporary
    /// rejection and nothing else.
    /// </para>
    /// <para>
    /// A server that answered produces a result whatever it said, including a refusal, because either answer settles
    /// what the recipients received. Only a server that answered nothing raises — and the envelope ledger is what the
    /// caller reads then, since an envelope that accepted nobody is proof that no byte of the body went out.
    /// </para>
    /// </remarks>
    Task<MailTransmission> TransmitAsync(
        MailTransmissionRequest request,
        MailEnvelopeLedger envelope,
        CancellationToken cancellationToken);
}
