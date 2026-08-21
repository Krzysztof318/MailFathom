// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;

namespace MailFathom.Domain.Delivery;

/// <summary>Names one message this system sends, in the identity every recipient's client threads it by.</summary>
/// <remarks>
/// <para>
/// It is the <c>Message-ID</c> header without its angle brackets, which is the form the header's two halves are read
/// and written as. The brackets are delimiters of the header rather than part of the identity, and keeping them out is
/// what lets the value be compared, stored, and logged as itself.
/// </para>
/// <para>
/// Minting it is a domain act rather than a formatting detail, because two properties of it are invariants a message
/// cannot be correct without. The left half is unguessable, so nothing outside this deployment can predict the identity
/// of a message it has not seen and forge a reply into its thread. The right half is the sending account's own domain,
/// so the identity is globally unique without any registry — which is the whole of what RFC 5322 asks of it.
/// </para>
/// <para>
/// A minted identity belongs to the outgoing record it was composed for and is never minted again for that record. The
/// message is stored once and transmitted from storage, so every attempt of one send carries the identity the first
/// composition produced; recomposing between attempts would thread one send as two messages in every client that
/// received both.
/// </para>
/// <para>
/// The type is deliberately not used for the <c>Message-ID</c> of arriving mail, which stays the text a sender wrote.
/// That value is whatever another system chose to put in the header — including shapes this type refuses — and reading
/// it is comparing what was received, never asserting what is correct.
/// </para>
/// </remarks>
public readonly record struct InternetMessageId
{
    /// <summary>How many random bytes the unguessable half is minted from.</summary>
    /// <remarks>
    /// One hundred and twenty-eight bits, which is the width every other unguessable identifier in this system is minted
    /// at. What it has to defeat is somebody guessing the identity of a message they never received, so the bound is the
    /// search space rather than any property of the header.
    /// </remarks>
    private const int EntropyByteCount = 16;

    private InternetMessageId(string value) => this.Value = value;

    /// <summary>Gets the identity as the header carries it, without the angle brackets that delimit it there.</summary>
    public string Value { get; }

    /// <summary>Mints an identity for one message the named domain is sending.</summary>
    /// <param name="domain">The domain of the address the message is sent from, which is the half that makes the identity globally unique.</param>
    /// <returns>An identity nothing outside this process can predict.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="domain" /> is blank, or carries whitespace, a control character, or an at-sign.</exception>
    public static InternetMessageId Mint(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        var trimmedDomain = domain.Trim();

        // An at-sign would put a second one into the value and make the identity split two ways, and either of the other
        // two would end the header early or fold it. None of the three can appear in a domain a mailbox is reached at.
        if (trimmedDomain.Any(static character =>
            character == '@' || char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new ArgumentException(
                "The domain a message identity is minted from names a mail domain.",
                nameof(domain));
        }

        var unguessableHalf = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(EntropyByteCount));

        return new InternetMessageId(
            string.Create(CultureInfo.InvariantCulture, $"{unguessableHalf}@{trimmedDomain}"));
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}
