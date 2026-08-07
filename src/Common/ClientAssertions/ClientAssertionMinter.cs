// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MailFathom.Common.ClientAssertions;

/// <summary>Produces the assertion a client presents, signed with the private half of its key pair.</summary>
/// <remarks>
/// <para>
/// One assertion per request, minted and discarded. There is nothing to cache and nothing to renew: the credential is
/// derived from a key the client already holds, so making a second one costs one signature and removes every question
/// about what a stored copy of the first one would still be good for.
/// </para>
/// <para>
/// The document is written directly rather than through a token library. What a JSON Web Token is here is fully stated
/// by <see cref="ClientAssertion" /> — three claims, one declared type, and an algorithm that follows from the key — so
/// building it costs a few lines, and the command that carries it ships as one trimmed binary that has no reason to
/// bring a token stack along for them.
/// </para>
/// </remarks>
public static class ClientAssertionMinter
{
    /// <summary>Mints one assertion for one surface.</summary>
    /// <param name="signingKey">The private half of the client's key pair, whose kind decides the signature algorithm.</param>
    /// <param name="audience">The surface the assertion is presented to, which is one of the audiences <see cref="ClientAssertion" /> names.</param>
    /// <param name="mintedAt">The moment the assertion is minted, from which its expiry is measured.</param>
    /// <returns>The compact serialization, ready to present as a bearer credential.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="signingKey" /> or <paramref name="audience" /> is <see langword="null" />.</exception>
    /// <exception cref="NotSupportedException">Thrown when the key is of a kind no permitted signature algorithm covers.</exception>
    /// <remarks>
    /// The replay identifier is 128 bits from the cryptographic generator, so two assertions never collide by accident
    /// and no client can be made to mint one an endpoint has already seen.
    /// </remarks>
    public static string Mint(AsymmetricAlgorithm signingKey, string audience, DateTimeOffset mintedAt)
    {
        ArgumentNullException.ThrowIfNull(signingKey);
        ArgumentNullException.ThrowIfNull(audience);

        var algorithmName = ClientAssertionSignature.AlgorithmFor(signingKey)
            ?? throw new NotSupportedException("The key is of a kind no permitted signature algorithm covers.");

        var header = Encode(WriteHeader(algorithmName));
        var payload = Encode(WritePayload(audience, mintedAt + ClientAssertion.MintedLifetime));

        var signingInput = $"{header}.{payload}";
        var signature = ClientAssertionSignature.Sign(signingKey, Encoding.ASCII.GetBytes(signingInput));

        return $"{signingInput}.{Base64Url.EncodeToString(signature)}";
    }

    private static byte[] WriteHeader(string algorithmName)
    {
        var document = new ArrayBufferWriter<byte>();

        using (var json = new Utf8JsonWriter(document))
        {
            json.WriteStartObject();
            json.WriteString("alg", algorithmName);
            json.WriteString("typ", ClientAssertion.DeclaredType);
            json.WriteEndObject();
        }

        return document.WrittenSpan.ToArray();
    }

    private static byte[] WritePayload(string audience, DateTimeOffset expiresAt)
    {
        var document = new ArrayBufferWriter<byte>();

        using (var json = new Utf8JsonWriter(document))
        {
            json.WriteStartObject();
            json.WriteString(ClientAssertion.AudienceClaimName, audience);
            json.WriteNumber(ClientAssertion.ExpiresAtClaimName, expiresAt.ToUnixTimeSeconds());
            json.WriteString(ClientAssertion.IdentifierClaimName, MintIdentifier());
            json.WriteEndObject();
        }

        return document.WrittenSpan.ToArray();
    }

    private static string MintIdentifier() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));

    private static string Encode(ReadOnlySpan<byte> document) => Base64Url.EncodeToString(document);
}
