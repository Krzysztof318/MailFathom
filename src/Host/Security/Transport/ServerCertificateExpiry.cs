// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Host.Security.Transport;

/// <summary>Decides when a server certificate is close enough to expiry for startup to say so.</summary>
/// <remarks>
/// The window is one rule for every listener MailFathom presents a certificate on. Each holder writes its own record —
/// one names the HTTPS profile, the other has no profile to name — but when to write the warning rather than the
/// ordinary notice is not a per-endpoint decision, and two copies of it would let one listener start warning a month
/// before the other.
/// </remarks>
internal static class ServerCertificateExpiry
{
    /// <summary>How close to expiry a certificate has to be before startup reports it as something to act on.</summary>
    /// <remarks>Thirty days is the window in which a renewal is still routine rather than urgent, and it is long enough that an operator reading the log on a Monday has not already lost the weekend.</remarks>
    private static readonly TimeSpan NoticeWindow = TimeSpan.FromDays(30);

    /// <summary>Reads the instant a certificate stops being usable.</summary>
    /// <param name="leaf">The certificate a listener presents.</param>
    /// <returns>The expiry instant in UTC, which is what a record states and what an operator renews against.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="leaf" /> is <see langword="null" />.</exception>
    internal static DateTimeOffset ExpirationOf(X509Certificate2 leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);

        return leaf.NotAfter.ToUniversalTime();
    }

    /// <summary>Reports whether an expiry is near enough to warn about.</summary>
    /// <param name="expiration">The instant the certificate stops being usable.</param>
    /// <param name="readAt">The instant the question is asked, which startup takes from the injected clock.</param>
    /// <returns><see langword="true" /> when the certificate expires inside the notice window or has expired already.</returns>
    /// <remarks>An already-expired certificate answers <see langword="true" /> as well, though nothing reaches this with one: the loader refuses it before a holder publishes it.</remarks>
    internal static bool IsExpiringSoon(DateTimeOffset expiration, DateTimeOffset readAt) =>
        expiration - readAt <= NoticeWindow;
}
