// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography.X509Certificates;
using MailFathom.Infrastructure.Certificates;
using MailKit;

namespace MailFathom.Infrastructure.Mail.MailKit;

/// <summary>Arranges the trust of a mail client and ends it, in the one way both protocol adapters end one.</summary>
/// <remarks>
/// The members are declared over <see cref="IMailService" />, which is the contract MailKit already publishes for a
/// message service and which both the mailbox client and the submission client derive from. Nothing here is an
/// abstraction this repository invented: certificate trust, connectedness, and the disconnect are declared there, so
/// sharing the three costs no seam and keeps the mail library's types inside the adapter that owns them.
/// </remarks>
internal static class MailKitClientLifetime
{
    /// <summary>Points the client at the account's configured authority before the handshake that will consult it.</summary>
    /// <param name="client">The client about to greet its server.</param>
    /// <param name="trustedCertificateAuthority">The authority the account pins, or <see langword="null" /> to leave the client's own default in place.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The anchor lives as long as the connection attempt that resolved it, and so does the callback that closes over
    /// it: the client is created per attempt and disposed with it, so no callback outlives the certificate it reads.
    /// An account without a configured authority leaves the client's own validating default untouched.
    /// </remarks>
    internal static void TrustConfiguredCertificateAuthority(
        IMailService client,
        X509Certificate2? trustedCertificateAuthority)
    {
        ArgumentNullException.ThrowIfNull(client);

        if (trustedCertificateAuthority is null)
        {
            return;
        }

        client.ServerCertificateValidationCallback = (_, certificate, chain, sslPolicyErrors) =>
            PrivateAuthorityServerCertificateValidator.IsServerCertificateTrusted(
                trustedCertificateAuthority,
                certificate,
                chain,
                sslPolicyErrors);
    }

    /// <summary>Drops a client its owner has already declared unusable, without speaking the protocol again.</summary>
    /// <param name="client">The client to close without a farewell.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A graceful logout or quit is a command, and a command sent to a server that stopped answering waits for a reply
    /// that may never come — on a client whose only timeout is its own, far beyond the attempt budget, and through a
    /// cancellation token this cleanup has no way to observe. Closing the socket asks the server for nothing and
    /// cannot block on it.
    /// </para>
    /// <para>
    /// The waiting is what makes this more than impoliteness. Every caller is inside an attempt the resilience
    /// pipeline may abandon, so a cleanup that blocked would still be running against a connection object that is not
    /// safe for concurrent use while the next attempt was already using one. That holds on both protocols: a mailbox
    /// connection replaces itself after a dropped read, and a submission session is established under a class that
    /// makes a second attempt of its own.
    /// </para>
    /// <para>
    /// Politeness belongs to the owner's disposal, where the session is ending in order and no attempt is racing it.
    /// </para>
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A connection already being replaced or abandoned must not have its cleanup failure replace the failure that made it unusable.")]
    [SuppressMessage("Roslynator", "RCS1075:Avoid empty catch clause that catches System.Exception", Justification = "There is no second action to take: the connection is already unusable, and the caller is about to rethrow the failure that made it so.")]
    internal static void Abandon(IMailService client)
    {
        ArgumentNullException.ThrowIfNull(client);

        try
        {
            client.Dispose();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Ends a session in order, reporting the first cleanup failure once both cleanups have been attempted.</summary>
    /// <param name="client">The client whose session is ending.</param>
    /// <returns>A task that completes when the client has been disconnected and released.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="client" /> is <see langword="null" />.</exception>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Both cleanup operations must be attempted while the first cleanup failure remains observable.")]
    internal static async ValueTask DisconnectAndDisposeAsync(IMailService client)
    {
        ArgumentNullException.ThrowIfNull(client);

        Exception? firstCleanupException = null;
        try
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(quit: true, CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            firstCleanupException = exception;
        }

        try
        {
            client.Dispose();
        }
        catch (Exception exception)
        {
            firstCleanupException ??= exception;
        }

        if (firstCleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(firstCleanupException).Throw();
        }
    }
}
