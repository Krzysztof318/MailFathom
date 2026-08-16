// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Sockets;
using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Opens the transport a submission session is then spoken over.</summary>
/// <remarks>
/// The connection is made here rather than left to the mail client because that is what separates the two stages the
/// client performs in one call: opening a transport, and negotiating encryption and reading the greeting over it. Each
/// gets a budget of its own that way, so a submission endpoint that accepts a connection and then says nothing is
/// reported as a server that never greeted rather than as one that could not be reached.
/// </remarks>
internal static class SubmissionSocketConnector
{
    /// <summary>Connects a stream socket to the submission endpoint.</summary>
    /// <param name="host">The submission server host name.</param>
    /// <param name="port">The submission server port.</param>
    /// <param name="cancellationToken">Cancels the connection attempt, including the name resolution in front of it.</param>
    /// <returns>The connected socket, whose ownership passes to the caller.</returns>
    /// <remarks>
    /// The socket disables Nagle's algorithm, because SMTP is a sequence of short commands each waiting on a reply and
    /// coalescing them buys nothing while delaying every one of them.
    /// </remarks>
    [RequiresIntegrationCoverage]
    internal static async Task<Socket> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        try
        {
            await socket.ConnectAsync(host, port, cancellationToken);

            return socket;
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }
}
