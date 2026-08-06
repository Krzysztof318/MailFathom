// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailKit.Net.Imap;
using MailKit.Security;
using NSubstitute;

namespace MailFathom.IntegrationTests.Mailbox;

/// <summary>A real IMAP client that reports one fewer capability than the server actually advertises.</summary>
/// <remarks>
/// <para>
/// The orchestrated mail server advertises <c>MOVE</c>, so nothing a test does to the mailbox can make MailFathom take
/// the fallback relocation path. That path is the main one for the servers that lack the extension, and leaving it
/// proven only against a substituted protocol boundary would leave the three commands it issues — <c>UID COPY</c>,
/// <c>UID STORE +FLAGS (\Deleted)</c>, and <c>UID EXPUNGE</c> — never once exercised against a real server.
/// </para>
/// <para>
/// So one thing is faked and one thing only: the advertised capability set. Every command still travels the wire to
/// GreenMail over a real <see cref="ImapClient" />, and the folder state a test reads back afterwards is the server's
/// own. Only the members the write connection actually uses are forwarded, because a member that is never called
/// cannot be forwarded wrongly.
/// </para>
/// </remarks>
internal static class CapabilityMaskedImapClient
{
    /// <summary>Builds a client that speaks to the real server while hiding the supplied capabilities from MailFathom.</summary>
    /// <param name="hiddenCapabilities">The capabilities to report as absent.</param>
    /// <returns>The masking client, whose disposal disposes the real one it wraps.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the real client passes to the returned masking client, whose Dispose forwards to it; the write connection disposes that one.")]
    internal static IImapClient HidingCapabilities(ImapCapabilities hiddenCapabilities)
    {
        var realClient = new ImapClient();
        var maskedClient = Substitute.For<IImapClient>();

        maskedClient.Capabilities.Returns(_ => realClient.Capabilities & ~hiddenCapabilities);
        maskedClient.AuthenticationMechanisms.Returns(_ => realClient.AuthenticationMechanisms);
        maskedClient.IsConnected.Returns(_ => realClient.IsConnected);

        maskedClient.ConnectAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<SecureSocketOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(call => realClient.ConnectAsync(
                call.ArgAt<string>(0),
                call.ArgAt<int>(1),
                call.ArgAt<SecureSocketOptions>(2),
                call.ArgAt<CancellationToken>(3)));

        maskedClient.AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => realClient.AuthenticateAsync(
                call.ArgAt<string>(0),
                call.ArgAt<string>(1),
                call.ArgAt<CancellationToken>(2)));

        maskedClient.GetFolderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => realClient.GetFolderAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)));

        maskedClient.DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(call => realClient.DisconnectAsync(call.ArgAt<bool>(0), call.ArgAt<CancellationToken>(1)));

        maskedClient.When(client => client.Dispose()).Do(_ => realClient.Dispose());

        return maskedClient;
    }
}
