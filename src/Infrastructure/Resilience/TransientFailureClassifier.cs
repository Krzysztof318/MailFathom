// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using System.Data.Common;
using System.Net;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailMcp.Application.Persistence;
using MailMcp.Application.Resilience;
using MailMcp.Infrastructure.Mail;

namespace MailMcp.Infrastructure.Resilience;

/// <summary>Classifies outbound failures by protocol family, so only failures that can clear on their own are repeated.</summary>
/// <remarks>
/// <para>
/// The decision is made from the failure's type and its protocol status code alone. Nothing from a message, a
/// mailbox address, a credential, or a provider payload takes part in it, and nothing from the failure is recorded
/// here.
/// </para>
/// <para>
/// Every family follows the same shape: the terminal cases are matched first and everything unrecognized is terminal
/// too. A failure whose meaning is unknown is not repeated, because an unrecognized rejection repeated against a mail
/// server is exactly what locks a mailbox account.
/// </para>
/// </remarks>
internal sealed class TransientFailureClassifier : ITransientFailureClassifier
{
    /// <inheritdoc />
    public bool IsTransientFailure(OutboundDependency dependency, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var classifyFailureOfFamily = dependency switch
        {
            OutboundDependency.MailboxSessionEstablishment
                or OutboundDependency.MailboxDataRetrieval => IsTransientMailboxFailure,
            OutboundDependency.EmailDelivery => IsRepeatableDeliveryFailure,
            OutboundDependency.DatabaseCommandExecution => IsTransientDatabaseFailure,
            OutboundDependency.AiProviderInvocation => (Func<Exception, bool>)IsTransientProviderFailure,
            _ => throw new ArgumentOutOfRangeException(
                nameof(dependency),
                dependency,
                "No transient-failure classification is defined for this outbound dependency class."),
        };

        // A caller that cancelled is not a dependency that failed, and the distinction has to survive every family.
        return failure is not OperationCanceledException && classifyFailureOfFamily(failure);
    }

    /// <summary>Classifies an IMAP mailbox failure, where reading is free to be repeated.</summary>
    /// <remarks>
    /// A rejected credential, an unusable TLS handshake, and a command the server refused are decisions the server
    /// will repeat identically. A dropped connection or a protocol desynchronization is not: the next session starts
    /// clean, and a repeated read changes nothing on the server.
    /// </remarks>
    private static bool IsTransientMailboxFailure(Exception failure) => failure switch
    {
        MailKit.Security.AuthenticationException => false,
        MailKit.Security.SslHandshakeException => false,
        System.Security.Authentication.AuthenticationException => false,
        ServiceNotAuthenticatedException => false,
        MailAuthenticationMechanismUnavailableException => false,
        CommandException => false,
        ProtocolException => true,
        ServiceNotConnectedException => true,
        _ => IsTransientTransportFailure(failure),
    };

    /// <summary>Classifies an SMTP submission failure, where an ambiguous outcome has to count as delivered.</summary>
    /// <remarks>
    /// A connection lost between the message data and the server's final reply leaves the client unable to tell an
    /// accepted message from a rejected one, and MailKit surfaces exactly that as an ordinary protocol, socket, or
    /// I/O failure — the same types a pre-submission failure produces. No outbox can separate the two after the fact,
    /// so repeating any of them risks a second copy in a recipient's mailbox.
    /// <para>
    /// Only an explicit temporary rejection is repeated: a 4yz reply is the server stating that it did not take the
    /// message and that the client should try later. Everything else, including a failure that happened before the
    /// message was ever submitted, is terminal here and left to the outbox to re-drive under its own idempotency.
    /// </para>
    /// </remarks>
    private static bool IsRepeatableDeliveryFailure(Exception failure) =>
        failure is SmtpCommandException rejection && IsTemporarySmtpRejection(rejection.StatusCode);

    /// <summary>Classifies a PostgreSQL failure.</summary>
    /// <remarks>
    /// The provider answers the transient question itself through <see cref="DbException.IsTransient" />, which
    /// covers the PostgreSQL SQLSTATE classes worth repeating. A concurrency conflict is deliberately terminal here:
    /// the application's commit policy already owns that retry, and repeating it at two layers would multiply the
    /// attempts against the same rows.
    /// </remarks>
    private static bool IsTransientDatabaseFailure(Exception failure) => failure switch
    {
        PersistenceConcurrencyConflictException => false,
        DbException databaseFailure => databaseFailure.IsTransient,
        _ => IsTransientTransportFailure(failure),
    };

    /// <summary>Classifies a chat or embedding provider failure.</summary>
    /// <remarks>An HTTP status is the provider's own statement about whether the request may be sent again; a request that never reached a status failed in transport.</remarks>
    private static bool IsTransientProviderFailure(Exception failure) => failure switch
    {
        HttpRequestException requestFailure => IsTransientHttpStatus(requestFailure.StatusCode),
        _ => IsTransientTransportFailure(failure),
    };

    private static bool IsTransientTransportFailure(Exception failure) =>
        failure is SocketException or IOException or TimeoutException;

    /// <summary>Reports whether an SMTP reply is a temporary negative completion, which the RFC 5321 4yz class defines as safe to repeat.</summary>
    private static bool IsTemporarySmtpRejection(SmtpStatusCode statusCode) =>
        (int)statusCode is >= 400 and < 500;

    /// <summary>Reports whether an HTTP status invites the same request again; an absent status means the response never arrived.</summary>
    private static bool IsTransientHttpStatus(HttpStatusCode? statusCode) => statusCode switch
    {
        null => true,
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => true,
        _ => (int)statusCode >= 500,
    };
}
