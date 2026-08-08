// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace MailFathom.Cli.Transport;

/// <summary>The certificate a deployment presented, in the terms an operator has to read to decide about it.</summary>
/// <param name="Subject">Who the certificate says the deployment is.</param>
/// <param name="Issuer">Who signed it, which is what says whether an internal authority or nobody vouched for it.</param>
/// <param name="Fingerprint">The SHA-256 fingerprint, which is what a profile pins and what an operator compares.</param>
/// <param name="NotBefore">When it starts being valid, in UTC.</param>
/// <param name="NotAfter">When it stops being valid, in UTC.</param>
/// <param name="ValidationFailure">Why this machine would not accept it on its own.</param>
/// <remarks>
/// MailFathom's own value rather than an <see cref="X509Certificate2" />, because the decision this feeds is an
/// operator's and everything above the transport deals in what they were shown: five fields to read and one to compare.
/// It also keeps the certificate object itself, which owns unmanaged state, from travelling up into a command that would
/// have to remember to dispose it.
/// </remarks>
internal sealed record PresentedCertificate(
    string Subject,
    string Issuer,
    string Fingerprint,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    string ValidationFailure)
{
    /// <summary>Describes a certificate as it was presented, together with why it was not accepted.</summary>
    /// <param name="certificate">The certificate the deployment presented.</param>
    /// <param name="errors">What the platform found wrong with it, which is <see cref="SslPolicyErrors.None" /> for a certificate refused only by a pin.</param>
    /// <param name="chain">The chain the platform built, or <see langword="null" /> when it built none.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="certificate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The validity window is converted rather than merely carried. <see cref="X509Certificate2" /> surfaces it as a
    /// local <see cref="DateTime" />, and while the implicit conversion to <see cref="DateTimeOffset" /> keeps the
    /// instant — which is what the <c>u</c> format an operator reads renders in UTC — a value that is UTC as stored
    /// cannot be rendered as local wall clock by a later caller that formats it some other way.
    /// </remarks>
    internal static PresentedCertificate Describe(
        X509Certificate2 certificate,
        SslPolicyErrors errors,
        X509Chain? chain = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return new PresentedCertificate(
            certificate.Subject,
            certificate.Issuer,
            FingerprintOf(certificate),
            certificate.NotBefore.ToUniversalTime(),
            certificate.NotAfter.ToUniversalTime(),
            DescribeFailure(errors, chain));
    }

    /// <summary>Reports the SHA-256 fingerprint of a certificate, in the form an operator sees elsewhere.</summary>
    /// <param name="certificate">The certificate.</param>
    /// <returns>The fingerprint as colon-separated uppercase hexadecimal.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="certificate" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Colon-separated because that is what <c>openssl x509 -fingerprint -sha256</c> prints and what a deployment's
    /// operator will be reading it from. One form is stored, printed, and compared, so nothing has to convert between
    /// two spellings of the same value.
    /// </remarks>
    internal static string FingerprintOf(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return string.Join(':', Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)).Chunk(2)
            .Select(pair => new string(pair)));
    }

    /// <summary>Reports whether a fingerprint a profile pinned names this certificate.</summary>
    /// <param name="pinnedFingerprint">The fingerprint the profile holds.</param>
    /// <param name="presentedFingerprint">The fingerprint of the certificate the deployment presented.</param>
    /// <returns><see langword="true" /> when the two name the same certificate.</returns>
    /// <remarks>
    /// Separators and case are ignored, because the stored value is a line in a file an operator may have copied from
    /// somewhere that prints it differently. What is compared is the hash itself, and only an equal hash passes.
    /// </remarks>
    internal static bool NamesTheSameCertificate(string? pinnedFingerprint, string? presentedFingerprint) =>
        pinnedFingerprint is { Length: > 0 }
        && presentedFingerprint is { Length: > 0 }
        && string.Equals(
            Normalize(pinnedFingerprint),
            Normalize(presentedFingerprint),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Reports the certificate as the lines an operator reads before answering.</summary>
    /// <returns>The lines, in the order they are written.</returns>
    internal IReadOnlyList<string> Lines() =>
    [
        $"  Subject:     {this.Subject}",
        $"  Issuer:      {this.Issuer}",
        $"  Fingerprint: {this.Fingerprint}",
        $"  Valid:       {this.NotBefore:u} to {this.NotAfter:u}",
        $"  Not trusted: {this.ValidationFailure}",
    ];

    private static string Normalize(string fingerprint) => fingerprint.Replace(":", string.Empty, StringComparison.Ordinal).Trim();

    /// <summary>Says why this machine refused the certificate, in stable English rather than the platform's localized text.</summary>
    /// <remarks>
    /// The chain statuses are named as well as the policy errors, because "the chain is not trusted" and "the chain is
    /// not trusted because its root is unknown" send an operator to different places, and the second is what a
    /// self-signed deployment produces.
    /// </remarks>
    private static string DescribeFailure(SslPolicyErrors errors, X509Chain? chain)
    {
        List<string> reasons = [];

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            reasons.Add("the deployment presented no certificate");
        }

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            reasons.Add("it does not name the host this address reaches");
        }

        if (errors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors))
        {
            var statuses = chain?.ChainStatus ?? [];

            reasons.Add(statuses.Length == 0
                ? "this machine does not trust the chain it was presented with"
                : $"this machine does not trust the chain it was presented with ({string.Join(", ", statuses.Select(status => status.Status))})");
        }

        return reasons.Count == 0 ? "it is not the certificate this profile pinned" : string.Join("; ", reasons);
    }
}
