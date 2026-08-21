// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;

namespace MailFathom.Domain.Delivery;

/// <summary>Tells apart whoever asked for one send from everybody else who has asked for one, without keeping who they are.</summary>
/// <remarks>
/// <para>
/// This is the other half of the question <see cref="OutgoingEmailRequester" /> answers, and the two are different
/// facts. The requester says which authored act asked, so that asking twice is one send; this says which admitted
/// principal performed the act, so that a caller reading a send back or withdrawing one reaches what it queued and
/// nothing anybody else did. Nothing about a send is decided from it — it is compared for sameness and never read.
/// </para>
/// <para>
/// It is a fingerprint rather than the identity, and both halves of that are deliberate. An admitted identity has no
/// bound anywhere above this — an authorization server's issuer and its own identifier for a person arrive as whatever
/// they are — so keeping the text would put an unbounded value into a bounded column and turn a long identity into a
/// send that cannot be written down. And an outgoing record already says who this mailbox wrote to and when; adding the
/// remote party's identifier for the person who asked would widen that for nothing, because sameness is the only
/// question ever put to it.
/// </para>
/// <para>
/// The fingerprint is not a secret and is not treated as one. Identities are short and drawn from a small set — an
/// operator's own name for a credential, the fixed word for a caller nothing tells apart — so anybody holding the
/// candidate list can confirm a guess. What it buys is a fixed width and one less identifier at rest, and a reader
/// should expect nothing else of it.
/// </para>
/// </remarks>
public sealed record OutgoingEmailPrincipal
{
    /// <summary>The number of characters a fingerprint is written as, which bounds the column it is stored in.</summary>
    /// <remarks>SHA-256 in lower-case hexadecimal, so the width is fixed whatever the identity behind it was.</remarks>
    public const int FingerprintLength = 64;

    private OutgoingEmailPrincipal(string fingerprint) => this.Fingerprint = fingerprint;

    /// <summary>Gets the fixed-width value two principals are compared by.</summary>
    public string Fingerprint { get; }

    /// <summary>Fingerprints the identity a unit of work was admitted under.</summary>
    /// <param name="principalIdentity">What admitted whoever asked for the send.</param>
    /// <returns>The principal that identity is recorded as.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="principalIdentity" /> is empty or white space, which names nobody at all.</exception>
    public static OutgoingEmailPrincipal Of(string principalIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalIdentity);

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(principalIdentity));

        return new OutgoingEmailPrincipal(Convert.ToHexStringLower(digest));
    }

    /// <summary>Restores a principal from the fingerprint a stored row holds.</summary>
    /// <param name="fingerprint">The stored value.</param>
    /// <returns>The principal that row names.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fingerprint" /> is not a fingerprint this system writes.</exception>
    /// <remarks>
    /// It validates rather than accepting whatever the column held, because the value's whole purpose is that two of
    /// them are equal or are not: a row carrying anything else would compare unequal to every caller and silently hide
    /// a send from the one who queued it, which is a fault to raise rather than a record to serve.
    /// </remarks>
    public static OutgoingEmailPrincipal Create(string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        if (fingerprint.Length != FingerprintLength
            || !fingerprint.All(char.IsAsciiHexDigitLower))
        {
            throw new ArgumentException(
                $"An outgoing email principal fingerprint is {FingerprintLength} lower-case hexadecimal characters.",
                nameof(fingerprint));
        }

        return new OutgoingEmailPrincipal(fingerprint);
    }

    /// <inheritdoc />
    public override string ToString() => this.Fingerprint;
}
