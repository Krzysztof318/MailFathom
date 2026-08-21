// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Sockets;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Stands in for the transport a delivery attempt opens, recording where it was opened to.</summary>
/// <remarks>
/// The socket is never connected to anything and reaches a scripted client that does nothing with it, so the ownership
/// the adapter hands to the mail library in a deployment is held here instead. That is why this is a type a test
/// disposes rather than a delegate it calls: a real descriptor is allocated per attempt, and nothing else in a unit
/// test would ever release it.
/// </remarks>
internal sealed class ScriptedSubmissionTransport : IDisposable
{
    private readonly List<Socket> openedSockets = [];

    private readonly List<(string Host, int Port)> requestedEndpoints = [];

    /// <summary>Gets every host and port an attempt opened a transport to, in order.</summary>
    internal IReadOnlyList<(string Host, int Port)> RequestedEndpoints => this.requestedEndpoints;

    /// <summary>Hands out one socket per attempt, recording the endpoint it was asked for.</summary>
    internal Task<Socket> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.requestedEndpoints.Add((host, port));

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        this.openedSockets.Add(socket);

        return Task.FromResult(socket);
    }

    /// <summary>Releases every socket handed out, including the ones the adapter already disposed itself.</summary>
    public void Dispose()
    {
        foreach (var socket in this.openedSockets)
        {
            socket.Dispose();
        }

        this.openedSockets.Clear();
    }
}
