// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Cli.Transport;

/// <summary>Decides which certificate one connection may accept, and remembers the one it turned away.</summary>
/// <remarks>
/// <para>
/// Two postures and no third. Without a pin the platform's own answer stands, which is the posture every deployment with
/// a publicly trusted certificate keeps. With one, exactly the pinned certificate is accepted and every other is refused
/// — including one that would have validated on its own, because a profile that pinned a certificate said which
/// deployment it is talking to and a substitution is the event the pin exists to catch.
/// </para>
/// <para>
/// Refusing is not enough on its own. A refused handshake reaches a command as a transport failure with no certificate
/// in it, so what was presented is recorded here instead: it is what <c>login</c> shows an operator before asking, and
/// what every other command names when the certificate has changed under a pinned profile.
/// </para>
/// </remarks>
internal sealed class ServerCertificatePolicy
{
    private readonly string? pinnedCertificateFingerprint;

    /// <summary>Initializes a policy for one connection.</summary>
    /// <param name="pinnedCertificateFingerprint">The SHA-256 fingerprint this connection accepts, or <see langword="null" /> to require an ordinary chain.</param>
    internal ServerCertificatePolicy(string? pinnedCertificateFingerprint) =>
        this.pinnedCertificateFingerprint = pinnedCertificateFingerprint;

    /// <summary>Gets the certificate this policy refused, or <see langword="null" /> when it has refused none.</summary>
    internal PresentedCertificate? Refused { get; private set; }

    /// <summary>Gets a value indicating whether this connection is bound to one certificate rather than to a chain.</summary>
    internal bool IsPinned => this.pinnedCertificateFingerprint is { Length: > 0 };

    /// <summary>Decides whether the certificate a deployment presented may be accepted.</summary>
    /// <param name="certificate">The certificate the deployment presented, or <see langword="null" /> when it presented none.</param>
    /// <param name="chain">The chain the platform built, or <see langword="null" /> when it built none.</param>
    /// <param name="errors">What the platform found wrong with the certificate.</param>
    /// <returns><see langword="true" /> when the connection may proceed.</returns>
    /// <remarks>Called on the handshake path, so it decides and records and does nothing else; asking the operator anything from here would put a prompt inside a connection attempt.</remarks>
    internal bool Accepts(X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (certificate is null)
        {
            this.Refused = new PresentedCertificate(
                Subject: string.Empty,
                Issuer: string.Empty,
                Fingerprint: string.Empty,
                NotBefore: default,
                NotAfter: default,
                "the deployment presented no certificate");

            return false;
        }

        if (this.IsPinned)
        {
            var presented = PresentedCertificate.FingerprintOf(certificate);

            if (PresentedCertificate.NamesTheSameCertificate(this.pinnedCertificateFingerprint, presented))
            {
                return true;
            }

            this.Refused = PresentedCertificate.Describe(certificate, errors, chain);

            return false;
        }

        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        this.Refused = PresentedCertificate.Describe(certificate, errors, chain);

        return false;
    }

    /// <summary>Says why the connection was refused, in a sentence naming what an operator does about it.</summary>
    /// <param name="address">The address the connection was aimed at.</param>
    /// <returns>The sentence, or <see langword="null" /> when no certificate was refused.</returns>
    internal string? DescribeRefusal(Uri? address)
    {
        if (this.Refused is not { } presented)
        {
            return null;
        }

        var deployment = address is null ? "the deployment" : $"the deployment at {address.GetLeftPart(UriPartial.Authority)}";

        return this.IsPinned
            ? $"{deployment} presented a certificate this profile has not pinned. Pinned: {this.pinnedCertificateFingerprint}. Presented: {presented.Fingerprint} (subject {presented.Subject}, issuer {presented.Issuer}). Nothing was sent. Sign in again to review the new certificate and accept it, or find out why it changed."
            : $"{deployment} presented a certificate this machine does not trust: {presented.ValidationFailure}. Nothing was sent. Sign in again to review it and accept it for this profile.";
    }
}
