// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

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
/// What the session publishes is the connection and what the server said about it. There is no operation here that
/// composes, stores, or transmits a message — a submission is a change to the world outside the deployment that cannot
/// be undone, so it is issued from a durable record rather than from whoever happens to hold a session.
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
}
